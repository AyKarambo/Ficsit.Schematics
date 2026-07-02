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

        var smelter = Assert.Single(world.Buildings, b => b.ClassName == "Build_SmelterMk1_C");
        Assert.Equal(Rational.One, smelter.ClockSpeed); // the 100% default isn't serialized
        Assert.Equal(0, smelter.Somersloops);

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

    // ------------------------------------------------------------- body builder

    private static byte[] BuildBody(float clock) => AssembleBody(
        (w => WriteActorToc(w,
            "/Game/FactoryGame/Buildable/Factory/ConstructorMk1/Build_ConstructorMk1.Build_ConstructorMk1_C",
            Constructor), FloatProperty("mCurrentPotential", clock)),
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

    private static void WriteActorToc(BinaryWriter w, string cls, string instance)
    {
        w.Write(1); // actor entry
        WriteFString(w, cls);
        WriteFString(w, "Persistent_Level");
        WriteFString(w, instance);
        w.Write(0);                                          // flags
        w.Write(0);                                          // needTransform
        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f);  // rotation quaternion
        w.Write(10_000f); w.Write(20_000f); w.Write(300f);   // position (cm)
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
