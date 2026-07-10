using System.Text.Json.Nodes;
using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Serialization;
using Ficsit.Schematics.Core.Solver;
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
    public void Roundtrip_preserves_a_connections_logistics_kind()
    {
        var doc = new FactoryDocument();
        var producer = new FactoryNode { Name = "Iron Ingot", Kind = NodeKind.Recipe };
        var consumer = new FactoryNode { Name = "Iron Plate", Kind = NodeKind.Recipe };
        doc.Root.Nodes.AddRange([producer, consumer]);
        doc.Root.Connections.Add(new NodeConnection
            { From = producer, To = consumer, Part = "Iron Ingot", Logistics = LogisticsKind.Truck });

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));

        var connection = Assert.Single(reloaded.Root.Connections);
        Assert.Equal(LogisticsKind.Truck, connection.Logistics);
        Assert.Equal("Iron Ingot", connection.Part);
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

    [Fact]
    public void Generator_node_roundtrips_as_a_unified_machine()
    {
        var doc = new FactoryDocument();
        doc.Root.Nodes.Add(new FactoryNode { Name = "Fuel-Powered Generator", Kind = NodeKind.Generator, Max = "2" });

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));
        var node = Assert.Single(reloaded.Root.Nodes);
        Assert.Equal(NodeKind.Generator, node.Kind);
        Assert.Equal("Fuel-Powered Generator", node.Name);
        Assert.Equal("2", node.Max);
    }

    // --------------------------------------------- Legacy per-fuel generator migration (#16)

    [Fact]
    public void Legacy_per_fuel_generator_node_migrates_and_keeps_its_wired_fuel()
    {
        // Old save: a "Turbofuel Generator" recipe node with Turbofuel wired in. It must load
        // as the unified generator named by its machine, keep the connection, and solve to the
        // exact flows the per-fuel recipe produced (7.5 Turbofuel/min and 250 MW per machine).
        const string json = """
            {"Version":"1.0","Data":[
              {"Name":"Storage Container","X":0,"Y":0},
              {"Name":"Turbofuel Generator","X":100,"Y":0,"Max":"2","Inputs":{"Turbofuel":[0]}}
            ]}
            """;
        var doc = SfmdSerializer.Deserialize(json);

        var generator = doc.Root.Nodes.Single(n => n.Kind == NodeKind.Generator);
        Assert.Equal("Fuel-Powered Generator", generator.Name);
        var connection = Assert.Single(doc.Root.Connections);
        Assert.Same(generator, connection.To);
        Assert.Equal("Turbofuel", connection.Part);

        var result = new BasicSolver(TestData.Database).Solve(doc);
        Assert.Equal(new Rational(2), result.For(generator).Count);
        Assert.Equal(new Rational(500), result.For(generator).Power);           // 2 × 250 MW
        Assert.Equal(new Rational(15), result.For(generator).Inputs["Turbofuel"].Target); // 2 × 7.5/min
    }

    [Fact]
    public void Every_legacy_per_fuel_generator_recipe_migrates_to_its_machine()
    {
        // Data-driven over the whole catalog: every recipe of a generator machine is a legacy
        // node name that must load as that machine's unified generator (includes "Biomass
        // Burner", whose recipe name equals the machine name).
        var data = TestData.Database;
        var legacy = data.Document.Recipes
            .Where(r => data.GeneratorMachines.Contains(r.Machine)).ToList();
        Assert.NotEmpty(legacy);

        foreach (var recipe in legacy)
        {
            var json = $$"""{"Version":"1.0","Data":[{"Name":"{{recipe.Name}}","X":0,"Y":0}]}""";
            var node = Assert.Single(SfmdSerializer.Deserialize(json).Root.Nodes);
            Assert.Equal(NodeKind.Generator, node.Kind);
            Assert.Equal(recipe.Machine, node.Name);
        }
    }

    [Fact]
    public void Unwired_legacy_generator_node_reports_rated_power_for_its_count()
    {
        // Documented behavior change: with no fuel wired, the migrated node is a unified
        // generator at rated power (a legacy recipe node showed fuel consumption even unwired).
        const string json = """
            {"Version":"1.0","Data":[{"Name":"Coal Generator","X":0,"Y":0,"Max":"2"}]}
            """;
        var doc = SfmdSerializer.Deserialize(json);

        var generator = Assert.Single(doc.Root.Nodes);
        Assert.Equal(NodeKind.Generator, generator.Kind);
        Assert.Equal("Coal-Powered Generator", generator.Name);

        var result = new BasicSolver(TestData.Database).Solve(doc);
        Assert.Equal(new Rational(2), result.For(generator).Count);
        Assert.Equal(new Rational(150), result.For(generator).Power); // 2 × 75 MW rated
    }

    [Fact]
    public void Migrated_document_saves_machine_named_generators_and_roundtrips_stably()
    {
        const string json = """
            {"Version":"1.0","Data":[
              {"Name":"Storage Container","X":0,"Y":0},
              {"Name":"Turbofuel Generator","X":100,"Y":0,"Inputs":{"Turbofuel":[0]}},
              {"Name":"Uranium Nuclear Power Plant","X":200,"Y":0}
            ]}
            """;
        var doc = SfmdSerializer.Deserialize(json);
        var saved = SfmdSerializer.Serialize(doc);

        // The saved file carries only machine-named generators — no legacy per-fuel names.
        var data = TestData.Database;
        foreach (var recipe in data.Document.Recipes
                     .Where(r => data.GeneratorMachines.Contains(r.Machine) && r.Name != r.Machine))
            Assert.DoesNotContain(recipe.Name, saved);

        // And reloads stably: same names, kinds and wiring.
        var reloaded = SfmdSerializer.Deserialize(saved);
        Assert.Equal(doc.Root.Nodes.Select(n => (n.Name, n.Kind)),
            reloaded.Root.Nodes.Select(n => (n.Name, n.Kind)));
        var connection = Assert.Single(reloaded.Root.Connections);
        Assert.Equal("Turbofuel", connection.Part);
        Assert.Equal("Fuel-Powered Generator", connection.To.Name);
    }

    [Fact]
    public void Auto_plan_created_per_fuel_generator_round_trips_with_identical_flows()
    {
        // Auto-Plan (MainPage.AutoPlan BuildPlanOnCanvas) materializes planner rows via
        // editor.AddNode(recipeName) — including per-fuel generator recipes. A power plan
        // with Coal provisioned wires only the in-plan Water to the generator; the current
        // document round-trips through this serializer on every app restart (FicsitStore)
        // and on copy/paste, so the solved flows must survive Serialize → Deserialize.
        var editor = new FactoryEditor(TestData.Database);
        var water = editor.AddNode("Storage Container", 0, 0);
        water.StorageMode = StorageMode.Full; // stands in for the plan's water extractors
        var gen = editor.AddNode("Coal Generator", 100, 0);
        editor.SetLimit(gen, "4");
        editor.Connect(water, "Water", gen);

        var solver = new BasicSolver(TestData.Database);
        var before = solver.Solve(editor.Document);
        // Coal Generator: In Coal 1, In Water 3, Batch 4 → 15 coal + 45 water /min each.
        Assert.Equal(new Rational(60), before.For(gen).Inputs["Coal"].Target);
        Assert.Equal(new Rational(180), before.For(gen).Inputs["Water"].Target);
        Assert.Equal(new Rational(300), before.For(gen).Power); // 4 × 75 MW

        var reloaded = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(editor.Document));
        var reloadedGen = reloaded.Root.Nodes.Single(n => n.X == 100);
        Assert.Equal(gen.Name, reloadedGen.Name);
        Assert.Equal(gen.Kind, reloadedGen.Kind);

        var after = solver.Solve(reloaded);
        Assert.Equal(new Rational(60), after.For(reloadedGen).Inputs["Coal"].Target);
        Assert.Equal(new Rational(180), after.For(reloadedGen).Inputs["Water"].Target);
        Assert.Equal(new Rational(300), after.For(reloadedGen).Power);
    }

    [Fact]
    public void Legacy_import_export_handles_migrate_to_direct_connections()
    {
        // Superseded boundary model: miner (root) → Import handle → smelter (member). The handle
        // is dropped and replaced by the direct miner → smelter connection it stood for, so the
        // flat graph carries no Import/Export nodes.
        const string json = """
            {"Version":"1.0","Data":[
              {"Name":"Iron Ore","X":0,"Y":0,"Max":"60"},
              {"Name":"Outpost","X":50,"Y":0},
              {"Name":"Iron Ore","X":40,"Y":0,"Kind":"Import","Parent":1,"Inputs":{"Iron Ore":[0]}},
              {"Name":"Iron Ingot","X":80,"Y":0,"Parent":1,"Inputs":{"Iron Ore":[2]}}
            ]}
            """;
        var doc = SfmdSerializer.Deserialize(json);

        Assert.Equal(3, doc.Root.Nodes.Count); // miner, outpost, smelter — the handle is gone
        var miner = doc.Root.Nodes.First(n => n.Name == "Iron Ore");
        var smelter = doc.Root.Nodes.Single(n => n.Name == "Iron Ingot");
        var conn = Assert.Single(doc.Root.Connections);
        Assert.Same(miner, conn.From);
        Assert.Same(smelter, conn.To);
        Assert.Equal("Iron Ore", conn.Part);
        Assert.Same(doc.Root.Nodes.Single(n => n.Kind == NodeKind.Outpost), smelter.Parent);
    }
}
