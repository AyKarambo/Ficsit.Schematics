using System.Text;
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

    // ------------------------------------------------------------- body builder

    /// <summary>A minimal persistent-level TOC+Data pair the blob locator accepts: ≥1000 objects,
    /// ≥100 KB of TOC, matching object counts, and a built machine in the TOC.</summary>
    private static byte[] BuildBody(float clock)
    {
        const int fillers = 1200;
        var spans = new List<byte[]>();

        using var tocMs = new MemoryStream();
        using var toc = new BinaryWriter(tocMs);
        toc.Write(fillers + 4);

        WriteActorToc(toc,
            "/Game/FactoryGame/Buildable/Factory/ConstructorMk1/Build_ConstructorMk1.Build_ConstructorMk1_C",
            Constructor);
        spans.Add(FloatProperty("mCurrentPotential", clock));

        WriteObjectToc(toc, "/Script/FactoryGame.FGInventoryComponent",
            Constructor + ".InventoryPotential", Constructor);
        spans.Add([.. SloopStack(2), .. SloopStack(1), .. SloopStack(0)]);

        WriteActorToc(toc,
            "/Game/FactoryGame/Buildable/Factory/SmelterMk1/Build_SmelterMk1.Build_SmelterMk1_C",
            Smelter);
        spans.Add([]);

        WriteObjectToc(toc, "/Script/FactoryGame.FGFactoryConnectionComponent",
            Constructor + ".Output0", Constructor);
        spans.Add(LinkProperty(Smelter + ".Input0"));

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
        data.Write(fillers + 4);
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
}
