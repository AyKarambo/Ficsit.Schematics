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
    public void Nested_outpost_children_roundtrip()
    {
        var doc = new FactoryDocument();
        var outpost = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost, Children = new FactoryGraph() };
        var inner = new FactoryNode { Name = "Iron Ingot", Max = "2" };
        outpost.Children.Nodes.Add(inner);
        doc.Root.Nodes.Add(outpost);

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));
        var reloadedOutpost = Assert.Single(reloaded.Root.Nodes);
        Assert.NotNull(reloadedOutpost.Children);
        var reloadedInner = Assert.Single(reloadedOutpost.Children!.Nodes);
        Assert.Equal("Iron Ingot", reloadedInner.Name);
        Assert.Equal("2", reloadedInner.Max);
    }
}
