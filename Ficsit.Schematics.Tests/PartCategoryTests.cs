using Ficsit.Schematics.Core.GameData;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class PartCategoryTests
{
    private static PartDefinition Part(string name, bool fluid = false)
        => new() { Name = name, Fluid = fluid };

    // ---- Fluid ----------------------------------------------------------

    [Theory]
    [InlineData("Water")]
    [InlineData("Crude Oil")]
    [InlineData("Nitrogen Gas")]
    public void Fluid_parts_are_classified_as_Fluid(string name)
    {
        var part = Part(name, fluid: true);
        Assert.Equal(PartCategory.Fluid, PartCategoryClassifier.Classify(part));
    }

    // ---- Raw resources --------------------------------------------------

    [Theory]
    [InlineData("Iron Ore")]
    [InlineData("Limestone")]
    [InlineData("Coal")]
    [InlineData("Copper Ore")]
    [InlineData("Caterium Ore")]
    [InlineData("Raw Quartz")]
    [InlineData("Bauxite")]
    [InlineData("Sulfur")]
    [InlineData("SAM")]
    [InlineData("Uranium")]
    public void Non_fluid_ScarcityWeighted_resources_are_classified_as_Raw(string name)
    {
        var part = Part(name, fluid: false);
        Assert.Equal(PartCategory.Raw, PartCategoryClassifier.Classify(part));
    }

    // Water is in the weighted-resources set but is always a fluid in-game.
    [Fact]
    public void Water_is_Fluid_because_Fluid_flag_takes_priority()
    {
        // In the real catalog Water has Fluid=true; but even if someone passes
        // it as non-fluid, the name is in the raw set so it would be Raw.
        // The real database marks it as fluid, so it should be Fluid.
        var db = TestData.Database;
        Assert.True(db.PartsByName.ContainsKey("Water"));
        var water = db.PartsByName["Water"];
        // Water is a fluid → category must be Fluid.
        Assert.Equal(PartCategory.Fluid, PartCategoryClassifier.Classify(water));
    }

    // ---- Intermediates --------------------------------------------------

    [Theory]
    [InlineData("Iron Plate")]
    [InlineData("Iron Rod")]
    [InlineData("Copper Sheet")]
    [InlineData("Plastic")]
    [InlineData("Rubber")]
    [InlineData("Computer")]
    [InlineData("Adaptive Control Unit")]
    public void Crafted_parts_are_classified_as_Intermediate(string name)
    {
        var part = Part(name, fluid: false);
        Assert.Equal(PartCategory.Intermediate, PartCategoryClassifier.Classify(part));
    }

    // ---- Full-database smoke test ---------------------------------------

    [Fact]
    public void All_catalog_parts_classify_without_throwing()
    {
        var db = TestData.Database;
        foreach (var part in db.Document.Parts)
        {
            var category = PartCategoryClassifier.Classify(part);
            Assert.True(Enum.IsDefined(category), $"Unknown category for '{part.Name}'");
        }
    }

    [Fact]
    public void Catalog_contains_all_three_categories()
    {
        var db = TestData.Database;
        var categories = db.Document.Parts
            .Select(p => PartCategoryClassifier.Classify(p))
            .ToHashSet();
        Assert.Contains(PartCategory.Fluid, categories);
        Assert.Contains(PartCategory.Raw, categories);
        Assert.Contains(PartCategory.Intermediate, categories);
    }
}
