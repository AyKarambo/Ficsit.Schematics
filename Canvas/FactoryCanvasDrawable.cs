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
public sealed class FactoryCanvasDrawable(AppState state, IconStore icons, NumberFormatService numbers) : IDrawable
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

    /// <summary>While reordering a port, the world-space insertion slot (x, y, width) drawn as
    /// a short bar between ports.</summary>
    public (float X, float Y, float Width)? PortInsertLine { get; set; }

    /// <summary>True when the world map underlay and resource markers are active (root scope).</summary>
    public bool MapActive => state.Settings.ShowMap && state.Editor.ScopePath.Count == 0;

    private readonly Dictionary<FactoryNode, NodeLayout> _layouts = [];
    private bool _layoutsDirty = true;

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

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
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

        // Connections are flat; each end maps to its visible representative (the node itself
        // or the outpost box it sits inside). Cross-boundary wires draw to the box.
        foreach (var connection in state.Editor.Graph.Connections)
            DrawConnection(canvas, connection, layouts, result);

        foreach (var node in state.Editor.VisibleNodes)
            if (layouts.TryGetValue(node, out var layout))
                DrawNode(canvas, layout, result);

        if (PortInsertLine is { } ins)
        {
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 2.5f;
            canvas.DrawLine(ins.X, ins.Y, ins.X + ins.Width, ins.Y);
        }

        canvas.RestoreState();

        DrawAdorners(canvas);
    }

    private IImage? _mapImage;
    private bool _mapLoadAttempted;

    /// <summary>World map plus the imported resource-node markers.</summary>
    private void DrawMap(ICanvas canvas, RectF dirtyRect)
    {
        if (!_mapLoadAttempted)
        {
            _mapLoadAttempted = true;
            _mapImage = icons.GetAsset("map/world_map.jpg");
        }

        var rect = MapGeometry.MapRect;
        if (_mapImage is not null)
        {
            // Dim the artwork so factory cards and wires stay readable.
            canvas.Alpha = Theme == CanvasTheme.Dark ? 0.55f : 0.8f;
            canvas.DrawImage(_mapImage, rect.X, rect.Y, rect.Width, rect.Height);
            canvas.Alpha = 1f;
        }

        var occupied = state.OccupiedResourceNodes();
        var topLeft = ScreenToWorld(new PointF(dirtyRect.Left, dirtyRect.Top));
        var bottomRight = ScreenToWorld(new PointF(dirtyRect.Right, dirtyRect.Bottom));

        foreach (var node in state.MapNodes)
        {
            var p = MapGeometry.ToCanvas(node.X, node.Y);
            if (p.X < topLeft.X - 40 || p.X > bottomRight.X + 40
                || p.Y < topLeft.Y - 40 || p.Y > bottomRight.Y + 40) continue;
            var isPreview = SnapPreviewMarker is not null && node.Instance == SnapPreviewMarker.Instance;
            DrawMapNode(canvas, node, p, occupied.Contains(node.Instance), isPreview);
        }
    }

    private void DrawMapNode(ICanvas canvas, ResourceNodeInfo node, PointF p, bool occupied, bool isPreview = false)
    {
        var r = node.Kind == ResourceNodeKind.FrackingSatellite
            ? MapGeometry.MarkerRadius * 0.7f
            : MapGeometry.MarkerRadius;

        // Snap-preview pulse: a wide accent ring so the target reads at a glance.
        if (isPreview)
        {
            canvas.StrokeColor = CanvasTheme.Accent;
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(p, r + 5f);
        }

        canvas.FillColor = occupied
            ? CanvasTheme.AccentDeep.WithAlpha(0.85f)
            : Theme.CardBackground.WithAlpha(0.85f);
        canvas.FillCircle(p, r);

        canvas.StrokeSize = 2.5f;
        canvas.StrokeColor = node.Purity switch
        {
            "Pure" => Color.FromArgb("#4CC04C"),
            "Impure" => Color.FromArgb("#E2654E"),
            _ => Color.FromArgb("#E8C547"),
        };
        canvas.DrawCircle(p, r);

        var icon = icons.GetImage(node.Part == "Geyser" ? "Geothermal Generator" : node.Part);
        if (icon is not null)
        {
            var size = r * 1.2f;
            canvas.DrawImage(icon, p.X - size / 2, p.Y - size / 2, size, size);
        }
    }

    /// <summary>The map resource node under a world position, when map mode is on.</summary>
    public ResourceNodeInfo? HitMapNode(PointF world)
    {
        if (!state.Settings.ShowMap || state.Editor.ScopePath.Count > 0) return null;
        ResourceNodeInfo? best = null;
        var bestDistance = MapGeometry.MarkerRadius * 1.4f;
        foreach (var node in state.MapNodes)
        {
            var p = MapGeometry.ToCanvas(node.X, node.Y);
            var d = (float)Math.Sqrt((p.X - world.X) * (p.X - world.X) + (p.Y - world.Y) * (p.Y - world.Y));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = node;
            }
        }
        return best;
    }

    /// <summary>Subtle dot grid in world space; spacing doubles as you zoom out.</summary>
    private void DrawGrid(ICanvas canvas, RectF dirtyRect)
    {
        var spacing = 32f;
        while (spacing * Zoom < 14f) spacing *= 2;
        if (spacing * Zoom > 120f) return;

        var topLeft = ScreenToWorld(new PointF(dirtyRect.Left, dirtyRect.Top));
        var bottomRight = ScreenToWorld(new PointF(dirtyRect.Right, dirtyRect.Bottom));
        var radius = 1.2f / Zoom;

        canvas.FillColor = Theme.GridDot;
        for (var x = MathF.Floor(topLeft.X / spacing) * spacing; x <= bottomRight.X; x += spacing)
            for (var y = MathF.Floor(topLeft.Y / spacing) * spacing; y <= bottomRight.Y; y += spacing)
                canvas.FillCircle(x, y, radius);
    }

    // ------------------------------------------------------------ connections

    private void DrawConnection(
        ICanvas canvas,
        NodeConnection connection,
        IReadOnlyDictionary<FactoryNode, NodeLayout> layouts,
        SolveResult result)
    {
        var fromNode = VisibleRep(connection.From);
        var toNode = VisibleRep(connection.To);
        if (fromNode is null || toNode is null || fromNode == toNode) return;
        if (!layouts.TryGetValue(fromNode, out var fromLayout)
            || !layouts.TryGetValue(toNode, out var toLayout))
            return;

        var fromPort = fromLayout.Outputs.FirstOrDefault(p => p.Part == connection.Part)
            ?? fromLayout.Outputs.FirstOrDefault();
        var toPort = toLayout.Inputs.FirstOrDefault(p => p.Part == connection.Part)
            ?? toLayout.Inputs.FirstOrDefault();
        var start = fromPort is not null
            ? fromLayout.PortAnchor(fromPort)
            : new PointF(fromLayout.Bounds.Right, fromLayout.Bounds.Center.Y);
        var end = toPort is not null
            ? toLayout.PortAnchor(toPort)
            : new PointF(toLayout.Bounds.Left, toLayout.Bounds.Center.Y);

        canvas.StrokeColor = Theme.Wire;
        canvas.StrokeSize = 2f;

        var path = new PathF();
        path.MoveTo(start);
        PointF mid;
        if (state.Editor.Document.Path == "Direct")
        {
            path.LineTo(end);
            mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        }
        else if (state.Editor.Document.Path == "2D")
        {
            var midX = (start.X + end.X) / 2;
            path.LineTo(midX, start.Y);
            path.LineTo(midX, end.Y);
            path.LineTo(end.X, end.Y);
            mid = new PointF(midX, (start.Y + end.Y) / 2);
        }
        else
        {
            var dx = Math.Max(30f, Math.Abs(end.X - start.X) / 2);
            path.CurveTo(start.X + dx, start.Y, end.X - dx, end.Y, end.X, end.Y);
            mid = BezierPoint(start, new PointF(start.X + dx, start.Y), new PointF(end.X - dx, end.Y), end, 0.5f);
        }
        canvas.DrawPath(path);

        // Mid label: part icon + flow ppm — pill treatment (Slice B #3).
        var flow = result.FlowOf(connection);
        var icon = icons.GetImage(connection.Part);
        const float iconSize = 16f;
        if (icon is not null)
            canvas.DrawImage(icon, mid.X - iconSize - 2, mid.Y - iconSize / 2, iconSize, iconSize);
        // The label sits to the right of the icon, centred on mid.Y.
        DrawLabelPill(canvas, numbers.Connection(flow), mid.X, mid.Y, anchorRight: false);
    }

    /// <summary>
    /// Hit / exclusion rect for the connection mid-label pill. The pill's exact size
    /// depends on the measured text, but for hit-testing a fixed conservative estimate
    /// is sufficient — slightly wider than a 5-digit ppm value at label font size.
    /// Kept in sync with <see cref="DrawConnection"/>: same mid-point, same icon offset.
    /// </summary>
    public RectF ConnectionLabelRect(NodeConnection connection)
    {
        var layouts = Layouts;
        var fromNode = VisibleRep(connection.From);
        var toNode = VisibleRep(connection.To);
        if (fromNode is null || toNode is null || fromNode == toNode
            || !layouts.TryGetValue(fromNode, out var fromLayout)
            || !layouts.TryGetValue(toNode, out var toLayout))
            return RectF.Zero;
        var start = new PointF(fromLayout.Bounds.Right, fromLayout.Bounds.Center.Y);
        var end = new PointF(toLayout.Bounds.Left, toLayout.Bounds.Center.Y);
        var mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        // Icon sits at [mid.X - 18, mid.Y - 8]; pill starts at mid.X (anchorRight:false).
        // Estimate: icon + pill together span ~70 wide, ~18 tall, centred on mid.Y.
        const float iconSize = 16f;
        return new RectF(mid.X - iconSize - 4, mid.Y - 9, iconSize + 70, 18);
    }

    private static PointF BezierPoint(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        var u = 1 - t;
        return new PointF(
            u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
            u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
    }

    // ------------------------------------------------------------------ nodes

    private void DrawNode(ICanvas canvas, NodeLayout layout, SolveResult result)
    {
        var node = layout.Node;
        var selected = state.Selection.Contains(node);
        var nodeResult = result.For(node);

        const float corner = 8f;

        if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
        {
            canvas.FillColor = Theme.CardBackground;
            canvas.FillRoundedRectangle(layout.Bounds, corner);
            canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
            canvas.StrokeSize = selected ? 2f : 1f;
            canvas.DrawRoundedRectangle(layout.Bounds, corner);
            var icon = icons.GetImage(node.Kind == NodeKind.Outpost ? "Outpost" : "Blueprint");
            if (icon is not null) canvas.DrawImage(icon, layout.ImageRect.X, layout.ImageRect.Y, layout.ImageRect.Width, layout.ImageRect.Height);
            // Part icons on the boundary ports, same as machine cards, so it's clear what
            // flows in (left) and out (right).
            foreach (var port in layout.Inputs)
                DrawPort(canvas, layout, port, nodeResult);
            foreach (var port in layout.Outputs)
                DrawPort(canvas, layout, port, nodeResult);
            DrawTitle(canvas, layout, node);
            return;
        }

        // Outpost boundary handle: the part icon plus its single port (shown inside the outpost).
        if (node.Kind is NodeKind.Import or NodeKind.Export)
        {
            canvas.FillColor = Theme.CardBackground;
            canvas.FillRoundedRectangle(layout.Bounds, corner);
            canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
            canvas.StrokeSize = selected ? 2f : 1f;
            canvas.DrawRoundedRectangle(layout.Bounds, corner);
            var partIcon = icons.GetImage(node.Name);
            if (partIcon is not null)
                canvas.DrawImage(partIcon, layout.ImageRect.X, layout.ImageRect.Y, layout.ImageRect.Width, layout.ImageRect.Height);
            foreach (var port in layout.Inputs) DrawPort(canvas, layout, port, nodeResult);
            foreach (var port in layout.Outputs) DrawPort(canvas, layout, port, nodeResult);
            return;
        }

        if (layout.MapCompact)
        {
            DrawMapCompact(canvas, layout, nodeResult, selected);
            return;
        }

        // Card.
        canvas.FillColor = Theme.CardBackground;
        canvas.FillRoundedRectangle(layout.Bounds, corner);
        canvas.FillColor = Theme.ValueRowBackground;
        canvas.FillRectangle(layout.ValueRect);
        canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(layout.Bounds, corner);

        // Machine artwork (or name fallback).
        var machineImage = icons.GetImage(MachineImageName(node));
        if (machineImage is not null)
        {
            var rect = FitRect(layout.ImageRect, machineImage.Width / machineImage.Height);
            canvas.DrawImage(machineImage, rect.X, rect.Y, rect.Width, rect.Height);
        }
        else
        {
            canvas.FontColor = Theme.MutedText;
            canvas.FontSize = 9f;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.DrawString(node.Name, layout.ImageRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Ports.
        foreach (var port in layout.Inputs)
            DrawPort(canvas, layout, port, nodeResult);
        foreach (var port in layout.Outputs)
            DrawPort(canvas, layout, port, nodeResult);

        // Calculated value: machine count, or "ppm /min" — the unit beats the
        // reference's italics at telling the two modes apart.
        if (layout.HasValueRow)
        {
            canvas.FontColor = nodeResult.IsInvalid && state.Settings.FlagInvalidValues
                ? Theme.InvalidText
                : Theme.Text;
            canvas.FontSize = 11f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var valueText = nodeResult.IsPpmDisplay
                ? numbers.Value(nodeResult.DisplayValue) + "/min"
                : numbers.Value(nodeResult.DisplayValue);
            canvas.DrawString(valueText,
                layout.ValueRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Limit box ("≤" marks it as a cap; empty box invites a click to set one).
        if (layout.HasLimitRow)
        {
            canvas.FillColor = Theme.LimitBoxBackground;
            canvas.FillRoundedRectangle(layout.LimitRect, 4f);
            canvas.StrokeColor = Theme.CardBorder;
            canvas.StrokeSize = 0.8f;
            canvas.DrawRoundedRectangle(layout.LimitRect, 4f);
            if (node.HasLimit)
            {
                canvas.FontColor = Theme.Text;
                canvas.FontSize = 10f;
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.DrawString("≤ " + node.Max!, layout.LimitRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        DrawTitle(canvas, layout, node);

        // Somersloop badge.
        if (node.Somersloops > 0)
        {
            var sloop = icons.GetUiImage("somersloop");
            if (sloop is not null)
                canvas.DrawImage(sloop, layout.Bounds.Right - 14, layout.Bounds.Top + 2, 12, 12);
        }
    }

    /// <summary>
    /// Compact map badge: rounded marker-sized card, machine icon, one condensed
    /// value chip (hidden when zoomed too far out — shown on hover instead), and the
    /// lone output port. Same selection ring as the full card.
    /// </summary>
    private void DrawMapCompact(ICanvas canvas, NodeLayout layout, NodeResult nodeResult, bool selected)
    {
        const float corner = 7f;
        var node = layout.Node;
        var showChip = Zoom >= NodeLayout.MapCompactChipZoomThreshold;

        canvas.FillColor = Theme.CardBackground;
        canvas.FillRoundedRectangle(layout.Bounds, corner);
        if (showChip)
        {
            canvas.FillColor = Theme.ValueRowBackground;
            canvas.FillRectangle(layout.ValueRect);
        }
        canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(layout.Bounds, corner);

        // Machine artwork sits above the chip; leave the chip band clear when shown.
        var iconArea = showChip
            ? new RectF(layout.ImageRect.X, layout.ImageRect.Y,
                layout.ImageRect.Width, layout.Bounds.Bottom - NodeLayout.MapCompactChipHeight - layout.ImageRect.Y)
            : layout.ImageRect;
        var machineImage = icons.GetImage(MachineImageName(node));
        if (machineImage is not null)
        {
            var rect = FitRect(iconArea, machineImage.Width / machineImage.Height);
            canvas.DrawImage(machineImage, rect.X, rect.Y, rect.Width, rect.Height);
        }

        foreach (var port in layout.Outputs)
            DrawCompactPort(canvas, port, nodeResult);

        if (showChip)
        {
            canvas.FontColor = nodeResult.IsInvalid && state.Settings.FlagInvalidValues
                ? Theme.InvalidText
                : Theme.Text;
            canvas.FontSize = 9.5f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var valueText = nodeResult.IsPpmDisplay
                ? numbers.Value(nodeResult.DisplayValue) + "/min"
                : numbers.Value(nodeResult.DisplayValue);
            canvas.DrawString(valueText, layout.ValueRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        DrawTitle(canvas, layout, node);
    }

    /// <summary>The badge's single output port: a small chip centered in its padded hit rect.</summary>
    private void DrawCompactPort(ICanvas canvas, PortInfo port, NodeResult nodeResult)
    {
        nodeResult.Outputs.TryGetValue(port.Part, out var portResult);
        const float visual = 16f;
        var rect = new RectF(
            port.IconRect.Center.X - visual / 2,
            port.IconRect.Center.Y - visual / 2,
            visual, visual);

        var chip = Theme.PortChip;
        if (portResult is not null && portResult.Unused.IsPositive && portResult.Target.IsPositive)
            chip = Theme.UnusedFlag;
        canvas.FillColor = chip;
        canvas.FillRoundedRectangle(rect, 4f);

        var icon = icons.GetImage(port.Part);
        if (icon is not null)
            canvas.DrawImage(icon, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
    }

    private void DrawTitle(ICanvas canvas, NodeLayout layout, FactoryNode node)
    {
        var title = node.Title ?? (node.Kind is NodeKind.Outpost or NodeKind.Blueprint ? null : null);
        if (string.IsNullOrEmpty(title)) return;
        canvas.FontColor = Theme.Text;
        canvas.FontSize = 10f;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(title, layout.Bounds.X - 40, layout.Bounds.Y - 16, layout.Bounds.Width + 80, 14,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void DrawPort(ICanvas canvas, NodeLayout layout, PortInfo port, NodeResult nodeResult)
    {
        var ports = port.IsInput ? nodeResult.Inputs : nodeResult.Outputs;
        ports.TryGetValue(port.Part, out var portResult);

        // Port chip; flag color when an input is undersupplied or an output has surplus.
        var chip = Theme.PortChip;
        if (portResult is not null)
        {
            if (port.IsInput && portResult.Unmade.IsPositive)
                chip = Theme.UnmadeFlag;
            else if (!port.IsInput && portResult.Unused.IsPositive && portResult.Target.IsPositive)
                chip = Theme.UnusedFlag;
        }
        canvas.FillColor = chip;
        canvas.FillRoundedRectangle(port.IconRect, 5f);

        var icon = icons.GetImage(port.Part);
        if (icon is not null)
            canvas.DrawImage(icon, port.IconRect.X + 1, port.IconRect.Y + 1, port.IconRect.Width - 2, port.IconRect.Height - 2);

        // ppm label outside the card — rendered as a measured pill (Slice B #3).
        // Hidden when zoomed out far enough that pills would collide with neighbours;
        // hover still surfaces the value via the tooltip path (TooltipTextAt covers port rects).
        if (portResult is not null && portResult.Target.IsPositive
            && Zoom >= NodeLayout.LabelHideZoomThreshold)
        {
            var text = numbers.Connection(portResult.Target);
            var labelCenterY = port.IconRect.Center.Y;
            if (port.IsInput)
            {
                // Pill sits to the left of the port chip with a 2-unit gap.
                var anchorRight = port.IconRect.Left - 2f;
                DrawLabelPill(canvas, text, anchorRight, labelCenterY, anchorRight: true);
            }
            else
            {
                // Pill sits to the right of the port chip with a 2-unit gap.
                var anchorLeft = port.IconRect.Right + 2f;
                DrawLabelPill(canvas, text, anchorLeft, labelCenterY, anchorRight: false);
            }
        }
    }

    public string MachineImageName(FactoryNode node)
    {
        switch (node.Kind)
        {
            case NodeKind.Recipe:
                if (state.Data.RecipesByName.TryGetValue(node.Name, out var recipe))
                {
                    var machine = recipe.Machine;
                    if (state.Data.MultiMachinesByName.TryGetValue(machine, out var family)
                        && family.Machines.Count > 0)
                    {
                        var variant = family.Machines.FirstOrDefault(v => v.Name == node.MachineVariant)
                            ?? family.Machines.FirstOrDefault(v => v.Default)
                            ?? family.Machines[0];
                        return variant.Name;
                    }
                    return machine;
                }
                return node.Name;
            case NodeKind.AwesomeSink: return "AWESOME Sink";
            case NodeKind.StorageContainer: return "Storage Container";
            case NodeKind.DimensionalDepot: return "Dimensional Depot Uploader";
            default: return node.Name;
        }
    }

    private static RectF FitRect(RectF area, float aspect)
    {
        if (float.IsNaN(aspect) || aspect <= 0) aspect = 1;
        var width = area.Width;
        var height = width / aspect;
        if (height > area.Height)
        {
            height = area.Height;
            width = height * aspect;
        }
        return new RectF(area.Center.X - width / 2, area.Center.Y - height / 2, width, height);
    }

    // ------------------------------------------------------------- label pills

    /// <summary>
    /// Measures <paramref name="text"/> at the clamped label font size, draws a
    /// rounded-rectangle pill sized to the text, then the text on top — replacing
    /// the legacy fixed-width boxes (Slice B — #3).
    ///
    /// The pill is anchored either with its right edge at <paramref name="x"/> (when
    /// <paramref name="anchorRight"/> is true, i.e. input-side labels) or with its
    /// left edge at <paramref name="x"/> (output-side and connection labels).
    /// </summary>
    /// <summary>Memoized <see cref="ICanvas.GetStringSize"/> (font is always bold). The
    /// measurement for a (text, size) pair is invariant, so this is safe to cache forever;
    /// a hard cap guards against unbounded growth across many zoom levels.</summary>
    private SizeF MeasureText(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font, float fontSize)
    {
        var key = (text, fontSize);
        if (_textSizeCache.TryGetValue(key, out var size)) return size;
        if (_textSizeCache.Count > 4096) _textSizeCache.Clear();
        return _textSizeCache[key] = canvas.GetStringSize(text, font, fontSize);
    }

    private void DrawLabelPill(ICanvas canvas, string text, float x, float centerY, bool anchorRight)
    {
        // Clamp the font upward so effective on-screen size never drops below the
        // minimum legible pixel height (LabelMinEffectivePx / Zoom restores world units).
        var fontSize = MathF.Max(NodeLayout.LabelFontSize, NodeLayout.LabelMinEffectivePx / Zoom);
        var font = Microsoft.Maui.Graphics.Font.DefaultBold;

        var textSize = MeasureText(canvas, text, font, fontSize);
        var pillW = textSize.Width + NodeLayout.LabelPillPadX * 2;
        var pillH = textSize.Height + NodeLayout.LabelPillPadY * 2;
        var pillX = anchorRight ? x - pillW : x;
        var pillY = centerY - pillH / 2;
        var pillRect = new RectF(pillX, pillY, pillW, pillH);

        canvas.FillColor = Theme.LabelPillBackground;
        canvas.FillRoundedRectangle(pillRect, NodeLayout.LabelPillCorner);

        canvas.FontColor = Theme.LabelPillText;
        canvas.FontSize = fontSize;
        canvas.Font = font;
        canvas.DrawString(text, pillRect, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // --------------------------------------------------------------- adorners

    private void DrawAdorners(ICanvas canvas)
    {
        if (PendingWire is { } wire)
        {
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 1.5f;
            canvas.StrokeDashPattern = [4, 3];
            canvas.DrawLine(wire.From, wire.To);
            canvas.StrokeDashPattern = null;
        }

        if (RubberBand is { } band)
        {
            canvas.FillColor = Theme.RubberBand;
            canvas.FillRectangle(band);
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 1f;
            canvas.DrawRectangle(band);
        }

        if (Tooltip is { } tooltip)
        {
            canvas.FontSize = 11f;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            var size = canvas.GetStringSize(tooltip.Text, Microsoft.Maui.Graphics.Font.Default, 11f);
            var rect = new RectF(tooltip.Screen.X + 14, tooltip.Screen.Y + 18, size.Width + 16, size.Height + 10);
            canvas.FillColor = Theme.TooltipBackground;
            canvas.FillRoundedRectangle(rect, 6);
            canvas.StrokeColor = Theme.TooltipBorder;
            canvas.StrokeSize = 1f;
            canvas.DrawRoundedRectangle(rect, 6);
            canvas.FontColor = Theme.TooltipText;
            canvas.DrawString(tooltip.Text, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
