using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// Integration tests over the two committed real-save fixtures (issue #17): the rail save
/// (dune_desert — 21 train stations, 9 timetabled trains, 40 freight platforms) and the
/// rail-free save (random.node — 3 truck routes, zero rail content, the no-false-positives
/// gate). Each save is read once and shared across tests.
/// </summary>
public class RealSaveTests
{
    private static readonly Lazy<SaveWorld> DuneDesertLazy =
        new(() => SatisfactorySaveReader.ReadWorld(TestData.DuneDesertSavePath));
    private static readonly Lazy<SaveWorld> RandomNodeLazy =
        new(() => SatisfactorySaveReader.ReadWorld(TestData.RandomNodeSavePath));

    private static SaveWorld DuneDesert => DuneDesertLazy.Value;
    private static SaveWorld RandomNode => RandomNodeLazy.Value;

    // ------------------------------------------------- blob selection (AC1)

    [Fact]
    public void Dune_desert_selects_the_real_persistent_level()
    {
        // Before the walk-validated blob selection, a junk length-pair (fake object count
        // 1,179,648) beat the real persistent level (90,530 objects) on this 107 MB body and the
        // per-object scan silently yielded nothing: 0 links, 0 routes, no clocks or Somersloops.
        Assert.True(DuneDesert.Buildings.Count > 20_000,
            $"Expected the full built world, got {DuneDesert.Buildings.Count} buildings.");
        Assert.True(DuneDesert.ComponentLinks.Count > 10_000,
            $"Expected tens of thousands of component links, got {DuneDesert.ComponentLinks.Count}.");
        Assert.NotEmpty(DuneDesert.VehicleRoutes);

        // Per-building object data must be attributed: clocks, Somersloops, recipes.
        Assert.True(DuneDesert.Buildings.Count(b => b.ClockSpeed != Rational.One) > 100);
        Assert.Contains(DuneDesert.Buildings, b => b.Somersloops > 0);
        Assert.True(DuneDesert.Buildings.Count(b => b.RecipeStem is not null) > 500);
    }

    [Fact]
    public void Random_node_still_reads_fully_after_the_blob_fix()
    {
        Assert.True(RandomNode.Buildings.Count > 5_000,
            $"Expected the full built world, got {RandomNode.Buildings.Count} buildings.");
        Assert.True(RandomNode.ComponentLinks.Count > 5_000,
            $"Expected thousands of component links, got {RandomNode.ComponentLinks.Count}.");
    }

    // --------------------------------------------------- train routes (AC2)

    [Fact]
    public void Dune_desert_resolves_its_nine_timetables_into_train_routes()
    {
        var trains = DuneDesert.VehicleRoutes.Where(r => r.Kind == LogisticsKind.Train).ToList();
        Assert.Equal(9, trains.Count); // one per timetabled train
        Assert.All(trains, r => Assert.True(r.Stations.Count >= 2));
        // Routes run over freight platforms (where the belts terminate), never the station
        // buildings themselves; platforms shared by several trains appear in several routes.
        Assert.All(trains, r => Assert.All(r.Stations,
            s => Assert.Contains(".Build_TrainDockingStation", s)));
        var distinct = trains.SelectMany(r => r.Stations).Distinct().Count();
        Assert.InRange(distinct, 10, 40); // the save has 40 freight platforms
    }

    [Fact]
    public void Dune_desert_materializes_dashed_train_connections_on_import()
    {
        var (nodes, connections) = SaveImport.Build(DuneDesert, TestData.Database);

        Assert.NotEmpty(nodes);
        var trains = connections.Where(c => c.Logistics == LogisticsKind.Train).ToList();
        Assert.NotEmpty(trains); // real cargo crosses the rails (e.g. Compacted Coal → Turbofuel)
        Assert.All(trains, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Part));
            Assert.NotEqual(c.From, c.To);
        });
    }

    // ------------------------------------------- truck regression gate (AC5)

    [Fact]
    public void Random_node_keeps_its_three_truck_routes()
    {
        var trucks = RandomNode.VehicleRoutes.Where(r => r.Kind == LogisticsKind.Truck).ToList();
        Assert.Equal(3, trucks.Count);
        Assert.Equal([2, 4, 13], trucks.Select(r => r.Stations.Count).OrderBy(n => n));
    }

    [Fact]
    public void Random_node_imports_with_zero_train_links()
    {
        // The rail-free save is the no-false-positives gate: nothing in it (particularly not
        // mPathNetworkID, which belongs to wheeled-vehicle path nodes) may read as a train.
        Assert.DoesNotContain(RandomNode.VehicleRoutes, r => r.Kind == LogisticsKind.Train);

        var (_, connections) = SaveImport.Build(RandomNode, TestData.Database);
        Assert.DoesNotContain(connections, c => c.Logistics == LogisticsKind.Train);
        Assert.Contains(connections, c => c.Logistics == LogisticsKind.Truck);
    }
}
