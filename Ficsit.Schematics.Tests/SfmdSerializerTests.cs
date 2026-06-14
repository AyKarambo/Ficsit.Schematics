using System.Text.Json.Nodes;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Serialization;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SfmdSerializerTests
{
    [Fact]
    public void Reads_the_real_reference_save()
    {
        var doc = SfmdSerializer.Deserialize(File.ReadAllText(TestData.ReferenceSavePath));

        Assert.Equal("Full", doc.Solver);
        Assert.Equal(5, doc.Root.Nodes.Count);
        Assert.Equal(NodeKind.Outpost, doc.Root.Nodes[0].Kind);

        var miner = doc.Root.Nodes.Single(n => n.Name == "Limestone");
        Assert.Equal("60", miner.Max);

        // Inputs resolve by Data-array index: Fine Concrete ← node 1 (the miner).
        var concrete = doc.Root.Nodes.Single(n => n.Name == "Fine Concrete");
        var connection = Assert.Single(doc.Root.Connections, c => c.To == concrete);
        Assert.Same(miner, connection.From);
        Assert.Equal("Limestone", connection.Part);

        // Heavy Flexible Frame ← node 4 (Encased Industrial Pipe) carrying EIB.
        var frame = doc.Root.Nodes.Single(n => n.Name == "Heavy Flexible Frame");
        var pipe = doc.Root.Nodes.Single(n => n.Name == "Encased Industrial Pipe");
        var eib = Assert.Single(doc.Root.Connections, c => c.To == frame);
        Assert.Same(pipe, eib.From);
        Assert.Equal("Encased Industrial Beam", eib.Part);
    }

    [Fact]
    public void Roundtrip_preserves_structure()
    {
        var original = SfmdSerializer.Deserialize(File.ReadAllText(TestData.ReferenceSavePath));
        var json = SfmdSerializer.Serialize(original);
        var reloaded = SfmdSerializer.Deserialize(json);

        Assert.Equal(original.Root.Nodes.Count, reloaded.Root.Nodes.Count);
        Assert.Equal(original.Root.Connections.Count, reloaded.Root.Connections.Count);
        Assert.Equal(original.Solver, reloaded.Solver);
        Assert.Equal(original.Zoom, reloaded.Zoom);

        for (var i = 0; i < original.Root.Nodes.Count; i++)
        {
            Assert.Equal(original.Root.Nodes[i].Name, reloaded.Root.Nodes[i].Name);
            Assert.Equal(original.Root.Nodes[i].X, reloaded.Root.Nodes[i].X);
            Assert.Equal(original.Root.Nodes[i].Y, reloaded.Root.Nodes[i].Y);
            Assert.Equal(original.Root.Nodes[i].Max, reloaded.Root.Nodes[i].Max);
        }
    }

    [Fact]
    public void Export_keeps_reference_field_names()
    {
        var original = SfmdSerializer.Deserialize(File.ReadAllText(TestData.ReferenceSavePath));
        var exported = JsonNode.Parse(SfmdSerializer.Serialize(original))!.AsObject();

        foreach (var key in new[]
                 {
                     "Version", "Language", "Solver", "Zoom", "PanX", "PanY",
                     "UseBuildingGrid", "BuildingGridX", "BuildingGridY",
                     "UseConnectionGrid", "ConnectionGridX", "ConnectionGridY",
                     "Path", "SpaceElevatorMultiplier", "InputMultiplier", "PowerMultiplier", "Data",
                 })
            Assert.True(exported.ContainsKey(key), $"Exported save misses '{key}'.");

        var data = exported["Data"]!.AsArray();
        var minerEntry = data.Select(n => n!.AsObject()).Single(o => o["Name"]!.GetValue<string>() == "Limestone");
        Assert.Equal("60", minerEntry["Max"]!.GetValue<string>());

        var frameEntry = data.Select(n => n!.AsObject()).Single(o => o["Name"]!.GetValue<string>() == "Heavy Flexible Frame");
        var inputs = frameEntry["Inputs"]!.AsObject();
        Assert.True(inputs.ContainsKey("Encased Industrial Beam"));
    }

    [Fact]
    public void Port_order_overrides_roundtrip()
    {
        var doc = new FactoryDocument();
        var node = new FactoryNode
        {
            Name = "Heavy Flexible Frame",
            InputOrder = ["Rubber", "Modular Frame", "Screw"],
            OutputOrder = ["Heavy Modular Frame"],
        };
        doc.Root.Nodes.Add(node);

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));
        var n = Assert.Single(reloaded.Root.Nodes);
        Assert.Equal(new[] { "Rubber", "Modular Frame", "Screw" }, n.InputOrder);
        Assert.Equal(new[] { "Heavy Modular Frame" }, n.OutputOrder);
    }

    [Fact]
    public void Empty_port_order_is_omitted_from_json()
    {
        var doc = new FactoryDocument();
        doc.Root.Nodes.Add(new FactoryNode { Name = "Iron Ingot" });
        var entry = JsonNode.Parse(SfmdSerializer.Serialize(doc))!.AsObject()["Data"]!.AsArray()[0]!.AsObject();
        Assert.False(entry.ContainsKey("InputOrder"));
        Assert.False(entry.ContainsKey("OutputOrder"));
    }

    [Fact]
    public void Outpost_membership_roundtrips_flat()
    {
        // Flat model: outpost + member in one list, membership via Parent (serialized by index).
        var doc = new FactoryDocument();
        var outpost = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost };
        var inner = new FactoryNode { Name = "Iron Ingot", Max = "2", Parent = outpost };
        doc.Root.Nodes.Add(outpost);
        doc.Root.Nodes.Add(inner);

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));
        Assert.Equal(2, reloaded.Root.Nodes.Count);
        var ro = reloaded.Root.Nodes.Single(n => n.Kind == NodeKind.Outpost);
        var ri = reloaded.Root.Nodes.Single(n => n.Name == "Iron Ingot");
        Assert.Same(ro, ri.Parent);
        Assert.Equal("2", ri.Max);
    }

    [Fact]
    public void Legacy_nested_outpost_save_migrates_to_flat()
    {
        // Old format: an outpost carries a nested "Data" array with locally-indexed Inputs.
        // It must flatten into one list, set Parent, and remap the connection.
        const string json = """
            {"Version":"1.0","Data":[{"Name":"Outpost","X":0,"Y":0,"Data":[{"Name":"Limestone","X":0,"Y":0,"Max":"60"},{"Name":"Fine Concrete","X":10,"Y":0,"Inputs":{"Limestone":[0]}}]}]}
            """;
        var doc = SfmdSerializer.Deserialize(json);

        Assert.Equal(3, doc.Root.Nodes.Count); // outpost + 2 members, flattened
        var outpost = doc.Root.Nodes.Single(n => n.Kind == NodeKind.Outpost);
        var concrete = doc.Root.Nodes.Single(n => n.Name == "Fine Concrete");
        var limestone = doc.Root.Nodes.Single(n => n.Name == "Limestone");
        Assert.Same(outpost, concrete.Parent);
        Assert.Same(outpost, limestone.Parent);
        var conn = Assert.Single(doc.Root.Connections);
        Assert.Same(limestone, conn.From);
        Assert.Same(concrete, conn.To);
        Assert.Equal("Limestone", conn.Part);
    }
}
