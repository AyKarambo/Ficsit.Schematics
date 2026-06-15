using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

public sealed partial class CanvasController
{
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

        // A snapped extractor is one physical machine whose output is driven by mark × purity ×
        // clock, so the auto-applied ppm default limit is meaningless — drop it so the editor
        // doesn't show a cap the solver ignores (issue #7). No-op once already cleared.
        if (node.HasLimit)
            state.Editor.SetProperty(node, "Clear limit", n => n.Max, (n, v) => n.Max = v, (string?)null);
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
}
