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
    // TODO(ui-readability-ux #3): potentially unify with LabelHideZoomThreshold below.
    public const float MapCompactChipZoomThreshold = 0.6f;

    // ---- port / connection label-pill constants (Slice B — #3) ----------------

    /// <summary>Base font size for port and connection ppm labels (world units).</summary>
    public const float LabelFontSize = 9.5f;

    /// <summary>
    /// Minimum on-screen pixel size for label text. When <c>LabelFontSize × Zoom</c>
    /// drops below this value the font is clamped upward so labels stay legible.
    /// </summary>
    public const float LabelMinEffectivePx = 8f;

    /// <summary>
    /// Below this zoom level, even a size-clamped pill would collide with a
    /// neighbouring node's labels, so port labels are hidden entirely and surfaced
    /// on hover instead. Chosen so a typical inter-node gap (≥20 world units) can
    /// still contain the pill at this zoom; below it the pill overflows.
    /// Could later unify with MapCompactChipZoomThreshold (0.6) once a shared
    /// legibility threshold is formalised (ui-readability-ux #3 backlog).
    /// </summary>
    public const float LabelHideZoomThreshold = 0.4f;

    /// <summary>Horizontal padding added to each side of the measured text inside a label pill.</summary>
    public const float LabelPillPadX = 3f;

    /// <summary>Vertical padding added to each side of the measured text inside a label pill.</summary>
    public const float LabelPillPadY = 1.5f;

    /// <summary>Corner radius of label pills.</summary>
    public const float LabelPillCorner = 3f;

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
            // A bracket box: its ports are the parts that cross its boundary (a connection
            // with exactly one end inside it). Card-like so the icon sits between port columns.
            var (inParts, outParts) = OutpostBoundaryParts(node, scope);
            var boundaryPorts = Math.Max(inParts.Count, outParts.Count);
            var boxHeight = Math.Max(SpecialtySize, boundaryPorts * (PortSize + PortGap) + PortGap);
            var box = new RectF(x, y, CardWidth, boxHeight);
            var specialty = new NodeLayout
            {
                Node = node,
                Bounds = box,
                ImageRect = new RectF(x + PortSize + 2, y + 2, CardWidth - 2 * (PortSize + 2), boxHeight - 4),
                HasValueRow = false,
                HasLimitRow = false,
            };
            PlacePorts(specialty.Inputs, OrderParts(inParts, node.InputOrder), box.Left, box.Top, box.Height, isInput: true);
            PlacePorts(specialty.Outputs, OrderParts(outParts, node.OutputOrder), box.Right - PortSize, box.Top, box.Height, isInput: false);
            return specialty;
        }

        List<string> inputParts = [];
        List<string> outputParts = [];
        if (node.Kind == NodeKind.Recipe && data.RecipesByName.TryGetValue(node.Name, out var recipe))
        {
            inputParts = OrderParts(recipe.Inputs.Select(p => p.Part).ToList(), node.InputOrder);
            outputParts = OrderParts(recipe.Outputs.Select(p => p.Part).ToList(), node.OutputOrder);
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

    /// <summary>Apply a user port-order override: listed parts first (in override order),
    /// then any remaining parts in their natural order. Unknown override entries are ignored.</summary>
    public static List<string> OrderParts(List<string> parts, List<string> order)
    {
        if (order.Count == 0 || parts.Count < 2) return parts;
        var result = new List<string>(parts.Count);
        foreach (var part in order)
            if (parts.Contains(part) && !result.Contains(part))
                result.Add(part);
        foreach (var part in parts)
            if (!result.Contains(part))
                result.Add(part);
        return result;
    }

    /// <summary>The outpost box's ports, auto-derived from the connections that cross its
    /// boundary: a part entering it (a connection from outside to a member) is an input port,
    /// a part leaving it (member to outside) an output port. There are no stored boundary
    /// handles — membership and crossings are read straight off the flat graph.</summary>
    public static (List<string> InParts, List<string> OutParts) OutpostBoundaryParts(FactoryNode outpost, FactoryGraph graph)
    {
        var inParts = new List<string>();
        var outParts = new List<string>();

        foreach (var c in graph.Connections)
        {
            var fromInside = IsInside(c.From, outpost);
            var toInside = IsInside(c.To, outpost);
            if (toInside && !fromInside) { if (!inParts.Contains(c.Part)) inParts.Add(c.Part); }
            else if (fromInside && !toInside) { if (!outParts.Contains(c.Part)) outParts.Add(c.Part); }
        }
        return (inParts, outParts);
    }

    /// <summary>True when <paramref name="node"/> is a descendant of <paramref name="outpost"/>.</summary>
    public static bool IsInside(FactoryNode? node, FactoryNode outpost)
    {
        for (var p = node?.Parent; p is not null; p = p.Parent)
            if (p == outpost) return true;
        return false;
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

        PlacePorts(layout.Inputs, OrderParts(inParts, node.InputOrder), area.Left, area.Top, area.Height, isInput: true);
        PlacePorts(layout.Outputs, OrderParts(outParts, node.OutputOrder), area.Right - PortSize, area.Top, area.Height, isInput: false);
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
