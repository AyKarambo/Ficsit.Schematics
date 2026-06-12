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

    private readonly Dictionary<FactoryNode, NodeLayout> _layouts = [];
    private bool _layoutsDirty = true;

    public void InvalidateLayouts() => _layoutsDirty = true;

    public IReadOnlyDictionary<FactoryNode, NodeLayout> Layouts
    {
        get
        {
            if (_layoutsDirty)
            {
                _layouts.Clear();
                var scope = state.Editor.CurrentScope;
                foreach (var node in scope.Nodes)
                    _layouts[node] = NodeLayout.Compute(node, state.Data, scope);
                _layoutsDirty = false;
            }
            return _layouts;
        }
    }

    public PointF ScreenToWorld(PointF screen)
        => new((screen.X - PanX) / Zoom, (screen.Y - PanY) / Zoom);

    public PointF WorldToScreen(PointF world)
        => new(world.X * Zoom + PanX, world.Y * Zoom + PanY);

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

        var scope = state.Editor.CurrentScope;
        var result = state.Editor.Result;
        var layouts = Layouts;

        foreach (var connection in scope.Connections)
            DrawConnection(canvas, connection, layouts, result);

        foreach (var node in scope.Nodes)
            if (layouts.TryGetValue(node, out var layout))
                DrawNode(canvas, layout, result);

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
            DrawMapNode(canvas, node, p, occupied.Contains(node.Instance));
        }
    }

    private void DrawMapNode(ICanvas canvas, ResourceNodeInfo node, PointF p, bool occupied)
    {
        var r = node.Kind == ResourceNodeKind.FrackingSatellite
            ? MapGeometry.MarkerRadius * 0.7f
            : MapGeometry.MarkerRadius;

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
        if (!layouts.TryGetValue(connection.From, out var fromLayout)
            || !layouts.TryGetValue(connection.To, out var toLayout))
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

        // Mid label: part icon + flow ppm.
        var flow = result.FlowOf(connection);
        var icon = icons.GetImage(connection.Part);
        const float iconSize = 16f;
        if (icon is not null)
            canvas.DrawImage(icon, mid.X - iconSize - 2, mid.Y - iconSize / 2, iconSize, iconSize);
        canvas.FontColor = Theme.Text;
        canvas.FontSize = 10f;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(numbers.Connection(flow), mid.X, mid.Y - 8, 60, 16,
            HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    public RectF ConnectionLabelRect(NodeConnection connection)
    {
        var layouts = Layouts;
        if (!layouts.TryGetValue(connection.From, out var fromLayout)
            || !layouts.TryGetValue(connection.To, out var toLayout))
            return RectF.Zero;
        var start = new PointF(fromLayout.Bounds.Right, fromLayout.Bounds.Center.Y);
        var end = new PointF(toLayout.Bounds.Left, toLayout.Bounds.Center.Y);
        var mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        return new RectF(mid.X - 22, mid.Y - 12, 80, 24);
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
            DrawTitle(canvas, layout, node);
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

        // ppm label outside the card.
        if (portResult is not null && portResult.Target.IsPositive)
        {
            canvas.FontColor = Theme.Text;
            canvas.FontSize = 9.5f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var text = numbers.Connection(portResult.Target);
            if (port.IsInput)
                canvas.DrawString(text, port.IconRect.Left - 64, port.IconRect.Top, 60, port.IconRect.Height,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            else
                canvas.DrawString(text, port.IconRect.Right + 4, port.IconRect.Top, 60, port.IconRect.Height,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
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
