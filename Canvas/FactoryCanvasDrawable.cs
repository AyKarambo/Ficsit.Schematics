using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Solver;
using Ficsit.Schematics.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// Immediate-mode renderer for the factory canvas: wires below, node cards above,
/// then interaction adorners (pending wire, rubber band, hover tooltip).
/// World→screen: screen = world · zoom + pan.
/// </summary>
public sealed partial class FactoryCanvasDrawable(AppState state, IconStore icons, NumberFormatService numbers) : IDrawable
{
    public float Zoom { get; set; } = 1f;
    public float PanX { get; set; }
    public float PanY { get; set; }

    public CanvasTheme Theme { get; set; } = CanvasTheme.Dark;

    // Interaction adorners, set by the controller.
    public (PointF From, PointF To)? PendingWire { get; set; }
    public RectF? RubberBand { get; set; }
    public (PointF Screen, string Text)? Tooltip { get; set; }

    /// <summary>The resource marker a dragged extractor would snap to; drawn highlighted.</summary>
    public ResourceNodeInfo? SnapPreviewMarker { get; set; }

    /// <summary>The node currently under the cursor — drives focus highlighting (fade the rest).</summary>
    public FactoryNode? HoverNode { get; set; }

    /// <summary>Opacity for everything outside the focused node's neighborhood.</summary>
    private const float DimAlpha = 0.16f;

    /// <summary>While reordering a port, the world-space insertion slot (x, y, width) drawn as
    /// a short bar between ports.</summary>
    public (float X, float Y, float Width)? PortInsertLine { get; set; }

    /// <summary>While dragging a wire inside an outpost toward a screen edge: true = left rail
    /// (feed this input from outside), false = right rail (send this output out), null = no
    /// hint. Drawn as a translucent rail highlight.</summary>
    public bool? EdgeDropZone { get; set; }

    /// <summary>Last drawn viewport width (screen px).</summary>
    public float ViewportWidth => _viewport.Width;

    /// <summary>True when the world map underlay and resource markers are active (root scope).</summary>
    public bool MapActive => state.Settings.ShowMap && state.Editor.ScopePath.Count == 0;

    private readonly Dictionary<FactoryNode, NodeLayout> _layouts = [];
    private bool _layoutsDirty = true;

    private SizeF _viewport;

    // Text measurement is the dominant per-frame cost when panning (one GetStringSize per
    // port/connection label per frame). The size of a given (text, fontSize) pair never
    // changes — the font is constant — so memoize it; pan holds zoom (hence fontSize) fixed,
    // so every label hits the cache after the first frame. Bounded by a hard cap.
    private readonly Dictionary<(string Text, float FontSize), SizeF> _textSizeCache = [];

    public void InvalidateLayouts() => _layoutsDirty = true;

    public IReadOnlyDictionary<FactoryNode, NodeLayout> Layouts
    {
        get
        {
            if (_layoutsDirty)
            {
                _layouts.Clear();
                var graph = state.Editor.Graph;
                var mapCompact = MapActive;
                // Only the nodes in the current scope (members of the active outpost) are laid out.
                foreach (var node in state.Editor.VisibleNodes)
                    _layouts[node] = NodeLayout.Compute(node, state.Data, graph, mapCompact);
                _layoutsDirty = false;
            }
            return _layouts;
        }
    }

    public PointF ScreenToWorld(PointF screen)
        => new((screen.X - PanX) / Zoom, (screen.Y - PanY) / Zoom);

    public PointF WorldToScreen(PointF world)
        => new(world.X * Zoom + PanX, world.Y * Zoom + PanY);

    /// <summary>The node that represents <paramref name="node"/> in the current scope: the node
    /// itself if it's a direct member of the active outpost, the outpost box it lives inside if
    /// it's deeper, or null if it's outside the current scope entirely.</summary>
    public FactoryNode? VisibleRep(FactoryNode node)
    {
        var active = state.Editor.ActiveOutpost;
        FactoryNode? cur = node;
        while (cur is not null && cur.Parent != active) cur = cur.Parent;
        return cur;
    }

    /// <summary>The node to focus on: the hovered node, else the lone selected node.
    /// Null when focus highlighting is off or nothing singular is targeted.</summary>
    private FactoryNode? FocusNode()
    {
        if (!state.Settings.FocusHighlight) return null;
        if (HoverNode is not null) return HoverNode;
        return state.Selection.Count == 1 ? state.Selection[0] : null;
    }

    /// <summary>The focus node plus every node it directly connects to (in this scope).</summary>
    private HashSet<FactoryNode>? FocusNeighbors(FactoryNode? focus)
    {
        if (focus is null) return null;
        var neighbors = new HashSet<FactoryNode> { focus };
        foreach (var connection in state.Editor.Graph.Connections)
        {
            var from = VisibleRep(connection.From);
            var to = VisibleRep(connection.To);
            if (from == focus && to is not null) neighbors.Add(to);
            else if (to == focus && from is not null) neighbors.Add(from);
        }
        return neighbors;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _viewport = new SizeF(dirtyRect.Width, dirtyRect.Height);
        canvas.FillColor = Theme.Background;
        canvas.FillRectangle(dirtyRect);

        canvas.SaveState();
        canvas.Translate(PanX, PanY);
        canvas.Scale(Zoom, Zoom);

        // Map mode replaces the dot grid; only the root canvas sits on the world.
        if (state.Settings.ShowMap && state.Editor.ScopePath.Count == 0)
            DrawMap(canvas, dirtyRect);
        else
            DrawGrid(canvas, dirtyRect);

        var result = state.Editor.Result;
        var layouts = Layouts;

        // Focus highlighting: fade everything but the hovered/selected node and what
        // it connects to, so a dense graph can be read one machine at a time.
        var focus = FocusNode();
        var focusNeighbors = FocusNeighbors(focus);

        // Connections are flat; each end maps to its visible representative (the node itself
        // or the outpost box it sits inside). Cross-boundary wires draw to the box.
        foreach (var connection in state.Editor.Graph.Connections)
        {
            if (focus is not null)
                canvas.Alpha = VisibleRep(connection.From) == focus || VisibleRep(connection.To) == focus
                    ? 1f : DimAlpha;
            DrawConnection(canvas, connection, layouts, result);
        }
        canvas.Alpha = 1f;

        foreach (var node in state.Editor.VisibleNodes)
            if (layouts.TryGetValue(node, out var layout))
            {
                if (focus is not null)
                    canvas.Alpha = focusNeighbors!.Contains(node) ? 1f : DimAlpha;
                DrawNode(canvas, layout, result);
            }
        canvas.Alpha = 1f;

        if (PortInsertLine is { } ins)
        {
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 2.5f;
            canvas.DrawLine(ins.X, ins.Y, ins.X + ins.Width, ins.Y);
        }

        canvas.RestoreState();

        DrawAdorners(canvas);
    }
}
