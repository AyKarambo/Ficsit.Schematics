using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// Map-mode snapping logic — which free marker an extractor latches onto and which
/// recipe a bare marker spawns. These are the pure pieces of the map-extractor
/// feature; the badge rendering and interaction feel are verified by running the app.
/// </summary>
public class MapSnapTests
{
    private static GameDatabase Data => TestData.Database;

    /// <summary>An Iron Ore miner node (the recipe is literally named "Iron Ore").</summary>
    private static FactoryNode IronMiner(string? snappedTo = null) =>
        new() { Kind = NodeKind.Recipe, Name = "Iron Ore", ResourceNodeId = snappedTo };

    /// <summary>An ore marker at the given canvas-unit (= metre) position, carrying a part.</summary>
    private static ResourceNodeInfo Marker(string instance, string part, double canvasX, double canvasY,
        string purity = "Normal", ResourceNodeKind kind = ResourceNodeKind.Node) =>
        new()
        {
            Instance = instance,
            Part = part,
            Purity = purity,
            Kind = kind,
            X = canvasX * MapSnap.CmPerUnit,
            Y = canvasY * MapSnap.CmPerUnit,
        };

    [Fact]
    public void NearestCandidate_picks_the_marker_closest_to_the_pointer()
    {
        // Three iron markers in a tight cluster; the pointer sits next to the middle one.
        var markers = new List<ResourceNodeInfo>
        {
            Marker("a", "Iron Ore", 0, 0),
            Marker("b", "Iron Ore", 60, 0),
            Marker("c", "Iron Ore", 120, 0),
        };

        var best = MapSnap.NearestCandidate(Data, IronMiner(), markers, new HashSet<string>(), 58, 5);

        Assert.NotNull(best);
        Assert.Equal("b", best!.Instance);
    }

    [Fact]
    public void NearestCandidate_excludes_occupied_markers()
    {
        var markers = new List<ResourceNodeInfo>
        {
            Marker("a", "Iron Ore", 0, 0),
            Marker("b", "Iron Ore", 60, 0),
        };
        // Pointer is right on "a", but "a" is taken by another machine.
        var occupied = new HashSet<string> { "a" };

        var best = MapSnap.NearestCandidate(Data, IronMiner(), markers, occupied, 0, 0);

        Assert.NotNull(best);
        Assert.Equal("b", best!.Instance);
    }

    [Fact]
    public void NearestCandidate_keeps_the_nodes_own_marker_available()
    {
        // The extractor already sits on "a"; a tiny wobble must not unsnap it.
        var markers = new List<ResourceNodeInfo> { Marker("a", "Iron Ore", 0, 0) };
        var occupied = new HashSet<string> { "a" };

        var best = MapSnap.NearestCandidate(Data, IronMiner(snappedTo: "a"), markers, occupied, 3, 0);

        Assert.NotNull(best);
        Assert.Equal("a", best!.Instance);
    }

    [Fact]
    public void NearestCandidate_ignores_non_matching_parts()
    {
        var markers = new List<ResourceNodeInfo>
        {
            Marker("copper", "Copper Ore", 0, 0),
            Marker("iron", "Iron Ore", 120, 0),
        };

        // Pointer sits on the copper marker, but an iron miner cannot snap to it.
        var best = MapSnap.NearestCandidate(Data, IronMiner(), markers, new HashSet<string>(), 0, 0);

        Assert.NotNull(best);
        Assert.Equal("iron", best!.Instance);
    }

    [Fact]
    public void NearestCandidate_returns_null_beyond_the_snap_radius()
    {
        var markers = new List<ResourceNodeInfo> { Marker("a", "Iron Ore", 0, 0) };

        Assert.Null(MapSnap.NearestCandidate(
            Data, IronMiner(), markers, new HashSet<string>(), MapSnap.SnapRadius + 50, 0));
    }

    [Fact]
    public void RecipeForMarker_maps_an_ore_marker_to_its_miner_recipe()
        => Assert.Equal("Iron Ore", MapSnap.RecipeForMarker(Data, Marker("a", "Iron Ore", 0, 0)));

    [Fact]
    public void RecipeForMarker_maps_a_geyser_to_a_geothermal_generator()
    {
        var geyser = Marker("g", "Geyser", 0, 0, kind: ResourceNodeKind.Geyser);
        var recipeName = MapSnap.RecipeForMarker(Data, geyser);

        Assert.NotNull(recipeName);
        Assert.True(Data.RecipesByName.TryGetValue(recipeName!, out var recipe));
        var family = Data.MultiMachineFor(recipe!.Machine)?.Name ?? recipe.Machine;
        Assert.Equal("Geothermal Generator", family);
    }

    [Fact]
    public void RecipeForMarker_maps_crude_oil_to_the_oil_extractor()
    {
        var recipeName = MapSnap.RecipeForMarker(Data, Marker("o", "Crude Oil", 0, 0));

        Assert.NotNull(recipeName);
        Assert.True(Data.RecipesByName.TryGetValue(recipeName!, out var recipe));
        Assert.Equal("Oil Extractor", recipe!.Machine);
    }

    [Fact]
    public void ExtractorOutputPart_is_the_recipes_single_product()
    {
        // The compact badge's lone output port carries this part.
        Assert.Equal("Iron Ore", MapSnap.ExtractorOutputPart(Data, IronMiner()));
    }

    [Fact]
    public void Matches_rejects_a_pressurizer_recipe_on_a_plain_node()
    {
        // A fracking core needs a Pressurizer; a Miner recipe must not match it.
        var core = Marker("core", "Water", 0, 0, kind: ResourceNodeKind.FrackingCore);
        Assert.False(MapSnap.Matches(Data, IronMiner(), core));
    }
}
