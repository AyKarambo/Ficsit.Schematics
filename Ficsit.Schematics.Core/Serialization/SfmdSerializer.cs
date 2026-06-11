using System.Text.Json;
using System.Text.Json.Nodes;
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
    };

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
            if (node.Kind == NodeKind.StorageContainer && node.StorageMode != StorageMode.PartiallyFull)
                obj["Mode"] = StorageModeName(node.StorageMode);

            if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                obj["Zoom"] = node.InnerZoom;
                obj["PanX"] = (int)Math.Round(node.InnerPanX);
                obj["PanY"] = (int)Math.Round(node.InnerPanY);
                if (node.Children is { Nodes.Count: > 0 })
                    obj["Data"] = SerializeGraph(node.Children);
            }

            var inputs = new JsonObject();
            foreach (var group in graph.IncomingTo(node).GroupBy(c => c.Part))
            {
                var sources = new JsonArray();
                foreach (var connection in group)
                    if (indexOf.TryGetValue(connection.From, out var index))
                        sources.Add(index);
                if (sources.Count > 0) inputs[group.Key] = sources;
            }
            if (inputs.Count > 0) obj["Inputs"] = inputs;

            data.Add(obj);
        }
        return data;
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
        var byIndex = new List<FactoryNode>();
        var pendingInputs = new List<(FactoryNode Node, string Part, List<int> Sources)>();

        foreach (var element in data)
        {
            if (element is not JsonObject obj) continue;
            var name = GetString(obj["Name"], string.Empty);
            var node = new FactoryNode
            {
                Name = name,
                Kind = KindFor(name),
                X = GetDouble(obj["X"], 0),
                Y = GetDouble(obj["Y"], 0),
                Title = obj["Title"]?.GetValue<string>(),
                Max = obj["Max"]?.GetValue<string>(),
                Somersloops = (int)GetDouble(obj["Somersloops"], 0),
                AutoRound = obj["AutoRound"]?.GetValue<bool>() ?? false,
                MachineVariant = obj["Machine"]?.GetValue<string>(),
                Capacity = obj["Capacity"]?.GetValue<string>(),
            };

            if (obj["Ppm"] is JsonValue ppm) node.ShowPpm = ppm.GetValue<bool>();
            if (obj["ClockSpeed"] is JsonValue clock
                && Rational.TryParse(GetString(clock, "100"), out var clockPercent)
                && clockPercent.IsPositive)
                node.ClockSpeed = clockPercent / 100;
            if (obj["Mode"] is JsonValue mode)
                node.StorageMode = StorageModeFromName(GetString(mode, string.Empty));

            if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                node.InnerZoom = GetDouble(obj["Zoom"], 1.0);
                node.InnerPanX = GetDouble(obj["PanX"], 0);
                node.InnerPanY = GetDouble(obj["PanY"], 0);
                if (obj["Data"] is JsonArray nested)
                    node.Children = DeserializeGraph(nested);
            }

            if (obj["Inputs"] is JsonObject inputs)
                foreach (var (part, sourcesNode) in inputs)
                    if (sourcesNode is JsonArray sources)
                        pendingInputs.Add((node, part,
                            sources.Select(s => (int)GetDouble(s, -1)).Where(i => i >= 0).ToList()));

            graph.Nodes.Add(node);
            byIndex.Add(node);
        }

        foreach (var (node, part, sources) in pendingInputs)
            foreach (var sourceIndex in sources)
                if (sourceIndex < byIndex.Count)
                    graph.Connections.Add(new NodeConnection
                    {
                        From = byIndex[sourceIndex],
                        To = node,
                        Part = part,
                    });

        return graph;
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
