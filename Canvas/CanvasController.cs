using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
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

    // Phase 3 "drag out of a resource node": the free marker pressed on empty canvas,
    // and the extractor created from it once the drag starts.
    private ResourceNodeInfo? _pressMarker;
    private FactoryNode? _dragOutNode;
    private DateTime _lastClickTime = DateTime.MinValue;
    private PointF _lastClickPoint;
    private FactoryNode? _lastClickNode;

    public event Action<PointF>? OpenRecipeChooser;        // screen position
    public event Action<PortDragContext, PointF>? OpenChooserForPort;
    public event Action<FactoryNode, PointF>? OpenMachinePopup;
    public event Action<FactoryNode>? EnterOutpostRequested;
    public event Action<FactoryNode, NodeLayout>? EditLimitRequested;
    public event Action? Invalidate;
    // Press-time close: dismisses the transient panels (chooser, settings, …) but
    // leaves the docked machine editor for the click/selection logic to retarget.
    public event Action? CloseTransientOverlays;

    private float DragThreshold => Math.Max(2, state.Settings.DragSensitivity / 4f);

    // ---------------------------------------------------------------- pressed

    public void PointerPressed(PointF screen, bool isRight, bool ctrl)
    {
        CloseTransientOverlays?.Invoke();
        _pressScreen = _lastScreen = screen;
        _pressWasRight = isRight;
        var world = drawable.ScreenToWorld(screen);
        (_pressNode, var layout) = HitNode(world);
        _pressPort = layout?.HitPort(world);
        _detachedConnection = null;
        _pressMarker = null;
        _dragOutNode = null;
        _mode = Mode.Pressed;

        // Drag-out-of-a-resource-node: an empty press over a free marker arms the
        // gesture without disturbing pan-on-drag for misses.
        if (!isRight && _pressNode is null && _pressPort is null && drawable.MapActive)
            _pressMarker = FreeMarkerAt(world);

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
                else if (_pressMarker is not null && TryBeginDragOut(_pressMarker))
                    _mode = Mode.Connect;
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
                }
                UpdateSnapPreview(world);
                Invalidate?.Invoke();
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
        _pressMarker = null;

        switch (mode)
        {
            case Mode.Pressed:
                HandleClick(screen, world, wasRight, ctrl);
                break;

            case Mode.DragNodes:
                drawable.SnapPreviewMarker = null;
                state.Editor.Commands.BreakCoalescing();
                SnapSelectionToGrid();
                SnapToResourceNode(world);
                break;

            case Mode.Connect:
                drawable.PendingWire = null;
                if (_dragOutNode is not null)
                    CompleteDragOut(world, screen);
                else
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

    /// <summary>
    /// Shift the view by a screen-space delta and persist it. Used by the docked
    /// machine editor to nudge a node out from under the panel.
    /// </summary>
    public void PanBy(float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;
        drawable.PanX += dx;
        drawable.PanY += dy;
        SyncPanToDocument();
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

    /// <summary>
    /// Map mode: a dropped extractor latches onto the matching free resource node
    /// nearest the given reference point (the pointer during a drag) — the marker's
    /// purity is applied as the machine capacity. Dragging it away releases the node.
    /// The compact badge centers on the marker. Returns the marker snapped to, if any.
    /// </summary>
    private ResourceNodeInfo? SnapToResourceNode(PointF? reference = null, bool coalesce = false)
    {
        if (!drawable.MapActive) return null;
        if (state.Selection.Count != 1) return null;
        var node = state.Selection[0];
        if (node.Kind != NodeKind.Recipe) return null;

        var refPoint = reference ?? NodeCenter(node);
        var occupied = state.OccupiedResourceNodes();
        var best = MapSnap.NearestCandidate(state.Data, node, state.MapNodes, occupied, refPoint.X, refPoint.Y);

        if (best is null)
        {
            if (node.ResourceNodeId is not null)
                state.Editor.SetProperty(node, "Release node", n => n.ResourceNodeId, (n, v) => n.ResourceNodeId = v, (string?)null);
            return null;
        }

        // Center the actual (possibly compact) card on the marker.
        var half = drawable.Layouts.TryGetValue(node, out var layout)
            ? new SizeF(layout.Bounds.Width / 2, layout.Bounds.Height / 2)
            : new SizeF(NodeLayout.CardWidth / 2, NodeLayout.ImageAreaHeight / 2);
        var marker = MapGeometry.ToCanvas(best.X, best.Y);
        state.Editor.MoveNodes([node],
            marker.X - half.Width - node.X,
            marker.Y - half.Height - node.Y,
            coalesce: coalesce);
        if (node.ResourceNodeId != best.Instance)
            state.Editor.SetProperty(node, "Snap to node", n => n.ResourceNodeId, (n, v) => n.ResourceNodeId = v, (string?)best.Instance);

        // Adopt the node's purity when the machine family has purity capacities.
        if (state.Data.RecipesByName.TryGetValue(node.Name, out var recipe))
        {
            var family = state.Data.MultiMachineFor(recipe.Machine);
            if (family is not null && family.Capacities.Any(c => c.Name == best.Purity))
                state.Editor.SetProperty(node, "Purity", n => n.Capacity, (n, v) => n.Capacity = v, (string?)best.Purity);
        }
        drawable.InvalidateLayouts();
        return best;
    }

    private PointF NodeCenter(FactoryNode node)
        => drawable.Layouts.TryGetValue(node, out var layout)
            ? layout.Bounds.Center
            : new PointF((float)node.X + NodeLayout.CardWidth / 2, (float)node.Y + NodeLayout.ImageAreaHeight / 2);

    /// <summary>While dragging a single extractor in map mode, highlight the marker it would snap to.</summary>
    private void UpdateSnapPreview(PointF world)
    {
        if (!drawable.MapActive || state.Selection.Count != 1
            || state.Selection[0].Kind != NodeKind.Recipe)
        {
            drawable.SnapPreviewMarker = null;
            return;
        }
        drawable.SnapPreviewMarker = MapSnap.NearestCandidate(
            state.Data, state.Selection[0], state.MapNodes, state.OccupiedResourceNodes(), world.X, world.Y);
    }

    /// <summary>An unoccupied resource marker under the world point (a touch larger than the icon).</summary>
    private ResourceNodeInfo? FreeMarkerAt(PointF world)
    {
        var occupied = state.OccupiedResourceNodes();
        ResourceNodeInfo? best = null;
        var bestDistance = MapGeometry.MarkerRadius + 6f;
        foreach (var marker in state.MapNodes)
        {
            if (occupied.Contains(marker.Instance)) continue;
            var p = MapGeometry.ToCanvas(marker.X, marker.Y);
            var dx = p.X - world.X;
            var dy = p.Y - world.Y;
            var d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = marker;
            }
        }
        return best;
    }

    /// <summary>
    /// Phase 3: create the matching extractor for a bare marker, snap it (purity
    /// adopted) and hand the new node's output port to the connect drag — all inside
    /// one undo group so a single Ctrl+Z removes the machine and its wire together.
    /// Returns false (gesture aborts to pan) when no recipe fits the marker.
    /// </summary>
    private bool TryBeginDragOut(ResourceNodeInfo marker)
    {
        var recipeName = MapSnap.RecipeForMarker(state.Data, marker);
        if (recipeName is null) return false;

        state.Editor.Commands.BeginGroup("Extract from node");
        var p = MapGeometry.ToCanvas(marker.X, marker.Y);
        var node = state.Editor.AddNode(recipeName, p.X, p.Y);
        ApplyMachineDefaults(node);
        state.SetSelection([node]);
        drawable.InvalidateLayouts();
        // Snap centers the compact badge on the marker and adopts purity.
        SnapToResourceNode(p);

        if (!drawable.Layouts.TryGetValue(node, out var layout)
            || layout.Outputs.Count == 0)
        {
            state.Editor.Commands.CancelGroup();
            state.ClearSelection();
            return false;
        }

        _dragOutNode = node;
        _pressNode = node;
        _pressPort = layout.Outputs[0];
        return true;
    }

    /// <summary>Apply the user's per-family default variant to a freshly created machine, if set.</summary>
    private void ApplyMachineDefaults(FactoryNode node)
    {
        if (node.Kind != NodeKind.Recipe
            || !state.Data.RecipesByName.TryGetValue(node.Name, out var recipe)) return;
        var family = state.Data.MultiMachineFor(recipe.Machine);
        if (family is null) return;
        var setting = state.Settings.MachineDefaults.FirstOrDefault(m => m.Name == family.Name);
        if (setting?.DefaultMachine is { Length: > 0 } variant
            && family.Machines.Any(v => v.Name == variant))
            state.Editor.SetProperty(node, "Machine", n => n.MachineVariant, (n, v) => n.MachineVariant = v, (string?)variant);
    }

    /// <summary>Finish the drag-out gesture: wire to a target input, or open the filtered chooser.</summary>
    private void CompleteDragOut(PointF world, PointF screen)
    {
        var node = _dragOutNode!;
        var port = _pressPort!;
        _dragOutNode = null;

        var (targetNode, targetLayout) = HitNode(world);
        var targetPort = targetLayout?.HitPort(world);

        if (targetNode is not null && targetNode != node
            && TryResolveEndpoints(node, port, targetNode, targetPort, out var from, out var part, out var to))
        {
            state.Editor.Connect(from!, part!, to!);
            state.Editor.Commands.EndGroup();
            drawable.InvalidateLayouts();
            return;
        }

        if (targetNode is null)
        {
            // Empty canvas: keep the extractor, close the group, then offer the chooser.
            state.Editor.Commands.EndGroup();
            OpenChooserForPort?.Invoke(new PortDragContext(node, port.Part, !port.IsInput), screen);
            return;
        }

        // Dropped on something incompatible (or the node itself): keep just the extractor.
        state.Editor.Commands.EndGroup();
    }

    /// <summary>Esc / cancel during a drag-out removes the just-created extractor as one step.</summary>
    public void Cancel()
    {
        if (_mode == Mode.Connect && _dragOutNode is not null)
        {
            state.Editor.Commands.CancelGroup();
            state.ClearSelection();
            _dragOutNode = null;
        }
        _mode = Mode.Idle;
        _pressMarker = null;
        drawable.PendingWire = null;
        drawable.SnapPreviewMarker = null;
        drawable.RubberBand = null;
        Invalidate?.Invoke();
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
            // On a compact badge the chip may be hidden (zoomed out); surface the
            // value anywhere on the badge that is not the output port.
            var valueHit = layout.MapCompact
                ? layout.Bounds.Contains(world) && layout.HitPort(world) is null
                : layout.HasValueRow && layout.ValueRect.Contains(world);
            if (valueHit)
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

        // Map mode: hovering a resource node shows what it is and who uses it.
        var mapNode = drawable.HitMapNode(world);
        if (mapNode is not null)
        {
            var label = $"{loc.L(mapNode.Part)} · {loc.L(mapNode.Purity)}";
            var occupant = state.OccupantOf(mapNode.Instance);
            if (occupant is null)
                return $"{label} — {loc.L("UNUSED")}";
            var rate = state.Editor.Result.For(occupant).DisplayValue;
            return $"{label} — {loc.L(occupant.Name)}: {numbers.Value(rate)}/min";
        }
        return null;
    }
}
