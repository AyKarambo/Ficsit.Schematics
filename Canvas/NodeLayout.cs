using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Solver;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// World-space geometry of one node card: image area, value row, limit row and
/// port icon rectangles. Shared by the renderer and the hit-testing controller.
/// </summary>
public sealed class NodeLayout
{
    public const float CardWidth = 110f;
    public const float ImageAreaHeight = 64f;
    public const float ValueRowHeight = 20f;
    public const float LimitRowHeight = 20f;
    public const float PortSize = 20f;
    public const float PortGap = 2f;
    public const float SpecialtySize = 44f;

    /// <summary>Footprint of a map-snapped extractor badge: close to the marker (36 across).</summary>
    public const float MapCompactSize = 46f;

    /// <summary>Height of the single condensed value chip below the badge icon.</summary>
    public const float MapCompactChipHeight = 16f;

    /// <summary>Padded hit rect for the lone output port so it stays a comfortable touch target.</summary>
    public const float MapCompactPortHit = 22f;

    /// <summary>Below this zoom the badge hides its value chip and shows it on hover.</summary>
    // TODO(ui-readability-ux #3): unify with the shared label-legibility zoom threshold.
    public const float MapCompactChipZoomThreshold = 0.6f;

    /// <summary>True when this layout is the compact map-snapped badge (drives the draw path).</summary>
    public bool MapCompact { get; init; }

    public required FactoryNode Node { get; init; }
    public RectF Bounds { get; init; }
    public RectF ImageRect { get; init; }
    public RectF ValueRect { get; init; }
    public RectF LimitRect { get; init; }
    public bool HasValueRow { get; init; }
    public bool HasLimitRow { get; init; }
    public List<PortInfo> Inputs { get; } = [];
    public List<PortInfo> Outputs { get; } = [];

    public static NodeLayout Compute(FactoryNode node, GameDatabase data, FactoryGraph scope, bool mapCompact = false)
    {
        var x = (float)node.X;
        var y = (float)node.Y;

        // Map-snapped extractor: a marker-sized badge with one value chip and a
        // single output port. Editing still happens through the popup.
        if (mapCompact && node.Kind == NodeKind.Recipe && node.ResourceNodeId is not null)
            return ComputeMapCompact(node, data, x, y);

        if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
        {
            var box = new RectF(x, y, SpecialtySize, SpecialtySize);
            var specialty = new NodeLayout
            {
                Node = node,
                Bounds = box,
                ImageRect = box.Inflate(-4, -4),
                HasValueRow = false,
                HasLimitRow = false,
            };
            AddDynamicPorts(specialty, node, scope, box);
            return specialty;
        }

        List<string> inputParts = [];
        List<string> outputParts = [];
        if (node.Kind == NodeKind.Recipe && data.RecipesByName.TryGetValue(node.Name, out var recipe))
        {
            inputParts = recipe.Inputs.Select(p => p.Part).ToList();
            outputParts = recipe.Outputs.Select(p => p.Part).ToList();
        }

        var portsPerSide = Math.Max(inputParts.Count, outputParts.Count);
        var minHeightForPorts = portsPerSide * (PortSize + PortGap) + PortGap;
        var imageHeight = Math.Max(ImageAreaHeight, minHeightForPorts);
        var height = imageHeight + ValueRowHeight + LimitRowHeight;

        var bounds = new RectF(x, y, CardWidth, height);
        var layout = new NodeLayout
        {
            Node = node,
            Bounds = bounds,
            ImageRect = new RectF(x + PortSize + 2, y + 2, CardWidth - 2 * (PortSize + 2), imageHeight - 4),
            ValueRect = new RectF(x, y + imageHeight, CardWidth, ValueRowHeight),
            LimitRect = new RectF(x + 6, y + imageHeight + ValueRowHeight + 1, CardWidth - 12, LimitRowHeight - 3),
            HasValueRow = true,
            HasLimitRow = true,
        };

        if (node.Kind == NodeKind.Recipe)
        {
            PlacePorts(layout.Inputs, inputParts, x, y, imageHeight, isInput: true);
            PlacePorts(layout.Outputs, outputParts, x + CardWidth - PortSize, y, imageHeight, isInput: false);
        }
        else
        {
            AddDynamicPorts(layout, node, scope, new RectF(x, y, CardWidth, imageHeight));
        }
        return layout;
    }

    /// <summary>
    /// Marker-sized badge for a snapped extractor: square icon area, one condensed
    /// value chip, and a single padded output port on the right edge. The output is
    /// the recipe's first produced part (extractors have exactly one).
    /// </summary>
    private static NodeLayout ComputeMapCompact(FactoryNode node, GameDatabase data, float x, float y)
    {
        var box = new RectF(x, y, MapCompactSize, MapCompactSize);
        var layout = new NodeLayout
        {
            Node = node,
            MapCompact = true,
            Bounds = box,
            ImageRect = box.Inflate(-5, -5),
            ValueRect = new RectF(x, y + MapCompactSize - MapCompactChipHeight, MapCompactSize, MapCompactChipHeight),
            HasValueRow = true,
            HasLimitRow = false,
        };

        var outputPart = Core.Saves.MapSnap.ExtractorOutputPart(data, node);
        if (outputPart is not null)
        {
            // Padded hit rect centered on the right edge for an easy port-drag target.
            var portTop = y + (MapCompactSize - MapCompactPortHit) / 2;
            layout.Outputs.Add(new PortInfo(
                outputPart,
                new RectF(x + MapCompactSize - MapCompactPortHit / 2, portTop, MapCompactPortHit, MapCompactPortHit),
                false));
        }
        return layout;
    }

    private static void PlacePorts(List<PortInfo> target, List<string> parts, float x, float y, float areaHeight, bool isInput)
    {
        if (parts.Count == 0) return;
        var total = parts.Count * (PortSize + PortGap) - PortGap;
        var startY = y + (areaHeight - total) / 2;
        for (var i = 0; i < parts.Count; i++)
            target.Add(new PortInfo(
                parts[i],
                new RectF(x, startY + i * (PortSize + PortGap), PortSize, PortSize),
                isInput));
    }

    /// <summary>
    /// Specialty machines (sinks, storage, splurgers, outposts) adopt whatever parts
    /// are connected; they also expose one "any part" stub port per side for new drags.
    /// </summary>
    private static void AddDynamicPorts(NodeLayout layout, FactoryNode node, FactoryGraph scope, RectF area)
    {
        var inParts = scope.IncomingTo(node).Select(c => c.Part).Distinct().ToList();
        var outParts = scope.OutgoingFrom(node).Select(c => c.Part).Distinct().ToList();

        var acceptsInputs = node.Kind is not NodeKind.Outpost and not NodeKind.Blueprint
            ? node.StorageMode != StorageMode.Full || node.Kind != NodeKind.StorageContainer
            : true;
        var providesOutputs = node.Kind switch
        {
            NodeKind.AwesomeSink or NodeKind.DimensionalDepot => false,
            NodeKind.StorageContainer => node.StorageMode != StorageMode.Empty,
            _ => true,
        };

        if (acceptsInputs && inParts.Count == 0) inParts.Add("AnyPart");
        if (providesOutputs && outParts.Count == 0)
            outParts.Add(inParts.FirstOrDefault(p => p != "AnyPart") ?? "AnyPart");

        PlacePorts(layout.Inputs, inParts, area.Left, area.Top, area.Height, isInput: true);
        PlacePorts(layout.Outputs, outParts, area.Right - PortSize, area.Top, area.Height, isInput: false);
    }

    public PortInfo? HitPort(PointF world)
    {
        foreach (var port in Inputs)
            if (port.IconRect.Contains(world)) return port;
        foreach (var port in Outputs)
            if (port.IconRect.Contains(world)) return port;
        return null;
    }

    public PointF PortAnchor(PortInfo port) => new(
        port.IsInput ? port.IconRect.Left : port.IconRect.Right,
        port.IconRect.Center.Y);
}
