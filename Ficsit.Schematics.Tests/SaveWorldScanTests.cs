using System.Text;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// Synthetic-body tests for the persistent-level object-data scan: per-building clock
/// (<c>mCurrentPotential</c>), Somersloops (potential-inventory items), and component links.
/// The blob framing written here mirrors what the reader is verified against on real saves.
/// </summary>
public class SaveWorldScanTests
{
    private const string Constructor = "Persistent_Level:PersistentLevel.Build_ConstructorMk1_C_1";
    private const string Smelter = "Persistent_Level:PersistentLevel.Build_SmelterMk1_C_2";

    [Fact]
    public void Reads_clock_sloops_and_links_from_a_synthetic_body()
    {
        var world = SatisfactorySaveReader.ReadWorldFromBody(BuildBody(clock: 0.5f));

        Assert.Equal(2, world.Buildings.Count);
        var constructor = Assert.Single(world.Buildings, b => b.ClassName == "Build_ConstructorMk1_C");
        Assert.Equal(new Rational(1, 2), constructor.ClockSpeed);
        Assert.Equal(3, constructor.Somersloops); // 2 + 1; the vacated 0-item stack adds nothing
        Assert.Equal("IronPlate", constructor.RecipeStem); // its own mCurrentRecipe, per actor

        var smelter = Assert.Single(world.Buildings, b => b.ClassName == "Build_SmelterMk1_C");
        Assert.Equal(Rational.One, smelter.ClockSpeed); // the 100% default isn't serialized
        Assert.Equal(0, smelter.Somersloops);
        Assert.Null(smelter.RecipeStem); // no recipe set

        Assert.Equal(Smelter + ".Input0", world.ComponentLinks[Constructor + ".Output0"]);
    }

    [Fact]
    public void Clock_floats_read_back_at_the_games_percent_precision()
    {
        // 133.3333% is stored as the nearest float; it must come back as the exact fraction.
        var world = SatisfactorySaveReader.ReadWorldFromBody(BuildBody(clock: 1.333333f));

        var constructor = Assert.Single(world.Buildings, b => b.ClassName == "Build_ConstructorMk1_C");
        Assert.Equal(new Rational(1_333_333, 1_000_000), constructor.ClockSpeed);
    }

    [Fact]
    public void Reads_truck_networks_and_drone_pairs_into_vehicle_routes()
    {
        const string stationA = "Persistent_Level:PersistentLevel.Build_TruckStation_C_10";
        const string stationB = "Persistent_Level:PersistentLevel.Build_TruckStation_C_20";
        const string dockA = "Persistent_Level:PersistentLevel.Build_VehiclePathNode_DockingStation_C_11";
        const string dockB = "Persistent_Level:PersistentLevel.Build_VehiclePathNode_DockingStation_C_21";
        const string droneA = "Persistent_Level:PersistentLevel.Build_DroneStation_C_30";
        const string droneB = "Persistent_Level:PersistentLevel.Build_DroneStation_C_31";

        var world = SatisfactorySaveReader.ReadWorldFromBody(AssembleBody(
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Factory/TruckStation/Build_TruckStation.Build_TruckStation_C",
                stationA), []),
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Factory/TruckStation/Build_TruckStation.Build_TruckStation_C",
                stationB), []),
            // Each station's docking path node: parent ref = the station, then the road-network id.
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Vehicle/VehiclePath/Build_VehiclePathNode_DockingStation.Build_VehiclePathNode_DockingStation_C",
                dockA), DockingNodeSpan(stationA, networkId: 7)),
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Vehicle/VehiclePath/Build_VehiclePathNode_DockingStation.Build_VehiclePathNode_DockingStation_C",
                dockB), DockingNodeSpan(stationB, networkId: 7)),
            // Drone ports pair mutually; the pair must come out as one route.
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Factory/DroneStation/Build_DroneStation.Build_DroneStation_C",
                droneA), PairedStationSpan(droneB)),
            (w => WriteActorToc(w,
                "/Game/FactoryGame/Buildable/Factory/DroneStation/Build_DroneStation.Build_DroneStation_C",
                droneB), PairedStationSpan(droneA))));

        var truck = Assert.Single(world.VehicleRoutes, r => r.Kind == LogisticsKind.Truck);
        Assert.Equal([stationA, stationB], truck.Stations.OrderBy(s => s, StringComparer.Ordinal));
        var drone = Assert.Single(world.VehicleRoutes, r => r.Kind == LogisticsKind.Drone);
        Assert.Equal([droneA, droneB], drone.Stations.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void Scans_timetables_identifiers_and_platform_connections()
    {
        const string stationA = "Persistent_Level:PersistentLevel.Build_TrainStation_C_100";
        const string stationB = "Persistent_Level:PersistentLevel.Build_TrainStation_C_200";
        const string dockA = "Persistent_Level:PersistentLevel.Build_TrainDockingStation_C_110";
        const string idA = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_101";
        const string idB = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_201";
        const string timetable = "Persistent_Level:PersistentLevel.FGRailroadTimeTable_300";
        const string decoy = "Fake_Level:PersistentLevel.Build_TrainStation_C_999";

        var scan = SatisfactorySaveReader.ScanObjectDataFromBody(AssembleBody(
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGRailroadTimeTable", timetable,
                "Persistent_Level:PersistentLevel"),
                [.. ObjectRefProperty("Station", idA), .. ObjectRefProperty("Station", idB)]),
            // The identifier carries mStationName too; a decoy *before* mStation proves the scan
            // matches the exact property name (mStation) rather than the mStationName prefix.
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", idA,
                "Persistent_Level:PersistentLevel"),
                [.. ObjectRefProperty("mStationName", decoy), .. ObjectRefProperty("mStation", stationA)]),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", idB,
                "Persistent_Level:PersistentLevel"),
                ObjectRefProperty("mStation", stationB)),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
                stationA + ".PlatformConnection0", stationA),
                ObjectRefProperty("mConnectedTo", dockA + ".PlatformConnection0")),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
                dockA + ".PlatformConnection0", dockA),
                ObjectRefProperty("mConnectedTo", stationA + ".PlatformConnection0"))));

        Assert.Equal([idA, idB], scan.TimetableStops[timetable]);
        Assert.Equal(stationA, scan.StationOfIdentifier[idA]); // not the decoy
        Assert.Equal(stationB, scan.StationOfIdentifier[idB]);
        Assert.Equal(dockA + ".PlatformConnection0", scan.PlatformLinks[stationA + ".PlatformConnection0"]);
        Assert.Equal(stationA + ".PlatformConnection0", scan.PlatformLinks[dockA + ".PlatformConnection0"]);
    }

    private const string TrainStationCls =
        "/Game/FactoryGame/Buildable/Factory/Train/Station/Build_TrainStation.Build_TrainStation_C";
    private const string DockCls =
        "/Game/FactoryGame/Buildable/Factory/Train/Station/Build_TrainDockingStation.Build_TrainDockingStation_C";
    private const string LiquidDockCls =
        "/Game/FactoryGame/Buildable/Factory/Train/StationLiquid/Build_TrainDockingStationLiquid.Build_TrainDockingStationLiquid_C";
    private const string StationA = "Persistent_Level:PersistentLevel.Build_TrainStation_C_100";
    private const string StationB = "Persistent_Level:PersistentLevel.Build_TrainStation_C_200";
    private const string DockA = "Persistent_Level:PersistentLevel.Build_TrainDockingStation_C_110";
    private const string DockB = "Persistent_Level:PersistentLevel.Build_TrainDockingStationLiquid_C_210";
    private const string IdA = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_101";
    private const string IdB = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_201";

    [Fact]
    public void Reads_a_train_timetable_into_a_route_of_freight_platforms()
    {
        var world = SatisfactorySaveReader.ReadWorldFromBody(TrainBody(stops: [IdA, IdB]));

        var route = Assert.Single(world.VehicleRoutes, r => r.Kind == LogisticsKind.Train);
        // The route's stations are the *freight platforms* in stop order — cargo flows through
        // them, not the station buildings; the liquid variant participates symmetrically.
        Assert.Equal([DockA, DockB], route.Stations);
    }

    [Fact]
    public void A_dangling_timetable_stop_is_skipped_and_the_route_still_forms()
    {
        const string ghost = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_999";
        var world = SatisfactorySaveReader.ReadWorldFromBody(TrainBody(stops: [IdA, ghost, IdB]));

        var route = Assert.Single(world.VehicleRoutes, r => r.Kind == LogisticsKind.Train);
        Assert.Equal([DockA, DockB], route.Stations);
    }

    [Fact]
    public void A_single_resolvable_station_yields_no_train_route()
    {
        const string ghost = "Persistent_Level:PersistentLevel.FGTrainStationIdentifier_999";
        var world = SatisfactorySaveReader.ReadWorldFromBody(TrainBody(stops: [IdA, ghost]));

        Assert.DoesNotContain(world.VehicleRoutes, r => r.Kind == LogisticsKind.Train);
    }

    [Fact]
    public void Chainless_platforms_fall_back_to_the_nearest_station_within_100m()
    {
        // No PlatformConnection objects at all — assignment must come from world positions:
        // each dock sits a few meters from its station, the stations a kilometer apart.
        var world = SatisfactorySaveReader.ReadWorldFromBody(AssembleBody(
            (w => WriteActorToc(w, TrainStationCls, StationA, x: 0f, y: 0f), []),
            (w => WriteActorToc(w, TrainStationCls, StationB, x: 100_000f, y: 0f), []),
            (w => WriteActorToc(w, DockCls, DockA, x: 2_000f, y: 0f), []),
            (w => WriteActorToc(w, LiquidDockCls, DockB, x: 98_000f, y: 0f), []),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", IdA,
                "Persistent_Level:PersistentLevel"), ObjectRefProperty("mStation", StationA)),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", IdB,
                "Persistent_Level:PersistentLevel"), ObjectRefProperty("mStation", StationB)),
            (w => WriteObjectToc(w, "/Script/FactoryGame.FGRailroadTimeTable",
                "Persistent_Level:PersistentLevel.FGRailroadTimeTable_300",
                "Persistent_Level:PersistentLevel"),
                [.. ObjectRefProperty("Station", IdA), .. ObjectRefProperty("Station", IdB)])));

        var route = Assert.Single(world.VehicleRoutes, r => r.Kind == LogisticsKind.Train);
        Assert.Equal([DockA, DockB], route.Stations);
    }

    /// <summary>A two-station train world: stations with one freight platform each (chained via
    /// PlatformConnections), their identifiers, and one timetable visiting <paramref name="stops"/>.</summary>
    private static byte[] TrainBody(string[] stops) => AssembleBody(
        (w => WriteActorToc(w, TrainStationCls, StationA), []),
        (w => WriteActorToc(w, TrainStationCls, StationB), []),
        (w => WriteActorToc(w, DockCls, DockA), []),
        (w => WriteActorToc(w, LiquidDockCls, DockB), []),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
            StationA + ".PlatformConnection0", StationA),
            ObjectRefProperty("mConnectedTo", DockA + ".PlatformConnection0")),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
            DockA + ".PlatformConnection0", DockA),
            ObjectRefProperty("mConnectedTo", StationA + ".PlatformConnection0")),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
            StationB + ".PlatformConnection0", StationB),
            ObjectRefProperty("mConnectedTo", DockB + ".PlatformConnection0")),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainPlatformConnection",
            DockB + ".PlatformConnection0", DockB),
            ObjectRefProperty("mConnectedTo", StationB + ".PlatformConnection0")),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", IdA,
            "Persistent_Level:PersistentLevel"), ObjectRefProperty("mStation", StationA)),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGTrainStationIdentifier", IdB,
            "Persistent_Level:PersistentLevel"), ObjectRefProperty("mStation", StationB)),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGRailroadTimeTable",
            "Persistent_Level:PersistentLevel.FGRailroadTimeTable_300",
            "Persistent_Level:PersistentLevel"),
            stops.SelectMany(s => ObjectRefProperty("Station", s)).ToArray()));

    // ------------------------------------------------------------- body builder

    private static byte[] BuildBody(float clock) => AssembleBody(
        (w => WriteActorToc(w,
            "/Game/FactoryGame/Buildable/Factory/ConstructorMk1/Build_ConstructorMk1.Build_ConstructorMk1_C",
            Constructor),
            [.. FloatProperty("mCurrentPotential", clock), .. RecipeProperty("IronPlate")]),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGInventoryComponent",
            Constructor + ".InventoryPotential", Constructor),
            [.. SloopStack(2), .. SloopStack(1), .. SloopStack(0)]),
        (w => WriteActorToc(w,
            "/Game/FactoryGame/Buildable/Factory/SmelterMk1/Build_SmelterMk1.Build_SmelterMk1_C",
            Smelter), []),
        (w => WriteObjectToc(w, "/Script/FactoryGame.FGFactoryConnectionComponent",
            Constructor + ".Output0", Constructor), LinkProperty(Smelter + ".Input0")));

    /// <summary>A minimal persistent-level TOC+Data pair the blob locator accepts: ≥1000 objects,
    /// ≥100 KB of TOC, matching object counts, and a built machine in the TOC.</summary>
    private static byte[] AssembleBody(params (Action<BinaryWriter> WriteToc, byte[] Span)[] objects)
    {
        const int fillers = 1200;
        var spans = new List<byte[]>();

        using var tocMs = new MemoryStream();
        using var toc = new BinaryWriter(tocMs);
        toc.Write(fillers + objects.Length);

        foreach (var (writeToc, span) in objects)
        {
            writeToc(toc);
            spans.Add(span);
        }
        for (var i = 0; i < fillers; i++)
        {
            WriteObjectToc(toc, "/Script/FactoryGame.PaddingObjectClassKeepsTocAboveTheMinimum",
                $"Persistent_Level:PersistentLevel.Filler_Object_Number_{i:D5}",
                "Persistent_Level:PersistentLevel");
            spans.Add([]);
        }
        toc.Flush();
        var tocBytes = tocMs.ToArray();

        using var dataMs = new MemoryStream();
        using var data = new BinaryWriter(dataMs);
        data.Write(spans.Count);
        foreach (var span in spans)
        {
            data.Write(46); // per-object save version (< 53 ⇒ no version tail)
            data.Write(0);  // shouldMigrate
            data.Write(span.Length);
            data.Write(span);
        }
        data.Flush();
        var dataBytes = dataMs.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((long)tocBytes.Length);
        w.Write(tocBytes);
        w.Write((long)dataBytes.Length);
        w.Write(dataBytes);
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteFString(BinaryWriter w, string s)
    {
        w.Write(s.Length + 1);
        w.Write(Encoding.ASCII.GetBytes(s));
        w.Write((byte)0);
    }

    private static void WriteActorToc(
        BinaryWriter w, string cls, string instance, float x = 10_000f, float y = 20_000f)
    {
        w.Write(1); // actor entry
        WriteFString(w, cls);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, instance);
        w.Write(0);                                          // flags
        w.Write(0);                                          // needTransform
        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f);  // rotation quaternion
        w.Write(x); w.Write(y); w.Write(300f);               // position (cm)
        w.Write(1f); w.Write(1f); w.Write(1f);               // scale
        w.Write(1);                                          // wasPlaced
    }

    private static void WriteObjectToc(BinaryWriter w, string cls, string instance, string outer)
    {
        w.Write(0); // non-actor entry
        WriteFString(w, cls);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, instance);
        w.Write(0); // flags
        WriteFString(w, outer);
    }

    /// <summary>name FString, "FloatProperty" FString, index/size int32 pair, pad byte, value —
    /// the on-disk framing observed in real saves.</summary>
    private static byte[] FloatProperty(string name, float value)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, name);
        WriteFString(w, "FloatProperty");
        w.Write(0); w.Write(4); w.Write((byte)0);
        w.Write(value);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>An mCurrentRecipe ObjectProperty pointing at the recipe asset.</summary>
    private static byte[] RecipeProperty(string stem)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "mCurrentRecipe");
        WriteFString(w, "ObjectProperty");
        w.Write(0); w.Write(90); w.Write((byte)0);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, $"/Game/FactoryGame/Recipes/Constructor/Recipe_{stem}.Recipe_{stem}_C");
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>One Somersloop inventory stack: item class path, then its NumItems int.</summary>
    private static byte[] SloopStack(int numItems)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "/Game/FactoryGame/Prototype/WAT/Desc_WAT1.Desc_WAT1_C");
        WriteFString(w, "NumItems");
        WriteFString(w, "IntProperty");
        w.Write(4); w.Write(0); w.Write((byte)0);
        w.Write(numItems);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>An ObjectProperty reference: property name, type, size/index pair, pad byte,
    /// level name, then the target instance path — the framing every ref scan matches.</summary>
    private static byte[] ObjectRefProperty(string name, string target)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, name);
        WriteFString(w, "ObjectProperty");
        w.Write(0); w.Write(38); w.Write((byte)0);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, target);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] LinkProperty(string target)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "mConnectedComponent");
        WriteFString(w, "ObjectProperty");
        w.Write(0); w.Write(38); w.Write((byte)0);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, target);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A docking path node's data: the parent-actor ref (its station) followed by the
    /// road-network id property.</summary>
    private static byte[] DockingNodeSpan(string station, int networkId)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, station);
        WriteFString(w, "mPathNetworkID");
        WriteFString(w, "IntProperty");
        w.Write(4); w.Write(0); w.Write((byte)0);
        w.Write(networkId);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] PairedStationSpan(string target)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "mPairedStation");
        WriteFString(w, "ObjectProperty");
        w.Write(0); w.Write(38); w.Write((byte)0);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, target);
        w.Flush();
        return ms.ToArray();
    }
}
