using System.Text;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveReaderTests
{
    [Fact]
    public void Building_scan_reads_class_and_transform_from_an_actor_header()
    {
        // A minimal, correctly-framed actor header for a Build_* machine. The scan must read its
        // short class and world position, and ignore the instance string's own "Build_" (it ends
        // in digits, not "_C").
        var body = BuildActorHeaderBody(
            "/Game/FactoryGame/Buildable/Factory/ConstructorMk1/Build_ConstructorMk1.Build_ConstructorMk1_C",
            "Persistent_Level:PersistentLevel.Build_ConstructorMk1_C_2147480001",
            x: 12345.5f, y: -6789.25f, z: 100f);

        var buildings = SatisfactorySaveReader.ReadBuildingsFromBody(body);

        var b = Assert.Single(buildings);
        Assert.Equal("Build_ConstructorMk1_C", b.ClassName);
        Assert.Equal(12345.5, b.X, 3);
        Assert.Equal(-6789.25, b.Y, 3);
        Assert.Equal(100, b.Z, 3);
        Assert.EndsWith("2147480001", b.Instance);
    }

    /// <summary>Lay out a save-TOC actor header the way the reader scans it:
    /// int32 type(1), fstring class, fstring level/instance, 8 bytes, quaternion, position.</summary>
    private static byte[] BuildActorHeaderBody(string classPath, string instance, float x, float y, float z)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        var classBytes = Encoding.ASCII.GetBytes(classPath);
        w.Write(1);                       // entry type: actor (start - 8)
        w.Write(classBytes.Length + 1);   // class fstring length prefix (start - 4)
        w.Write(classBytes);              // class path (start)
        w.Write((byte)0);                 // null terminator
        WriteFString(w, "Persistent_Level");
        WriteFString(w, instance);
        w.Write(new byte[8]);             // unknown int32 pair
        w.Write(new byte[16]);            // quaternion (rotation)
        w.Write(x); w.Write(y); w.Write(z);
        return ms.ToArray();

        static void WriteFString(BinaryWriter w, string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            w.Write(bytes.Length + 1);
            w.Write(bytes);
            w.Write((byte)0);
        }
    }

    [Fact]
    public void Recipe_stem_scan_reads_mCurrentRecipe_values_in_order()
    {
        var body = ConcatBytes(
            RecipeProperty("/Game/FactoryGame/Recipes/.../Recipe_PackagedWater.Recipe_PackagedWater_C"),
            RecipeProperty("/Game/FactoryGame/Recipes/.../Recipe_Alternate_BoltedFrame.Recipe_Alternate_BoltedFrame_C"));

        var stems = SatisfactorySaveReader.ScanRecipeStems(body);

        Assert.Equal(new[] { "PackagedWater", "Alternate_BoltedFrame" }, stems);
    }

    /// <summary>An mCurrentRecipe ObjectProperty as the scanner frames it: the length-prefixed,
    /// null-terminated property name, then (further along) the recipe asset path.</summary>
    private static byte[] RecipeProperty(string recipePath)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        var name = Encoding.ASCII.GetBytes("mCurrentRecipe");
        w.Write(name.Length + 1); // FString length prefix (read at name start - 4)
        w.Write(name);
        w.Write((byte)0);         // null terminator
        w.Write(new byte[24]);    // property type/size/index padding before the value
        var path = Encoding.ASCII.GetBytes(recipePath);
        w.Write(path.Length + 1);
        w.Write(path);
        w.Write((byte)0);
        return ms.ToArray();
    }

    private static byte[] ConcatBytes(params byte[][] parts)
    {
        using var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p, 0, p.Length);
        return ms.ToArray();
    }

    [Fact]
    public void Reads_built_world_from_a_real_save()
    {
        var save = NewestLocalSave();
        if (save is null) return; // no game installed here — nothing to verify

        var world = SatisfactorySaveReader.ReadWorld(save);

        Assert.NotEmpty(world.ResourceNodes);
        // A played save has built machines; their positions must be on the map.
        Assert.All(world.Buildings, b =>
        {
            Assert.StartsWith("Build_", b.ClassName);
            Assert.EndsWith("_C", b.ClassName);
            Assert.InRange(b.X, -1_000_000, 1_000_000);
            Assert.InRange(b.Y, -1_000_000, 1_000_000);
        });
    }

    /// <summary>Newest local Satisfactory save, when the game is installed on this machine.</summary>
    private static string? NewestLocalSave()
    {
        var saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactoryGame", "Saved", "SaveGames");
        if (!Directory.Exists(saveDir)) return null;
        return Directory.EnumerateFiles(saveDir, "*.sav", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 100_000) // skip manager/settings stubs
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    [Fact]
    public void Reads_resource_nodes_from_a_real_save()
    {
        var save = NewestLocalSave();
        if (save is null) return; // no game installed here — nothing to verify

        var nodes = SatisfactorySaveReader.ReadResourceNodes(save);

        Assert.True(nodes.Count > 100, $"Expected hundreds of nodes, got {nodes.Count}.");

        // Positions must be on the map (world is roughly ±5km in cm).
        Assert.All(nodes, n =>
        {
            Assert.InRange(n.X, -600_000, 600_000);
            Assert.InRange(n.Y, -600_000, 600_000);
        });

        // Order-correlation sanity: wells carry well fluids, plain nodes never do well-only gases.
        var wellParts = new[] { "Water", "Crude Oil", "Nitrogen Gas" };
        var satellites = nodes.Where(n => n.Kind == ResourceNodeKind.FrackingSatellite).ToList();
        if (satellites.Count > 0)
            Assert.All(satellites, s => Assert.Contains(s.Part, wellParts));
        var plainNodes = nodes.Where(n => n.Kind == ResourceNodeKind.Node).ToList();
        Assert.All(plainNodes, n => Assert.NotEqual("Nitrogen Gas", n.Part));
        Assert.All(plainNodes, n => Assert.NotEqual("Water", n.Part));

        // No unresolved resources when correlation holds, and purities are sane.
        Assert.DoesNotContain(nodes, n => n.Part == "Unknown");
        Assert.All(nodes, n => Assert.Contains(n.Purity, new[] { "Impure", "Normal", "Pure" }));

        // Geysers exist and are typed as such.
        Assert.Contains(nodes, n => n.Kind == ResourceNodeKind.Geyser && n.Part == "Geyser");
    }
}
