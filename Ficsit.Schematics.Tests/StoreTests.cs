using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Data;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class StoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        System.IO.Path.GetTempPath(), $"ficsit-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void Current_factory_roundtrips_through_the_document_store()
    {
        var doc = new FactoryDocument { Solver = "Basic", Zoom = 1.5 };
        doc.Root.Nodes.Add(new FactoryNode { Name = "Iron Ingot", Max = "3", X = 10, Y = -20 });

        using (var store = new FicsitStore(_path))
            store.SaveCurrent(doc);

        using (var store = new FicsitStore(_path))
        {
            var loaded = store.LoadCurrent();
            Assert.NotNull(loaded);
            Assert.Equal(1.5, loaded!.Zoom);
            var node = Assert.Single(loaded.Root.Nodes);
            Assert.Equal("Iron Ingot", node.Name);
            Assert.Equal("3", node.Max);
            Assert.Equal(10, node.X);
        }
    }

    [Fact]
    public void Named_saves_and_backups_work()
    {
        using var store = new FicsitStore(_path);
        var doc = new FactoryDocument();
        doc.Root.Nodes.Add(new FactoryNode { Name = "Iron Plate" });

        store.SaveNamed("my factory", doc);
        Assert.Single(store.ListSaves());
        Assert.NotNull(store.LoadNamed("my factory"));

        for (var i = 0; i < 5; i++) store.AddBackup(doc, maxBackups: 3);
        Assert.Equal(3, store.ListBackups().Count);
        Assert.NotNull(store.LoadBackup(store.ListBackups()[0].Id));

        store.DeleteNamed("my factory");
        Assert.Empty(store.ListSaves());
    }

    [Fact]
    public void Settings_roundtrip()
    {
        using var store = new FicsitStore(_path);
        var settings = store.LoadSettings();
        Assert.True(settings.DarkMode);
        // Readability defaults are on.
        Assert.True(settings.WireColorByPart);
        Assert.True(settings.FocusHighlight);

        settings.DarkMode = false;
        settings.UiScale = 16;
        settings.Numbers["value"].DecimalPlaces = 3;
        settings.WireColorByPart = false;
        settings.FocusHighlight = false;
        store.SaveSettings(settings);

        var reloaded = store.LoadSettings();
        Assert.False(reloaded.DarkMode);
        Assert.Equal(16, reloaded.UiScale);
        Assert.Equal(3, reloaded.Numbers["value"].DecimalPlaces);
        Assert.False(reloaded.WireColorByPart);
        Assert.False(reloaded.FocusHighlight);
    }

    [Fact]
    public void ShowBeltCapacityWarnings_roundtrips()
    {
        using var store = new FicsitStore(_path);
        // Default is on.
        var settings = store.LoadSettings();
        Assert.True(settings.ShowBeltCapacityWarnings);

        // Persist the toggled-off value.
        settings.ShowBeltCapacityWarnings = false;
        store.SaveSettings(settings);

        var reloaded = store.LoadSettings();
        Assert.False(reloaded.ShowBeltCapacityWarnings);
    }

    [Fact]
    public void Planner_settings_roundtrip()
    {
        using var store = new FicsitStore(_path);
        var settings = store.LoadSettings();
        // Defaults: exclude manual on, ore conversion off, nothing disabled.
        Assert.True(settings.PlannerExcludeManualParts);
        Assert.False(settings.PlannerAllowOreConversion);
        Assert.Empty(settings.PlannerDisabledRecipes);
        Assert.Empty(settings.PlannerResourcePreferences);
        Assert.Equal(99, settings.PlannerMaxTierPhase);
        Assert.Equal(0, settings.PlannerSomersloopBudget);
        Assert.False(settings.PlannerAutoApply);
        Assert.True(settings.PlannerAutoCollapse);

        settings.PlannerExcludeManualParts = false;
        settings.PlannerAllowOreConversion = true;
        settings.PlannerDisabledRecipes.Add("Pure Iron Ingot");
        settings.PlannerDisabledRecipes.Add("Solid Steel Ingot");
        settings.PlannerResourcePreferences["Iron Ore"] = 40;
        settings.PlannerResourcePreferences["Crude Oil"] = 5;
        settings.PlannerMaxTierPhase = 7;
        settings.PlannerSomersloopBudget = 24;
        settings.PlannerAutoApply = true;
        settings.PlannerAutoCollapse = false;
        store.SaveSettings(settings);

        var reloaded = store.LoadSettings();
        Assert.False(reloaded.PlannerExcludeManualParts);
        Assert.True(reloaded.PlannerAllowOreConversion);
        Assert.Equal(2, reloaded.PlannerDisabledRecipes.Count);
        Assert.Contains("Pure Iron Ingot", reloaded.PlannerDisabledRecipes);
        Assert.Contains("Solid Steel Ingot", reloaded.PlannerDisabledRecipes);
        Assert.Equal(40, reloaded.PlannerResourcePreferences["Iron Ore"]);
        Assert.Equal(5, reloaded.PlannerResourcePreferences["Crude Oil"]);
        Assert.Equal(7, reloaded.PlannerMaxTierPhase);
        Assert.Equal(24, reloaded.PlannerSomersloopBudget);
        Assert.True(reloaded.PlannerAutoApply);
        Assert.False(reloaded.PlannerAutoCollapse);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
