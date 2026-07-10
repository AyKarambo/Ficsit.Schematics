using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

public sealed partial class CanvasController
{
    private (FactoryNode?, NodeLayout?) HitNode(PointF world)
    {
        // Topmost = last drawn; only nodes visible in the current scope are hit-testable.
        foreach (var node in state.Editor.VisibleNodes.ToList().AsEnumerable().Reverse())
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
        => state.Editor.Graph.Connections
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
            var baseTooltip = $"{loc.L(connection.Part)}: {numbers.ValueTooltip(flow)} {loc.L("PER_MINUTE")}";
            if (connection.Logistics != LogisticsKind.None)
                baseTooltip = $"{baseTooltip} · {connection.Logistics}";

            // Augment with over-capacity warning when the setting is on. A vehicle-borne
            // (truck/drone/train) link isn't a belt, so it never warns.
            if (state.Settings.ShowBeltCapacityWarnings && connection.Logistics == LogisticsKind.None
                && flow > Rational.Zero)
            {
                var isFluid = state.Data.PartsByName.TryGetValue(connection.Part, out var partDef) && partDef.Fluid;
                var threshold = isFluid ? state.Data.MaxPipeThroughput : state.Data.MaxBeltThroughput;
                var overflow = ConnectionOverflowHelper.Check(flow, threshold);
                if (overflow is not null)
                {
                    var kind = isFluid ? "pipe" : "belt";
                    var markName = isFluid ? "Mk.2 pipe" : "Mk.6 belt";
                    return $"{baseTooltip} — exceeds {markName} ({numbers.ValueTooltip(threshold)}/min) · needs {overflow.LinesNeeded} {kind}s";
                }
            }

            return baseTooltip;
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
