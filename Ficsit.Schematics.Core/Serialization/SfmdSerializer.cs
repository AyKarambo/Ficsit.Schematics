using System.Text.Json;
using System.Text.Json.Nodes;
using Ficsit.Schematics.Core.GameData.Catalog;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Serialization;

/// <summary>
/// Reads and writes the reference application's .sfmd save format (plain JSON):
/// top-level canvas/calculation settings plus a flat "Data" array of nodes whose
/// "Inputs" reference producer nodes by array index. Unknown fields on import are
/// ignored; exported documents stick to the reference field set so the original
/// app can open them.
/// </summary>
public static class SfmdSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
    };

    private static readonly HashSet<string> SpecialtyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outpost", "Blueprint", "Splurger", "Priority Splitter", "Priority Merger",
        "Priority Splurger", "AWESOME Sink", "Storage Container", "Dimensional Depot",
        "Fuel-Powered Generator", "Coal-Powered Generator", "Nuclear Power Plant", "Biomass Burner",
    };

    /// <summary>
    /// Legacy per-fuel generator nodes ("Turbofuel Generator", "Coal Generator", …) migrate on
    /// load to the unified generator named by their machine. Derived from game data: every
    /// recipe of a <see cref="GameData.GameDatabase.GeneratorMachines"/> machine maps to that
    /// machine (including "Biomass Burner", whose recipe name equals the machine name).
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> GeneratorMachineByRecipe = new(() =>
    {
        var data = GameDataCatalog.Shared;
        return data.Document.Recipes
            .Where(r => data.GeneratorMachines.Contains(r.Machine))
            .ToDictionary(r => r.Name, r => r.Machine, StringComparer.Ordinal);
    });

    public static string Serialize(FactoryDocument document)
    {
        var root = new JsonObject
        {
            ["Version"] = "1.0",
            ["Language"] = document.Language,
            ["Solver"] = document.Solver,
            ["Zoom"] = document.Zoom,
            ["PanX"] = (int)Math.Round(document.PanX),
            ["PanY"] = (int)Math.Round(document.PanY),
            ["UseBuildingGrid"] = document.UseBuildingGrid,
            ["BuildingGridX"] = document.BuildingGridX,
            ["BuildingGridY"] = document.BuildingGridY,
            ["UseConnectionGrid"] = document.UseConnectionGrid,
            ["ConnectionGridX"] = document.ConnectionGridX,
            ["ConnectionGridY"] = document.ConnectionGridY,
            ["Path"] = document.Path,
            ["SpaceElevatorMultiplier"] = document.SpaceElevatorMultiplier,
            ["InputMultiplier"] = document.InputMultiplier,
            ["PowerMultiplier"] = document.PowerMultiplier,
            ["Data"] = SerializeGraph(document.Root),
        };
        return root.ToJsonString(WriteOptions);
    }

    private static JsonArray SerializeGraph(FactoryGraph graph)
    {
        var indexOf = new Dictionary<FactoryNode, int>();
        for (var i = 0; i < graph.Nodes.Count; i++)
            indexOf[graph.Nodes[i]] = i;

        var data = new JsonArray();
        foreach (var node in graph.Nodes)
        {
            var obj = new JsonObject
            {
                ["Name"] = node.Name,
                ["X"] = (int)Math.Round(node.X),
                ["Y"] = (int)Math.Round(node.Y),
            };

            if (!string.IsNullOrEmpty(node.Title)) obj["Title"] = node.Title;
            if (node.HasLimit) obj["Max"] = node.Max;
            if (node.ClockSpeed != Rational.One) obj["ClockSpeed"] = (node.ClockSpeed * 100).ToString();
            if (node.Somersloops > 0) obj["Somersloops"] = node.Somersloops;
            if (node.AutoRound) obj["AutoRound"] = true;
            if (node.ShowPpm is not null) obj["Ppm"] = node.ShowPpm.Value;
            if (node.MachineVariant is not null) obj["Machine"] = node.MachineVariant;
            if (node.Capacity is not null) obj["Capacity"] = node.Capacity;
            // Our extension (map mode); the reference app ignores unknown keys.
            if (node.ResourceNodeId is not null) obj["ResourceNode"] = node.ResourceNodeId;
            // Our extension (port reorder): persisted per-node display order.
            if (node.InputOrder.Count > 0) obj["InputOrder"] = PartArray(node.InputOrder);
            if (node.OutputOrder.Count > 0) obj["OutputOrder"] = PartArray(node.OutputOrder);
            // Flat outpost membership: which outpost (by index) this node belongs to.
            if (node.Parent is not null && indexOf.TryGetValue(node.Parent, out var parentIndex))
                obj["Parent"] = parentIndex;
            if (node.Kind == NodeKind.StorageContainer && node.StorageMode != StorageMode.PartiallyFull)
                obj["Mode"] = StorageModeName(node.StorageMode);

            if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                obj["Zoom"] = node.InnerZoom;
                obj["PanX"] = (int)Math.Round(node.InnerPanX);
                obj["PanY"] = (int)Math.Round(node.InnerPanY);
            }

            var inputs = new JsonObject();
            JsonObject? via = null; // our extension: logistics kind per vehicle-borne input
            foreach (var group in graph.IncomingTo(node).GroupBy(c => c.Part))
            {
                var sources = new JsonArray();
                foreach (var connection in group)
                    if (indexOf.TryGetValue(connection.From, out var index))
                    {
                        sources.Add(index);
                        if (connection.Logistics != LogisticsKind.None)
                        {
                            via ??= [];
                            if (via[group.Key] is not JsonObject partVia)
                                via[group.Key] = partVia = [];
                            partVia[index.ToString()] = connection.Logistics.ToString();
                        }
                    }
                if (sources.Count > 0) inputs[group.Key] = sources;
            }
            if (inputs.Count > 0) obj["Inputs"] = inputs;
            if (via is not null) obj["InputsVia"] = via;

            data.Add(obj);
        }
        return data;
    }

    private static JsonArray PartArray(List<string> parts)
    {
        var array = new JsonArray();
        foreach (var part in parts) array.Add(part);
        return array;
    }

    private static List<string> ReadParts(JsonArray array)
    {
        var parts = new List<string>(array.Count);
        foreach (var element in array)
        {
            var value = GetString(element, string.Empty);
            if (value.Length > 0) parts.Add(value);
        }
        return parts;
    }

    public static FactoryDocument Deserialize(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Save file is not a JSON object.");

        var document = new FactoryDocument
        {
            Language = root["Language"]?.GetValue<string>() ?? "en-US",
            Solver = root["Solver"]?.GetValue<string>() ?? "Basic",
            Zoom = GetDouble(root["Zoom"], 1.0),
            PanX = GetDouble(root["PanX"], 0),
            PanY = GetDouble(root["PanY"], 0),
            UseBuildingGrid = root["UseBuildingGrid"]?.GetValue<bool>() ?? false,
            BuildingGridX = GetString(root["BuildingGridX"], "100"),
            BuildingGridY = GetString(root["BuildingGridY"], "100"),
            UseConnectionGrid = root["UseConnectionGrid"]?.GetValue<bool>() ?? false,
            ConnectionGridX = GetString(root["ConnectionGridX"], "20"),
            ConnectionGridY = GetString(root["ConnectionGridY"], "20"),
            Path = GetString(root["Path"], "Curves"),
            SpaceElevatorMultiplier = GetString(root["SpaceElevatorMultiplier"], "1"),
            InputMultiplier = GetString(root["InputMultiplier"], "1"),
            PowerMultiplier = GetString(root["PowerMultiplier"], "1"),
        };

        if (root["Data"] is JsonArray data)
            document.Root = DeserializeGraph(data);
        return document;
    }

    private static FactoryGraph DeserializeGraph(JsonArray data)
    {
        var graph = new FactoryGraph();
        var legacyHandles = new List<FactoryNode>();
        ReadArray(data, graph, parent: null, legacyHandles);
        BypassLegacyHandles(graph, legacyHandles);
        return graph;
    }

    /// <summary>
    /// Migrate the superseded Import/Export boundary handles: an outpost boundary is now derived
    /// from crossing connections, so a saved handle (an <c>X → handle → Y</c> pass-through of one
    /// part) is replaced by the direct <c>X → Y</c> connection it stood for, then dropped. A
    /// dangling handle (one side unwired) simply disappears with its half-connection.
    /// </summary>
    private static void BypassLegacyHandles(FactoryGraph graph, List<FactoryNode> handles)
    {
        foreach (var handle in handles)
        {
            var incoming = graph.Connections.Where(c => c.To == handle).ToList();
            var outgoing = graph.Connections.Where(c => c.From == handle).ToList();
            foreach (var into in incoming)
                foreach (var outOf in outgoing)
                    if (into.Part == outOf.Part && into.From != outOf.To
                        && !graph.Connections.Any(c => c.From == into.From && c.To == outOf.To && c.Part == into.Part))
                        graph.Connections.Add(new NodeConnection { From = into.From, To = outOf.To, Part = into.Part });
        }
        foreach (var handle in handles) graph.RemoveNode(handle); // also drops the handle's own connections
    }

    /// <summary>
    /// Read a node array into the flat graph. Handles both layouts: the flat format (one array;
    /// <c>Parent</c> gives outpost membership by index, <c>Inputs</c> reference indices in this
    /// array) and the legacy nested format (an outpost carries a nested <c>Data</c> array with
    /// indices local to it) — nested children are flattened in with their <c>Parent</c> set.
    /// Saved Import/Export handles (the superseded boundary model) are collected into
    /// <paramref name="legacyHandles"/> for <see cref="BypassLegacyHandles"/>.
    /// </summary>
    private static void ReadArray(JsonArray data, FactoryGraph graph, FactoryNode? parent, List<FactoryNode> legacyHandles)
    {
        var local = new List<FactoryNode>();
        var pendingInputs = new List<(FactoryNode Node, string Part, List<int> Sources)>();
        var pendingParent = new List<(FactoryNode Node, int Index)>();
        var pendingVia = new Dictionary<(FactoryNode Node, string Part, int Source), LogisticsKind>();

        foreach (var element in data)
        {
            if (element is not JsonObject obj) continue;
            var name = GetString(obj["Name"], string.Empty);
            var node = new FactoryNode
            {
                Name = name,
                Kind = obj["Kind"] is { } kindNode
                    && Enum.TryParse<NodeKind>(GetString(kindNode, string.Empty), out var explicitKind)
                    ? explicitKind
                    : KindFor(name),
                X = GetDouble(obj["X"], 0),
                Y = GetDouble(obj["Y"], 0),
                Title = obj["Title"]?.GetValue<string>(),
                Max = obj["Max"]?.GetValue<string>(),
                Somersloops = (int)GetDouble(obj["Somersloops"], 0),
                AutoRound = obj["AutoRound"]?.GetValue<bool>() ?? false,
                MachineVariant = obj["Machine"]?.GetValue<string>(),
                Capacity = obj["Capacity"]?.GetValue<string>(),
                ResourceNodeId = obj["ResourceNode"]?.GetValue<string>(),
                Parent = parent,
            };

            // Superseded boundary handles: parsed as ordinary nodes (their Name is a part), then
            // migrated away by BypassLegacyHandles once all connections are wired.
            if (obj["Kind"] is { } rawKindNode && GetString(rawKindNode, string.Empty) is "Import" or "Export")
                legacyHandles.Add(node);
            // Legacy per-fuel generator node (a recipe of a generator machine, e.g. "Turbofuel
            // Generator"): load as the unified generator named by its machine. Its wired inputs
            // are preserved, so the solver resolves the same fuel recipe and the flows are
            // unchanged; an unwired one becomes a unified generator at rated power.
            else if (node.Kind == NodeKind.Recipe
                && GeneratorMachineByRecipe.Value.TryGetValue(name, out var generatorMachine))
            {
                node.Kind = NodeKind.Generator;
                node.Name = generatorMachine;
            }

            if (obj["Ppm"] is JsonValue ppm) node.ShowPpm = ppm.GetValue<bool>();
            if (obj["ClockSpeed"] is JsonValue clock
                && Rational.TryParse(GetString(clock, "100"), out var clockPercent)
                && clockPercent.IsPositive)
                node.ClockSpeed = clockPercent / 100;
            if (obj["Mode"] is JsonValue mode)
                node.StorageMode = StorageModeFromName(GetString(mode, string.Empty));
            if (obj["InputOrder"] is JsonArray inOrder) node.InputOrder = ReadParts(inOrder);
            if (obj["OutputOrder"] is JsonArray outOrder) node.OutputOrder = ReadParts(outOrder);

            if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                node.InnerZoom = GetDouble(obj["Zoom"], 1.0);
                node.InnerPanX = GetDouble(obj["PanX"], 0);
                node.InnerPanY = GetDouble(obj["PanY"], 0);
            }

            var parentIndex = (int)GetDouble(obj["Parent"], -1);
            if (parentIndex >= 0) pendingParent.Add((node, parentIndex));

            if (obj["Inputs"] is JsonObject inputs)
                foreach (var (part, sourcesNode) in inputs)
                    if (sourcesNode is JsonArray sources)
                        pendingInputs.Add((node, part,
                            sources.Select(s => (int)GetDouble(s, -1)).Where(i => i >= 0).ToList()));

            if (obj["InputsVia"] is JsonObject viaObj)
                foreach (var (part, mapNode) in viaObj)
                    if (mapNode is JsonObject map)
                        foreach (var (sourceText, kindValue) in map)
                            if (int.TryParse(sourceText, out var source)
                                && Enum.TryParse<LogisticsKind>(GetString(kindValue, string.Empty), out var kind))
                                pendingVia[(node, part, source)] = kind;

            graph.Nodes.Add(node);
            local.Add(node);

            // Legacy nested outpost: flatten its children in with Parent = this node.
            if (obj["Data"] is JsonArray nested)
                ReadArray(nested, graph, node, legacyHandles);
        }

        // Indices reference positions within THIS array (flat for the new format, local nested).
        foreach (var (node, part, sources) in pendingInputs)
            foreach (var sourceIndex in sources)
                if (sourceIndex >= 0 && sourceIndex < local.Count)
                    graph.Connections.Add(new NodeConnection
                    {
                        From = local[sourceIndex],
                        To = node,
                        Part = part,
                        Logistics = pendingVia.GetValueOrDefault((node, part, sourceIndex)),
                    });

        foreach (var (node, index) in pendingParent)
            if (index >= 0 && index < local.Count)
                node.Parent = local[index];
    }

    public static NodeKind KindFor(string name) => name switch
    {
        "Outpost" => NodeKind.Outpost,
        "Blueprint" => NodeKind.Blueprint,
        "Splurger" => NodeKind.Splurger,
        "Priority Splitter" => NodeKind.PrioritySplitter,
        "Priority Merger" => NodeKind.PriorityMerger,
        "Priority Splurger" => NodeKind.PrioritySplurger,
        "AWESOME Sink" => NodeKind.AwesomeSink,
        "Storage Container" => NodeKind.StorageContainer,
        "Dimensional Depot" => NodeKind.DimensionalDepot,
        // Unified fuel generators, derived from game data (a machine that produces power
        // and has fuel-burning recipes): Fuel-Powered / Coal-Powered / Nuclear / Biomass.
        _ when GameDataCatalog.Shared.GeneratorMachines.Contains(name) => NodeKind.Generator,
        _ => NodeKind.Recipe,
    };

    public static bool IsSpecialtyName(string name) => SpecialtyNames.Contains(name);

    private static string StorageModeName(StorageMode mode) => mode switch
    {
        StorageMode.Full => "Full",
        StorageMode.Empty => "Empty",
        StorageMode.InputEqualsOutput => "Input = Output",
        _ => "Partially Full",
    };

    private static StorageMode StorageModeFromName(string name) => name switch
    {
        "Full" => StorageMode.Full,
        "Empty" => StorageMode.Empty,
        "Input = Output" => StorageMode.InputEqualsOutput,
        _ => StorageMode.PartiallyFull,
    };

    private static string GetString(JsonNode? node, string fallback)
        => node switch
        {
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonValue value => value.ToJsonString().Trim('"'),
            _ => fallback,
        };

    private static double GetDouble(JsonNode? node, double fallback)
        => node is JsonValue value && value.TryGetValue<double>(out var d) ? d : fallback;
}
