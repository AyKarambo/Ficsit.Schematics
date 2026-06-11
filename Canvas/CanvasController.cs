using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// Pointer state machine for the canvas:
/// left-drag background = pan, left-drag node = move, drag from port = connect
/// (re-drag detaches), right-drag = rubber-band select, double-left on background =
/// recipe chooser, double-left on node = machine editor, right-click = context
/// popups, wheel = zoom around the cursor. Deletion is deliberate only — Delete
/// key or the editor popover, never an accidental double-click.
/// </summary>
public sealed class CanvasController(AppState state, FactoryCanvasDrawable drawable)
{
    private enum Mode { Idle, Pressed, Pan, DragNodes, Connect, RubberBand }

    private Mode _mode = Mode.Idle;
    private PointF _pressScreen;
    private PointF _lastScreen;
    private bool _pressWasRight;
    private FactoryNode? _pressNode;
    private PortInfo? _pressPort;
    private NodeConnection? _detachedConnection;
    private DateTime _lastClickTime = DateTime.MinValue;
    private PointF _lastClickPoint;
    private FactoryNode? _lastClickNode;

    public event Action<PointF>? OpenRecipeChooser;        // screen position
    public event Action<PortDragContext, PointF>? OpenChooserForPort;
    public event Action<FactoryNode, PointF>? OpenMachinePopup;
    public event Action<FactoryNode>? EnterOutpostRequested;
    public event Action<FactoryNode, NodeLayout>? EditLimitRequested;
    public event Action? Invalidate;
    public event Action? CloseOverlays;

    private float DragThreshold => Math.Max(2, state.Settings.DragSensitivity / 4f);

    // ---------------------------------------------------------------- pressed

    public void PointerPressed(PointF screen, bool isRight, bool ctrl)
    {
        CloseOverlays?.Invoke();
        _pressScreen = _lastScreen = screen;
        _pressWasRight = isRight;
        var world = drawable.ScreenToWorld(screen);
        (_pressNode, var layout) = HitNode(world);
        _pressPort = layout?.HitPort(world);
        _detachedConnection = null;
        _mode = Mode.Pressed;

        if (!isRight && _pressPort is { IsInput: true } inputPort && _pressNode is not null)
        {
            // Re-drag an existing single connection to detach it.
            var existing = state.Editor.CurrentScope
                .IncomingTo(_pressNode, inputPort.Part).ToList();
            if (existing.Count == 1)
                _detachedConnection = existing[0];
        }
    }

    public void PointerMoved(PointF screen, bool leftDown, bool rightDown)
    {
        var world = drawable.ScreenToWorld(screen);

        if (_mode == Mode.Pressed)
        {
            var moved = Math.Abs(screen.X - _pressScreen.X) + Math.Abs(screen.Y - _pressScreen.Y);
            if (moved > DragThreshold)
            {
                if (_pressWasRight)
                    _mode = Mode.RubberBand;
                else if (_pressPort is not null)
                    _mode = Mode.Connect;
                else if (_pressNode is not null)
                {
                    if (!state.Selection.Contains(_pressNode))
                        state.SetSelection([_pressNode]);
                    _mode = Mode.DragNodes;
                }
                else
                    _mode = Mode.Pan;
            }
        }

        switch (_mode)
        {
            case Mode.Pan:
                drawable.PanX += screen.X - _lastScreen.X;
                drawable.PanY += screen.Y - _lastScreen.Y;
                SyncPanToDocument();
                Invalidate?.Invoke();
                break;

            case Mode.DragNodes:
                var prevWorld = drawable.ScreenToWorld(_lastScreen);
                var dx = world.X - prevWorld.X;
                var dy = world.Y - prevWorld.Y;
                if (dx != 0 || dy != 0)
                {
                    state.Editor.MoveNodes(state.Selection, dx, dy);
                    drawable.InvalidateLayouts();
                    Invalidate?.Invoke();
                }
                break;

            case Mode.Connect:
                var anchorLayout = _pressNode is not null ? GetLayout(_pressNode) : null;
                var anchor = anchorLayout is not null && _pressPort is not null
                    ? drawable.WorldToScreen(anchorLayout.PortAnchor(_pressPort))
                    : _pressScreen;
                drawable.PendingWire = (anchor, screen);
                Invalidate?.Invoke();
                break;

            case Mode.RubberBand:
                drawable.RubberBand = new RectF(
                    Math.Min(_pressScreen.X, screen.X),
                    Math.Min(_pressScreen.Y, screen.Y),
                    Math.Abs(screen.X - _pressScreen.X),
                    Math.Abs(screen.Y - _pressScreen.Y));
                Invalidate?.Invoke();
                break;
        }

        _lastScreen = screen;
    }

    public void PointerReleased(PointF screen, bool wasRight, bool ctrl)
    {
        var world = drawable.ScreenToWorld(screen);
        var mode = _mode;
        _mode = Mode.Idle;

        switch (mode)
        {
            case Mode.Pressed:
                HandleClick(screen, world, wasRight, ctrl);
                break;

            case Mode.DragNodes:
                state.Editor.Commands.BreakCoalescing();
                SnapSelectionToGrid();
                break;

            case Mode.Connect:
                drawable.PendingWire = null;
                CompleteConnection(world, screen);
                break;

            case Mode.RubberBand:
                if (drawable.RubberBand is { } band)
                {
                    var rect = new RectF(
                        (band.X - drawable.PanX) / drawable.Zoom,
                        (band.Y - drawable.PanY) / drawable.Zoom,
                        band.Width / drawable.Zoom,
                        band.Height / drawable.Zoom);
                    var hits = drawable.Layouts.Values
                        .Where(l => l.Bounds.IntersectsWith(rect))
                        .Select(l => l.Node);
                    state.SetSelection(ctrl ? state.Selection.Union(hits) : hits);
                }
                drawable.RubberBand = null;
                break;
        }

        Invalidate?.Invoke();
    }

    private void HandleClick(PointF screen, PointF world, bool isRight, bool ctrl)
    {
        var (node, layout) = HitNode(world);
        var isDoubleClick = (DateTime.UtcNow - _lastClickTime).TotalMilliseconds < 450
            && Math.Abs(screen.X - _lastClickPoint.X) < 6
            && Math.Abs(screen.Y - _lastClickPoint.Y) < 6
            && node == _lastClickNode;
        _lastClickTime = isDoubleClick ? DateTime.MinValue : DateTime.UtcNow;
        _lastClickPoint = screen;
        _lastClickNode = node;

        if (isRight)
        {
            if (node is not null)
            {
                if (!state.Selection.Contains(node))
                    state.SetSelection([node]);
                OpenMachinePopup?.Invoke(node, screen);
                return;
            }
            var connection = HitConnectionLabel(world);
            if (connection is not null)
            {
                state.Editor.Disconnect(connection);
                drawable.InvalidateLayouts();
                return;
            }
            OpenRecipeChooser?.Invoke(screen);
            return;
        }

        if (isDoubleClick)
        {
            if (node is null)
            {
                OpenRecipeChooser?.Invoke(screen);
            }
            else if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                state.ClearSelection();
                EnterOutpostRequested?.Invoke(node);
            }
            else
            {
                state.SetSelection([node]);
                OpenMachinePopup?.Invoke(node, screen);
            }
            return;
        }

        if (node is not null)
        {
            if (layout is not null && layout.HasLimitRow && layout.LimitRect.Contains(world))
            {
                EditLimitRequested?.Invoke(node, layout);
                return;
            }
            state.SetSelection(ctrl ? ToggleSelection(node) : [node]);
            return;
        }

        state.ClearSelection();
    }

    private IEnumerable<FactoryNode> ToggleSelection(FactoryNode node)
        => state.Selection.Contains(node)
            ? state.Selection.Where(n => n != node).ToList()
            : state.Selection.Append(node).ToList();

    // ------------------------------------------------------------ connections

    private void CompleteConnection(PointF world, PointF screen)
    {
        if (_pressNode is null || _pressPort is null) return;

        var (targetNode, targetLayout) = HitNode(world);
        var targetPort = targetLayout?.HitPort(world);

        if (_detachedConnection is not null)
        {
            // Re-drag: dropping on a new valid input moves the connection; empty deletes it.
            var moved = TryResolveEndpoints(_detachedConnection.From,
                new PortInfo(_detachedConnection.Part, RectF.Zero, false),
                targetNode, targetPort, out var from, out var part, out var to);
            state.Editor.Disconnect(_detachedConnection);
            if (moved && to != _detachedConnection.To)
                state.Editor.Connect(from!, part!, to!);
            drawable.InvalidateLayouts();
            return;
        }

        if (targetNode is null)
        {
            // Dropped on empty canvas: offer compatible recipes for this port.
            OpenChooserForPort?.Invoke(
                new PortDragContext(_pressNode, _pressPort.Part, !_pressPort.IsInput), screen);
            return;
        }
        if (targetNode == _pressNode) return;
        if (TryResolveEndpoints(_pressNode, _pressPort, targetNode, targetPort, out var fromNode, out var partName, out var toNode))
        {
            state.Editor.Connect(fromNode!, partName!, toNode!);
            drawable.InvalidateLayouts();
        }
    }

    /// <summary>
    /// Works out producer/part/consumer from a drag between two nodes, allowing
    /// either direction and letting specialty machines adopt the dragged part.
    /// </summary>
    private bool TryResolveEndpoints(
        FactoryNode pressNode, PortInfo pressPort,
        FactoryNode? targetNode, PortInfo? targetPort,
        out FactoryNode? from, out string? part, out FactoryNode? to)
    {
        from = null; part = null; to = null;
        if (targetNode is null) return false;

        var pressIsOutput = !pressPort.IsInput;
        var pressPart = pressPort.Part;

        if (pressIsOutput)
        {
            from = pressNode;
            to = targetNode;
            part = pressPart != "AnyPart" ? pressPart : targetPort?.Part;
        }
        else
        {
            from = targetNode;
            to = pressNode;
            part = pressPart != "AnyPart" ? pressPart : targetPort?.Part;
        }

        if (part is null or "AnyPart") return false;
        var resolvedPart = part;

        // The consumer must accept the part: recipes need a matching input;
        // specialty machines accept anything.
        if (to!.Kind == NodeKind.Recipe)
        {
            if (!state.Data.RecipesByName.TryGetValue(to.Name, out var recipe)
                || recipe.Inputs.All(i => i.Part != resolvedPart))
                return false;
        }
        if (from!.Kind == NodeKind.Recipe)
        {
            if (!state.Data.RecipesByName.TryGetValue(from.Name, out var recipe)
                || recipe.Outputs.All(o => o.Part != resolvedPart))
                return false;
        }
        return from != to;
    }

    // ------------------------------------------------------------------- misc

    public void Wheel(PointF screen, int delta)
        => ZoomAround(screen, (float)Math.Pow(1.1, delta / 120.0));

    public void ZoomAround(PointF screen, float factor)
    {
        var newZoom = Math.Clamp(drawable.Zoom * factor, 0.1f, 5f);
        var world = drawable.ScreenToWorld(screen);
        drawable.Zoom = newZoom;
        drawable.PanX = screen.X - world.X * newZoom;
        drawable.PanY = screen.Y - world.Y * newZoom;
        SyncPanToDocument();
        Invalidate?.Invoke();
    }

    /// <summary>Frame all machines in the viewport (toolbar "fit" and Ctrl+0).</summary>
    public void ZoomToFit(SizeF viewport)
    {
        var layouts = drawable.Layouts.Values.ToList();
        if (layouts.Count == 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            drawable.Zoom = 1f;
            drawable.PanX = viewport.Width / 2;
            drawable.PanY = viewport.Height / 2;
        }
        else
        {
            var bounds = layouts[0].Bounds;
            foreach (var layout in layouts.Skip(1))
                bounds = bounds.Union(layout.Bounds);
            bounds = bounds.Inflate(60, 60);
            var zoom = Math.Clamp(Math.Min(viewport.Width / bounds.Width, viewport.Height / bounds.Height), 0.1f, 1.5f);
            drawable.Zoom = zoom;
            drawable.PanX = viewport.Width / 2 - bounds.Center.X * zoom;
            drawable.PanY = viewport.Height / 2 - bounds.Center.Y * zoom;
        }
        SyncPanToDocument();
        Invalidate?.Invoke();
    }

    /// <summary>Delete the current selection (Delete key / editor popover).</summary>
    public void DeleteSelection()
    {
        if (state.Selection.Count == 0) return;
        state.Editor.DeleteNodes(state.Selection.ToList());
        state.ClearSelection();
        drawable.InvalidateLayouts();
        Invalidate?.Invoke();
    }

    private void SyncPanToDocument()
    {
        // Outposts remember their own view; the root view lives on the document.
        if (state.Editor.ScopePath.Count > 0)
        {
            var outpost = state.Editor.ScopePath[^1];
            outpost.InnerZoom = drawable.Zoom;
            outpost.InnerPanX = drawable.PanX;
            outpost.InnerPanY = drawable.PanY;
        }
        else
        {
            var doc = state.Editor.Document;
            doc.Zoom = drawable.Zoom;
            doc.PanX = drawable.PanX;
            doc.PanY = drawable.PanY;
        }
    }

    private void SnapSelectionToGrid()
    {
        if (!state.Settings.UseBuildingGrid) return;
        if (!double.TryParse(state.Settings.BuildingGridX, out var gx) || gx <= 0) return;
        if (!double.TryParse(state.Settings.BuildingGridY, out var gy) || gy <= 0) return;
        foreach (var node in state.Selection)
        {
            var snappedX = Math.Round(node.X / gx) * gx;
            var snappedY = Math.Round(node.Y / gy) * gy;
            state.Editor.MoveNodes([node], snappedX - node.X, snappedY - node.Y, coalesce: true);
        }
        state.Editor.Commands.BreakCoalescing();
        drawable.InvalidateLayouts();
    }

    private (FactoryNode?, NodeLayout?) HitNode(PointF world)
    {
        // Topmost = last in list (drawn last).
        foreach (var node in state.Editor.CurrentScope.Nodes.AsEnumerable().Reverse())
        {
            if (!drawable.Layouts.TryGetValue(node, out var layout)) continue;
            var expanded = layout.Bounds;
            expanded = new RectF(expanded.X - NodeLayout.PortSize, expanded.Y,
                expanded.Width + 2 * NodeLayout.PortSize, expanded.Height);
            if (expanded.Contains(world) && (layout.Bounds.Contains(world) || layout.HitPort(world) is not null))
                return (node, layout);
        }
        return (null, null);
    }

    private NodeConnection? HitConnectionLabel(PointF world)
        => state.Editor.CurrentScope.Connections
            .FirstOrDefault(c => drawable.ConnectionLabelRect(c).Contains(world));

    private NodeLayout? GetLayout(FactoryNode node)
        => drawable.Layouts.GetValueOrDefault(node);

    /// <summary>Hover support for tooltips: what is under the cursor.</summary>
    public string? TooltipTextAt(PointF screen, NumberFormatService numbers, LocalizationService loc)
    {
        var world = drawable.ScreenToWorld(screen);
        var (node, layout) = HitNode(world);
        if (node is not null && layout is not null)
        {
            var result = state.Editor.Result.For(node);
            if (layout.HasValueRow && layout.ValueRect.Contains(world))
            {
                var machineName = node.Kind == NodeKind.Recipe
                    && state.Data.RecipesByName.TryGetValue(node.Name, out var recipe)
                        ? loc.L(recipe.Machine)
                        : loc.L(node.Name);
                return result.IsPpmDisplay
                    ? $"{numbers.ValueTooltip(result.DisplayValue)} {loc.L("PER_MINUTE")}"
                    : $"{numbers.ValueTooltip(result.DisplayValue)} {machineName}";
            }
            var port = layout.HitPort(world);
            if (port is not null)
            {
                var ports = port.IsInput ? result.Inputs : result.Outputs;
                if (ports.TryGetValue(port.Part, out var portResult))
                    return $"{loc.L(port.Part)}: {numbers.ValueTooltip(portResult.Target)} {loc.L("PER_MINUTE")}";
                return loc.L(port.Part);
            }
        }
        var connection = HitConnectionLabel(world);
        if (connection is not null)
        {
            var flow = state.Editor.Result.FlowOf(connection);
            return $"{loc.L(connection.Part)}: {numbers.ValueTooltip(flow)} {loc.L("PER_MINUTE")}";
        }
        return null;
    }
}
