using Xunit;

namespace Ficsit.Schematics.Tests;

public class GameDataTests
{
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
