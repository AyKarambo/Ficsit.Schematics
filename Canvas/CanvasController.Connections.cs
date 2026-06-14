using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

public sealed partial class CanvasController
{
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

        // Dropping a wire on an outpost box connects the part to the outpost: it creates the
        // boundary handle (a per-part Import/Export inside) and wires this node to it. The
        // handle then shows up inside the outpost as a draggable item — no chooser.
        if (targetNode is not null && targetNode != _pressNode && _pressPort.Part != "AnyPart"
            && targetNode.Kind is NodeKind.Outpost or NodeKind.Blueprint
            && _pressNode.Kind is not (NodeKind.Outpost or NodeKind.Blueprint))
        {
            var isImport = !_pressPort.IsInput; // a producer's output → import; a consumer's input → export
            state.Editor.Commands.BeginGroup(isImport ? "Import to outpost" : "Export from outpost");
            var handle = state.Editor.EnsureBoundary(targetNode, _pressPort.Part, isImport);
            if (isImport) state.Editor.Connect(_pressNode, _pressPort.Part, handle);
            else state.Editor.Connect(handle, _pressPort.Part, _pressNode);
            state.Editor.Commands.EndGroup();
            drawable.InvalidateLayouts();
            return;
        }

        if (targetNode is null)
        {
            // Inside an outpost, releasing near an edge declares a boundary: a machine input on
            // the left rail becomes an Import (fed from outside), an output on the right an Export.
            if (EdgeZoneFor(screen) is { } left && state.Editor.ActiveOutpost is { } outpost)
            {
                state.Editor.Commands.BeginGroup(left ? "Add import" : "Add export");
                var handle = state.Editor.EnsureBoundary(outpost, _pressPort.Part, isImport: left);
                if (left) state.Editor.Connect(handle, _pressPort.Part, _pressNode);
                else state.Editor.Connect(_pressNode, _pressPort.Part, handle);
                state.Editor.Commands.EndGroup();
                drawable.InvalidateLayouts();
                return;
            }
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
        // Outposts are groupings, not machines — you wire members from inside, not the box.
        if (targetNode.Kind is NodeKind.Outpost or NodeKind.Blueprint
            || pressNode.Kind is NodeKind.Outpost or NodeKind.Blueprint) return false;

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

    /// <summary>While dragging from a port, true when the pointer is over the pressed node's
    /// own port column on the same side (≥2 ports), meaning the gesture is a reorder rather
    /// than a connect. Outputs the world-space insertion bar and records the target index.</summary>
    private bool TryReorderHint(PointF world, out (float X, float Y, float Width) line)
    {
        line = default;
        if (_pressNode is null || _pressPort is null) return false;
        var layout = GetLayout(_pressNode);
        if (layout is null || !layout.Bounds.Contains(world)) return false;

        var ports = _pressPort.IsInput ? layout.Inputs : layout.Outputs;
        if (ports.Count < 2) return false;
        var sameSide = _pressPort.IsInput
            ? world.X < layout.Bounds.Center.X
            : world.X >= layout.Bounds.Center.X;
        if (!sameSide) return false;

        var index = 0;
        foreach (var port in ports)
            if (port.IconRect.Center.Y < world.Y) index++;
        _reorderIndex = index;

        var slotY = index < ports.Count
            ? ports[index].IconRect.Top - 1f
            : ports[^1].IconRect.Bottom + 1f;
        line = (ports[0].IconRect.Left - 2f, slotY, NodeLayout.PortSize + 4f);
        return true;
    }

    /// <summary>Commit the reorder captured by <see cref="TryReorderHint"/>: move the pressed
    /// port's part to the target slot and persist the whole side's new order (undoable).</summary>
    private void CommitReorder()
    {
        if (_pressNode is null || _pressPort is null) return;
        var layout = GetLayout(_pressNode);
        if (layout is null) return;
        var ports = _pressPort.IsInput ? layout.Inputs : layout.Outputs;
        var order = ports.Select(p => p.Part).ToList();
        var fromIndex = order.IndexOf(_pressPort.Part);
        if (fromIndex < 0) return;

        var target = Math.Clamp(_reorderIndex, 0, order.Count);
        order.RemoveAt(fromIndex);
        if (target > fromIndex) target--;
        order.Insert(Math.Clamp(target, 0, order.Count), _pressPort.Part);
        if (order.SequenceEqual(ports.Select(p => p.Part))) return; // no change

        state.Editor.SetPortOrder(_pressNode, _pressPort.IsInput, order);
        drawable.InvalidateLayouts();
    }

    private bool PortHasConnections(FactoryNode node, PortInfo port)
    {
        var graph = state.Editor.Graph;
        return port.IsInput
            ? graph.IncomingTo(node, port.Part).Any()
            : graph.OutgoingFrom(node, port.Part).Any();
    }

    /// <summary>Right-click → "Clear connection(s)": remove every connection on this exact
    /// port (same node, part and side) as one undo step.</summary>
    public void ClearPortConnections(FactoryNode node, PortInfo port)
    {
        var graph = state.Editor.Graph;
        var doomed = (port.IsInput
            ? graph.IncomingTo(node, port.Part)
            : graph.OutgoingFrom(node, port.Part)).ToList();
        if (doomed.Count == 0) return;
        state.Editor.Commands.BeginGroup("Clear connections");
        foreach (var connection in doomed)
            state.Editor.Disconnect(connection);
        state.Editor.Commands.EndGroup();
        drawable.InvalidateLayouts();
        Invalidate?.Invoke();
    }
}
