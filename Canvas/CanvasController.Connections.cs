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

        // Dragging from an outpost box's own boundary port (at root): the port is auto-derived from
        // a crossing connection, so resolve it back to the real member behind it and wire that member
        // to the drop target — exactly the reverse of the drop-on-box case below. The target may be a
        // plain machine or another outpost (whose matching member we resolve too).
        if (_pressNode.Kind is NodeKind.Outpost or NodeKind.Blueprint && _pressPort.Part != "AnyPart")
        {
            CompleteOutpostPortDrag(_pressNode, _pressPort, targetNode, screen);
            return;
        }

        // Dropping a wire on an outpost box (at root) crosses the boundary: wire this node to a
        // member that can take/give the part. The crossing is a normal node→node connection — the
        // box port and the interior edge marker are auto-derived from it, no stored handle.
        if (targetNode is not null && targetNode != _pressNode && _pressPort.Part != "AnyPart"
            && targetNode.Kind is NodeKind.Outpost or NodeKind.Blueprint
            && _pressNode.Kind is not (NodeKind.Outpost or NodeKind.Blueprint))
        {
            var producerSide = !_pressPort.IsInput; // dropping an output feeds a member (it consumes)
            var member = FirstMemberForPart(targetNode, _pressPort.Part, wantConsumer: producerSide);
            if (member is not null)
            {
                if (producerSide) state.Editor.Connect(_pressNode, _pressPort.Part, member);
                else state.Editor.Connect(member, _pressPort.Part, _pressNode);
                drawable.InvalidateLayouts();
            }
            return;
        }

        if (targetNode is null)
        {
            // Inside an outpost, releasing on a side rail wires this port to the outer world:
            // the first matching node out there, or the chooser to create one next to the box.
            if (EdgeZoneFor(screen) is not null && state.Editor.ActiveOutpost is { } scope)
            {
                ConnectPortOutside(scope, _pressNode, _pressPort, screen);
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
    /// Complete a drag that started on an outpost box's boundary port. The boundary port is just a
    /// view of a crossing connection, so resolve it to the real member it stands for (the member
    /// that consumes the part for an input port, or produces it for an output port) and wire that
    /// member to the drop target like any node→node drag. An empty drop opens the recipe chooser for
    /// the member; a drop on another outpost resolves that side's member too.
    /// </summary>
    private void CompleteOutpostPortDrag(FactoryNode outpost, PortInfo port, FactoryNode? targetNode, PointF screen)
    {
        var member = FirstMemberForPart(outpost, port.Part, wantConsumer: port.IsInput);
        if (member is null) return;

        if (targetNode is null)
        {
            // The member sits on the same side as the box port (consumes an input, produces an output).
            OpenChooserForPort?.Invoke(new PortDragContext(member, port.Part, FromOutput: !port.IsInput), screen);
            return;
        }
        if (targetNode == outpost) return;

        // Resolve the other end: a plain machine is itself; an outpost contributes its matching member.
        var other = targetNode.Kind is NodeKind.Outpost or NodeKind.Blueprint
            ? FirstMemberForPart(targetNode, port.Part, wantConsumer: !port.IsInput)
            : targetNode;
        if (other is null || other == member) return;

        // The box port's side fixes producer/consumer: an input port is fed (member consumes), an
        // output port emits (member produces). Reuse the normal endpoint validation either way.
        var producer = port.IsInput ? other : member;
        var consumer = port.IsInput ? member : other;
        if (TryResolveEndpoints(producer, new PortInfo(port.Part, RectF.Zero, false),
                consumer, new PortInfo(port.Part, RectF.Zero, true), out var from, out var part, out var to))
        {
            state.Editor.Connect(from!, part!, to!);
            drawable.InvalidateLayouts();
        }
    }

    /// <summary>
    /// Wire a member's port to the outer world (edge-rail drop inside an outpost): connect the
    /// first node outside the outpost that can supply (input port) or take (output port) the
    /// part; without one, open the chooser flagged Outside so the chosen machine is created next
    /// to the outpost box at the level above. Either way the result is a normal crossing
    /// connection — the boundary markers derive from it.
    /// </summary>
    private void ConnectPortOutside(FactoryNode outpost, FactoryNode member, PortInfo port, PointF screen)
    {
        var wantProducer = port.IsInput;
        var outside = FirstOutsiderForPart(outpost, port.Part, wantProducer);
        if (outside is not null)
        {
            if (wantProducer) state.Editor.Connect(outside, port.Part, member);
            else state.Editor.Connect(member, port.Part, outside);
            drawable.InvalidateLayouts();
            return;
        }
        OpenChooserForPort?.Invoke(
            new PortDragContext(member, port.Part, !port.IsInput, Outside: true), screen);
    }

    /// <summary>First node outside <paramref name="outpost"/> that produces (when
    /// <paramref name="wantProducer"/>) or consumes <paramref name="part"/> — a recipe with a
    /// matching output/input, or a generator that emits/takes it.</summary>
    private FactoryNode? FirstOutsiderForPart(FactoryNode outpost, string part, bool wantProducer)
    {
        foreach (var n in state.Editor.Graph.Nodes)
        {
            if (n.Kind is NodeKind.Outpost or NodeKind.Blueprint || NodeLayout.IsInside(n, outpost)) continue;
            if (n.Kind == NodeKind.Recipe && state.Data.RecipesByName.TryGetValue(n.Name, out var recipe))
            {
                if (wantProducer
                        ? recipe.Outputs.Any(o => o.Part == part)
                        : recipe.Inputs.Any(i => i.Part == part))
                    return n;
            }
            else if (n.Kind == NodeKind.Generator
                     && (wantProducer ? GeneratorEmits(n.Name, part) : GeneratorTakes(n.Name, part)))
            {
                return n;
            }
        }
        return null;
    }

    /// <summary>First member of <paramref name="outpost"/> (at any depth) that can take or give
    /// <paramref name="part"/>: a recipe whose inputs (when <paramref name="wantConsumer"/>) or
    /// outputs include it. Used to land a wire dropped on the outpost box onto a real member.</summary>
    private FactoryNode? FirstMemberForPart(FactoryNode outpost, string part, bool wantConsumer)
    {
        foreach (var n in state.Editor.Graph.Nodes)
        {
            if (n.Kind != NodeKind.Recipe || !NodeLayout.IsInside(n, outpost)) continue;
            if (!state.Data.RecipesByName.TryGetValue(n.Name, out var recipe)) continue;
            if (wantConsumer ? recipe.Inputs.Any(i => i.Part == part) : recipe.Outputs.Any(o => o.Part == part))
                return n;
        }
        return null;
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

        // The consumer must accept the part: recipes need a matching input; a generator burns
        // (or takes, e.g. water) one of its machine's fuels; other specialty machines accept anything.
        if (to!.Kind == NodeKind.Recipe)
        {
            if (!state.Data.RecipesByName.TryGetValue(to.Name, out var recipe)
                || recipe.Inputs.All(i => i.Part != resolvedPart))
                return false;
        }
        else if (to.Kind == NodeKind.Generator && !GeneratorTakes(to.Name, resolvedPart))
            return false;

        if (from!.Kind == NodeKind.Recipe)
        {
            if (!state.Data.RecipesByName.TryGetValue(from.Name, out var recipe)
                || recipe.Outputs.All(o => o.Part != resolvedPart))
                return false;
        }
        else if (from.Kind == NodeKind.Generator && !GeneratorEmits(from.Name, resolvedPart))
            return false;

        return from != to;
    }

    /// <summary>True when a generator machine accepts <paramref name="part"/> as a recipe input
    /// (any fuel, or water).</summary>
    private bool GeneratorTakes(string machine, string part)
        => state.Data.Document.Recipes.Any(r => r.Machine == machine && r.Inputs.Any(i => i.Part == part));

    /// <summary>True when a generator machine produces <paramref name="part"/> (e.g. nuclear waste).</summary>
    private bool GeneratorEmits(string machine, string part)
        => state.Data.Document.Recipes.Any(r => r.Machine == machine && r.Outputs.Any(o => o.Part == part));

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
