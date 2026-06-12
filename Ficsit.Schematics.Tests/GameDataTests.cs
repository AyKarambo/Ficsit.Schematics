using System.Text.Json;
using System.Text.Json.Serialization;
using Ficsit.Schematics.Core.GameData;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class GameDataTests
{
    // ------------------------------------------------------------------ snapshot
    // Serializes all definitions to canonical ordered JSON and compares against
    // Fixtures/catalog-snapshot.json. If the fixture does not yet exist it is
    // generated from the current catalog and the test passes (first-run bootstrap).
    // After any restructure the definitions must be byte-identical to the fixture;
    // regenerate the fixture only when the data itself intentionally changes.

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string SerializeCatalogSnapshot()
    {
        var db = TestData.Database;

        // Canonical ordering: SortIndex (already applied by BuildDocument) then Name
        var snapshot = new
        {
            Machines = db.Document.Machines
                .Select(m => new {
                    m.Name, m.Tier,
                    m.AveragePower, m.MinPower, m.BasePower,
                    m.BasePowerBoost, m.FueledBasePowerBoost,
                    m.OverclockPowerExponent,
                    m.MaxProductionShards,
                    m.ProductionShardMultiplier, m.ProductionShardPowerExponent,
                    Cost = m.Cost.Select(c => new { c.Part, c.Amount }).ToList(),
                })
                .ToList(),

            MultiMachines = db.Document.MultiMachines
                .Select(mm => new {
                    mm.Name, mm.ShowPpm, mm.AutoRound, mm.DefaultMax,
                    Machines = mm.Machines.Select(v => new { v.Name, v.PartsRatio, v.Default }).ToList(),
                    Capacities = mm.Capacities.Select(c => new { c.Name, c.PartsRatio, c.PowerRatio, c.Default, c.Color }).ToList(),
                })
                .ToList(),

            Parts = db.Document.Parts
                .Select(p => new { p.Name, p.Tier, p.SinkPoints, p.Fluid, p.IsManuallyGathered })
                .ToList(),

            Recipes = db.Document.Recipes
                .Select(r => new {
                    r.Name, r.Machine, r.BatchTime, r.Tier,
                    r.Alternate, r.Ficsmas,
                    r.AveragePower, r.MinPower,
                    r.IgnoreInputMultiplier, r.SpaceElevatorMultiplier,
                    Parts = r.Parts.Select(p => new { p.Part, p.Amount }).ToList(),
                })
                .ToList(),
        };

        return JsonSerializer.Serialize(snapshot, SnapshotOptions);
    }

    [Fact]
    public void Catalog_definitions_match_snapshot()
    {
        var fixtureDir = Path.Combine(TestData.RepoRoot, "Ficsit.Schematics.Tests", "Fixtures");
        var fixturePath = Path.Combine(fixtureDir, "catalog-snapshot.json");

        var actual = SerializeCatalogSnapshot();

        if (!File.Exists(fixturePath))
        {
            // First run: bootstrap the fixture
            Directory.CreateDirectory(fixtureDir);
            File.WriteAllText(fixturePath, actual, System.Text.Encoding.UTF8);
            return; // Passes on generation
        }

        var expected = File.ReadAllText(fixturePath, System.Text.Encoding.UTF8);
        // Normalize line endings for comparison
        expected = expected.Replace("\r\n", "\n").Replace("\r", "\n");
        var actualNorm = actual.Replace("\r\n", "\n").Replace("\r", "\n");
        Assert.Equal(expected, actualNorm);
    }


    [Fact]
    public void Loads_complete_reference_data()
    {
        var db = TestData.Database;
        Assert.Equal(32, db.Document.Machines.Count);
        Assert.Equal(7, db.Document.MultiMachines.Count);
        Assert.Equal(170, db.Document.Parts.Count);
        Assert.Equal(332, db.Document.Recipes.Count);
    }

    [Fact]
    public void Every_recipe_machine_resolves()
    {
        var db = TestData.Database;
        foreach (var recipe in db.Document.Recipes)
        {
            var known = db.MachinesByName.ContainsKey(recipe.Machine)
                || db.MultiMachinesByName.ContainsKey(recipe.Machine)
                || db.MultiMachineFor(recipe.Machine) is not null;
            Assert.True(known, $"Recipe '{recipe.Name}' references unknown machine '{recipe.Machine}'.");
        }
    }

    [Fact]
    public void Every_recipe_part_resolves()
    {
        var db = TestData.Database;
        foreach (var recipe in db.Document.Recipes)
            foreach (var part in recipe.Parts)
                Assert.True(db.PartsByName.ContainsKey(part.Part),
                    $"Recipe '{recipe.Name}' references unknown part '{part.Part}'.");
    }

    [Fact]
    public void Every_part_and_machine_has_an_icon()
    {
        var db = TestData.Database;
        var missing = new List<string>();
        foreach (var name in db.Document.Parts.Select(p => p.Name)
                     .Concat(db.Document.Machines.Select(m => m.Name)))
        {
            var fileName = name.Replace(' ', '_').Replace(":", "") + ".png";
            var path = Path.Combine(TestData.IconsDir, fileName);
            if (!File.Exists(path)) missing.Add(name);
        }
        Assert.True(missing.Count == 0, "Missing icons: " + string.Join(", ", missing));
    }

    [Fact]
    public void Multimachine_families_resolve_their_variants()
    {
        var db = TestData.Database;
        foreach (var family in db.Document.MultiMachines)
            foreach (var variant in family.Machines)
                Assert.True(db.MachinesByName.ContainsKey(variant.Name),
                    $"Family '{family.Name}' variant '{variant.Name}' is not a machine.");
    }

    [Fact]
    public void Rates_use_batch_time()
    {
        var recipe = TestData.Database.RecipesByName["Iron Plate"];
        // 2 plates / 6 s → 20/min; 3 ingots / 6 s → -30/min.
        Assert.Equal("20", recipe.RatePerMinute("Iron Plate").ToString());
        Assert.Equal("-30", recipe.RatePerMinute("Iron Ingot").ToString());
    }
}
