using System.IO.Compression;
using System.Text;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Extracts resource nodes (ore nodes, geysers, resource wells) from a
/// Satisfactory <c>.sav</c> file — including randomized-node game modes, where
/// nodes are spawned actors carrying <c>mResourceClassOverride</c> /
/// <c>mPurityOverride</c> properties.
///
/// The reader is deliberately surgical: it decompresses the save body (zlib
/// chunks) and scans for the actor headers and override properties it needs,
/// instead of parsing the full ever-shifting save schema. Positions come from
/// actor headers; resource type and purity are correlated by serialization
/// order (object data blocks follow header order), which is validated against
/// the entry counts. Verified against save version 60 (Satisfactory 1.1).
/// </summary>
public static class SatisfactorySaveReader
{
    private const uint ChunkMagic = 0x9E2A83C1;

    private const string NodeClass = "/Game/FactoryGame/Resource/BP_ResourceNode.BP_ResourceNode_C";
    private const string GeyserClass = "/Game/FactoryGame/Resource/BP_ResourceNodeGeyser.BP_ResourceNodeGeyser_C";
    private const string CoreClass = "/Game/FactoryGame/Resource/BP_FrackingCore.BP_FrackingCore_C";
    private const string SatelliteClass = "/Game/FactoryGame/Resource/BP_FrackingSatellite.BP_FrackingSatellite_C";

    private static readonly Dictionary<string, string> DescToPart = new()
    {
        ["Desc_OreIron_C"] = "Iron Ore",
        ["Desc_OreCopper_C"] = "Copper Ore",
        ["Desc_Stone_C"] = "Limestone",
        ["Desc_Coal_C"] = "Coal",
        ["Desc_OreGold_C"] = "Caterium Ore",
        ["Desc_OreBauxite_C"] = "Bauxite",
        ["Desc_OreUranium_C"] = "Uranium",
        ["Desc_RawQuartz_C"] = "Raw Quartz",
        ["Desc_Sulfur_C"] = "Sulfur",
        ["Desc_SAM_C"] = "SAM",
        ["Desc_LiquidOil_C"] = "Crude Oil",
        ["Desc_Water_C"] = "Water",
        ["Desc_NitrogenGas_C"] = "Nitrogen Gas",
    };

    public static IReadOnlyList<ResourceNodeInfo> ReadResourceNodes(string filePath)
        => ReadResourceNodes(File.ReadAllBytes(filePath));

    // ------------------------------------------------- unlocked schematics

    private const string PurchasedSchematicsProperty = "mPurchasedSchematics";
    private const string AlternatePrefix = "Alternate_";

    public static IReadOnlyList<string> ReadUnlockedSchematics(string filePath)
        => ReadUnlockedSchematics(File.ReadAllBytes(filePath));

    /// <summary>
    /// The stem of every unlocked schematic in the save's <c>mPurchasedSchematics</c> array
    /// (the part after <c>Schematic_</c>): milestones ("3-2"), the onboarding "StartingRecipes",
    /// tutorials, MAM research, and hard-drive alternates ("Alternate_BoltedFrame"). Reuses the
    /// resource-node reader's chunk inflation; the array is scanned by asset path rather than
    /// parsed by struct offset, which is robust across save versions (see from-save-spike.md).
    /// </summary>
    public static IReadOnlyList<string> ReadUnlockedSchematics(byte[] saveFile)
        => ReadAllSchematicStemsFromBody(DecompressBody(saveFile));

    public static IReadOnlyList<string> ReadUnlockedAlternateSchematics(string filePath)
        => ReadUnlockedAlternateSchematics(File.ReadAllBytes(filePath));

    /// <summary>Just the unlocked hard-drive alternates (stem with the "Alternate_" prefix dropped).</summary>
    public static IReadOnlyList<string> ReadUnlockedAlternateSchematics(byte[] saveFile)
        => OnlyAlternates(ReadUnlockedSchematics(saveFile));

    /// <summary>Stem of every <c>Schematic_*</c> entry in the purchased array. Public so it can be
    /// unit-tested against a synthetic body without a real <c>.sav</c> fixture.</summary>
    public static IReadOnlyList<string> ReadAllSchematicStemsFromBody(byte[] body)
    {
        var stems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var marker = Encoding.ASCII.GetBytes(PurchasedSchematicsProperty);
        foreach (var at in FindAll(body, marker))
            // Walk the array from every occurrence and union. The real array yields all of
            // them; an incidental reference elsewhere yields ~nothing (the walk's gap check
            // stops it quickly). No structural validation — the walk itself is the filter, so
            // we're robust to FString/FName framing differences.
            CollectSchematicStems(body, at, stems, seen);
        return stems;
    }

    /// <summary>Back-compat: the alternate stems only (used by the alternate-specific tests).</summary>
    public static IReadOnlyList<string> ReadAlternateStemsFromBody(byte[] body)
        => OnlyAlternates(ReadAllSchematicStemsFromBody(body));

    private static IReadOnlyList<string> OnlyAlternates(IReadOnlyList<string> schematicStems)
    {
        var alternates = new List<string>();
        foreach (var stem in schematicStems)
            if (stem.StartsWith(AlternatePrefix, StringComparison.Ordinal))
                alternates.Add(stem[AlternatePrefix.Length..]);
        return alternates;
    }

    /// <summary>Walks the contiguous purchased-schematics array by each entry's asset path
    /// (every entry starts <c>/Game/FactoryGame/Schematics/</c> — milestones, tutorials, AWESOME
    /// shop customizers and hard-drive alternates alike), collecting the stem after
    /// <c>Schematic_</c>. Walking the path (not the class name) is what keeps the scan going
    /// across the long runs of non-"Schematic_"-named customizer entries interleaved in the
    /// array; the first non-schematic <c>/Game</c> ref marks the array end.</summary>
    private static void CollectSchematicStems(byte[] body, int from, List<string> stems, HashSet<string> seen)
    {
        const string schematicsRoot = "/Game/FactoryGame/Schematics/";
        const string schematic = "Schematic_";
        var token = Encoding.ASCII.GetBytes("/Game/");
        var hardEnd = Math.Min(body.Length, from + 4 * 1024 * 1024);
        var pos = from;
        var lastHit = from;
        while (pos < hardEnd)
        {
            var idx = IndexOf(body, token, pos, hardEnd);
            if (idx < 0 || idx - lastHit > 8192) break; // a big gap = the array ended

            var end = idx;
            while (end < body.Length && IsPathByte(body[end])) end++;
            var path = Encoding.ASCII.GetString(body, idx, end - idx);
            pos = end;

            // The purchased array holds only schematic object refs; the first /Game ref that
            // isn't a schematic path marks the boundary.
            if (!path.StartsWith(schematicsRoot, StringComparison.Ordinal)) break;
            lastHit = idx;

            var s = path.IndexOf(schematic, StringComparison.Ordinal);
            if (s < 0) continue; // AWESOME-shop customizer (CBG_*) etc. — not a recipe unlock
            var stemStart = s + schematic.Length;
            var dot = path.IndexOf('.', stemStart);
            var stem = (dot >= 0 ? path[stemStart..dot] : path[stemStart..]);
            if (stem.EndsWith("_C", StringComparison.Ordinal)) stem = stem[..^2];
            if (stem.Length > 0 && seen.Add(stem)) stems.Add(stem);
        }
    }

    private static bool IsPathByte(byte b)
        => b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9' or (byte)'/' or (byte)'_' or (byte)'-' or (byte)'.';

    private static int IndexOf(byte[] haystack, byte[] needle, int start, int end)
    {
        var limit = Math.Min(end, haystack.Length) - needle.Length;
        for (var i = start; i <= limit; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    public static IReadOnlyList<ResourceNodeInfo> ReadResourceNodes(byte[] saveFile)
        => ReadResourceNodesFromBody(DecompressBody(saveFile));

    // -------------------------------------------------------- built world

    /// <summary>
    /// Read the built world (Phase 0 of "import built factories"): every <c>Build_*</c> machine
    /// actor with its class and world transform, plus the resource nodes (so extractors can snap
    /// to the node they sit on). The body is inflated once and scanned for both.
    /// </summary>
    public static SaveWorld ReadWorld(string filePath) => ReadWorld(File.ReadAllBytes(filePath));

    public static SaveWorld ReadWorld(byte[] saveFile)
    {
        var body = DecompressBody(saveFile);
        return new SaveWorld
        {
            Buildings = ReadBuildingsFromBody(body),
            ResourceNodes = ReadResourceNodesFromBody(body),
            RecipeStems = ScanRecipeStems(body),
        };
    }

    /// <summary>
    /// Every <c>mCurrentRecipe</c> value in the inflated body, as a recipe-class stem
    /// ("PackagedWater", "Alternate_BoltedFrame"), in serialization order — which mirrors the
    /// actor-header order, so <see cref="SaveImport"/> can line up the k-th machine of a type with
    /// the k-th recipe of that type. Same property-name framing the resource-override scan uses.
    /// Public for unit testing against a synthetic body.
    /// </summary>
    public static IReadOnlyList<string> ScanRecipeStems(byte[] body)
    {
        var name = Encoding.ASCII.GetBytes("mCurrentRecipe");
        var stems = new List<string>();
        foreach (var at in FindAll(body, name))
        {
            if (at < 4) continue;
            if (BitConverter.ToInt32(body, at - 4) != name.Length + 1) continue; // FString length prefix
            if (at + name.Length >= body.Length || body[at + name.Length] != 0) continue; // exact name
            if (RecipeStemAfter(body, at + name.Length, 320) is { } stem) stems.Add(stem);
        }
        return stems;
    }

    /// <summary>The recipe stem from the first <c>Recipe_*</c> asset path within a window: the text
    /// after "Recipe_" up to the next '.' (so "…/Recipe_PackagedWater.Recipe_PackagedWater_C" →
    /// "PackagedWater").</summary>
    private static string? RecipeStemAfter(byte[] body, int from, int window)
    {
        const string token = "Recipe_";
        var text = Encoding.ASCII.GetString(body, from, Math.Min(window, body.Length - from));
        var start = text.IndexOf(token, StringComparison.Ordinal);
        if (start < 0) return null;
        start += token.Length;
        var end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        var stem = text[start..end];
        if (stem.EndsWith("_C", StringComparison.Ordinal)) stem = stem[..^2];
        return stem.Length > 0 ? stem : null;
    }

    public static IReadOnlyList<SaveBuilding> ReadBuildings(string filePath)
        => ReadBuildingsFromBody(DecompressBody(File.ReadAllBytes(filePath)));

    /// <summary>
    /// Every machine actor in the inflated body. Detects an actor header whose class is a
    /// <c>Build_*_C</c> path generically — same header framing the resource-node scan is verified
    /// against (entry type 1, length-prefixed null-terminated class FString, then level / instance
    /// / quaternion / position) — so it needs no per-machine class table. Public for unit testing
    /// against a synthetic body. Recipe/clock/shard enrichment is deferred (see SaveBuilding).
    /// </summary>
    public static IReadOnlyList<SaveBuilding> ReadBuildingsFromBody(byte[] body)
    {
        var buildings = new List<SaveBuilding>();
        var seenStarts = new HashSet<int>();
        var marker = Encoding.ASCII.GetBytes("Build_");
        foreach (var at in FindAll(body, marker))
        {
            // Walk back to the class FString start ("/Game/...Build_X.Build_X_C").
            var start = at;
            while (start > 0 && IsPathByte(body[start - 1])) start--;
            if (start < 8 || !seenStarts.Add(start)) continue;

            var end = start;
            while (end < body.Length && IsPathByte(body[end])) end++;
            if (end >= body.Length || body[end] != 0) continue;             // FString is null-terminated
            var pathLen = end - start;
            if (BitConverter.ToInt32(body, start - 4) != pathLen + 1) continue; // FString length prefix
            if (BitConverter.ToInt32(body, start - 8) != 1) continue;       // actor entry (type 1)

            var path = Encoding.ASCII.GetString(body, start, pathLen);
            var dot = path.LastIndexOf('.');
            var shortClass = dot >= 0 ? path[(dot + 1)..] : path;
            if (!shortClass.StartsWith("Build_", StringComparison.Ordinal)
                || !shortClass.EndsWith("_C", StringComparison.Ordinal)) continue;

            var p = start + pathLen + 1;
            if (!TryReadFString(body, ref p, out _)) continue;              // level
            if (!TryReadFString(body, ref p, out var instance)) continue;
            p += 8;                                                          // unknown int32 pair
            p += 16;                                                         // quaternion (rotation)
            if (p + 12 > body.Length) continue;
            var x = BitConverter.ToSingle(body, p);
            var y = BitConverter.ToSingle(body, p + 4);
            var z = BitConverter.ToSingle(body, p + 8);
            if (double.IsNaN(x) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7 || Math.Abs(z) > 1e6) continue;

            buildings.Add(new SaveBuilding { ClassName = shortClass, Instance = instance, X = x, Y = y, Z = z });
        }
        return buildings;
    }

    private static IReadOnlyList<ResourceNodeInfo> ReadResourceNodesFromBody(byte[] body)
    {
        var headers = new List<ActorHeader>();
        headers.AddRange(ScanActorHeaders(body, NodeClass, ResourceNodeKind.Node));
        headers.AddRange(ScanActorHeaders(body, GeyserClass, ResourceNodeKind.Geyser));
        headers.AddRange(ScanActorHeaders(body, CoreClass, ResourceNodeKind.FrackingCore));
        headers.AddRange(ScanActorHeaders(body, SatelliteClass, ResourceNodeKind.FrackingSatellite));
        headers.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var overrides = ScanResourceOverrides(body);

        // Object data blocks are serialized in header order; geysers carry no
        // resource override, everything else exactly one. Validate before zipping.
        var overridable = headers.Where(h => h.Kind != ResourceNodeKind.Geyser).ToList();
        var canCorrelate = overridable.Count == overrides.Count;

        var nodes = new List<ResourceNodeInfo>();
        var id = 1;
        var overrideIndex = 0;
        foreach (var header in headers)
        {
            var info = new ResourceNodeInfo
            {
                Id = id++,
                Instance = header.Instance,
                Kind = header.Kind,
                X = header.X,
                Y = header.Y,
                Z = header.Z,
            };
            if (header.Kind == ResourceNodeKind.Geyser)
            {
                info.Part = "Geyser";
            }
            else if (canCorrelate)
            {
                var entry = overrides[overrideIndex++];
                info.Part = DescToPart.GetValueOrDefault(entry.Desc, entry.Desc);
                if (entry.Purity is not null) info.Purity = entry.Purity;
            }
            else
            {
                info.Part = "Unknown";
            }
            nodes.Add(info);
        }
        return nodes;
    }

    // ------------------------------------------------------------- chunks

    /// <summary>Locates the first compressed chunk and inflates the whole body.</summary>
    private static byte[] DecompressBody(byte[] file)
    {
        var start = FindFirstChunk(file);
        if (start < 0)
            throw new InvalidDataException("Not a Satisfactory save: no compressed chunk found.");

        using var output = new MemoryStream();
        var pos = start;
        while (pos <= file.Length - 49)
        {
            if (BitConverter.ToUInt32(file, pos) != ChunkMagic) break;
            // magic(4) version(4) maxChunkSize(8) compressor(1) comp(8) uncomp(8) comp2(8) uncomp2(8)
            var compressedSize = (int)BitConverter.ToInt64(file, pos + 17);
            var dataStart = pos + 49;
            if (compressedSize <= 0 || dataStart + compressedSize > file.Length)
                throw new InvalidDataException("Corrupt save chunk.");

            using var segment = new MemoryStream(file, dataStart, compressedSize);
            using var inflate = new ZLibStream(segment, CompressionMode.Decompress);
            inflate.CopyTo(output);
            pos = dataStart + compressedSize;
        }
        return output.ToArray();
    }

    private static int FindFirstChunk(byte[] file)
    {
        // The header is version-dependent; scanning for the chunk magic is
        // stable across save versions. Magic 0x9E2A83C1 little-endian.
        for (var i = 12; i < Math.Min(file.Length - 49, 64 * 1024); i++)
        {
            if (file[i] == 0xC1 && file[i + 1] == 0x83 && file[i + 2] == 0x2A && file[i + 3] == 0x9E
                && BitConverter.ToInt64(file, i + 8) == 131072)
                return i;
        }
        return -1;
    }

    // ------------------------------------------------------ actor headers

    private sealed record ActorHeader(int Offset, ResourceNodeKind Kind, string Instance, double X, double Y, double Z);

    /// <summary>
    /// Finds save-TOC actor headers of one class: int32 type(1), fstring class,
    /// fstring level, fstring instance, 8 unknown bytes, quat(4f), pos(3f), scale(3f).
    /// </summary>
    private static IEnumerable<ActorHeader> ScanActorHeaders(byte[] body, string className, ResourceNodeKind kind)
    {
        var pattern = Encoding.ASCII.GetBytes(className);
        foreach (var at in FindAll(body, pattern))
        {
            if (at < 8) continue;
            var stringLength = BitConverter.ToInt32(body, at - 4);
            var entryType = BitConverter.ToInt32(body, at - 8);
            if (stringLength != pattern.Length + 1 || entryType != 1) continue;

            var p = at + pattern.Length + 1;
            if (!TryReadFString(body, ref p, out _)) continue;          // level
            if (!TryReadFString(body, ref p, out var instance)) continue;
            p += 8;                                                      // unknown int32 pair
            p += 16;                                                     // quaternion
            if (p + 12 > body.Length) continue;
            var x = BitConverter.ToSingle(body, p);
            var y = BitConverter.ToSingle(body, p + 4);
            var z = BitConverter.ToSingle(body, p + 8);

            // Reject false positives (the class path can appear in property data).
            if (double.IsNaN(x) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7 || Math.Abs(z) > 1e6) continue;

            yield return new ActorHeader(at, kind, instance, x, y, z);
        }
    }

    // -------------------------------------------------- override properties

    private sealed record ResourceOverride(int Offset, string Desc, string? Purity);

    /// <summary>
    /// Finds every mResourceClassOverride property and pairs it with the
    /// mPurityOverride value inside the same object's data block (bounded by
    /// the neighboring override packets).
    /// </summary>
    private static List<ResourceOverride> ScanResourceOverrides(byte[] body)
    {
        var namePattern = Encoding.ASCII.GetBytes("mResourceClassOverride");
        var positions = new List<int>();
        foreach (var at in FindAll(body, namePattern))
        {
            if (at < 4) continue;
            if (BitConverter.ToInt32(body, at - 4) != namePattern.Length + 1) continue;
            if (body[at + namePattern.Length] != 0) continue;
            positions.Add(at);
        }

        var results = new List<ResourceOverride>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            var at = positions[i];
            var desc = FindDescAfter(body, at, 320) ?? "Unknown";

            // Purity is a sibling property in the same block; in practice it
            // precedes the resource override. Clamp the search to this block.
            var windowStart = Math.Max(i > 0 ? positions[i - 1] + 300 : 0, at - 400);
            var purity = FindPurity(body, windowStart, at + 320);

            results.Add(new ResourceOverride(at, desc, purity));
        }
        return results;
    }

    private static string? FindDescAfter(byte[] body, int from, int window)
    {
        var text = Encoding.ASCII.GetString(body, from, Math.Min(window, body.Length - from));
        var start = text.IndexOf("/Game/", StringComparison.Ordinal);
        if (start < 0) return null;
        var end = start;
        while (end < text.Length && text[end] != '\0') end++;
        var path = text[start..end];
        var dot = path.LastIndexOf('.');
        return dot >= 0 ? path[(dot + 1)..] : path;
    }

    private static string? FindPurity(byte[] body, int from, int to)
    {
        from = Math.Max(0, from);
        to = Math.Min(body.Length, to);
        var text = Encoding.ASCII.GetString(body, from, to - from);
        if (text.Contains("RP_Pure", StringComparison.Ordinal)) return "Pure";
        if (text.Contains("RP_Inpure", StringComparison.Ordinal)) return "Impure";
        if (text.Contains("RP_Normal", StringComparison.Ordinal)) return "Normal";
        return null;
    }

    // ------------------------------------------------------------- helpers

    private static IEnumerable<int> FindAll(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack[i] != needle[0]) continue;
            var match = true;
            for (var j = 1; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) yield return i;
        }
    }

    private static bool TryReadFString(byte[] body, ref int position, out string value)
    {
        value = string.Empty;
        if (position + 4 > body.Length) return false;
        var length = BitConverter.ToInt32(body, position);
        position += 4;
        if (length <= 0 || length > 4096 || position + length > body.Length) return false;
        value = Encoding.ASCII.GetString(body, position, length - 1);
        position += length;
        return true;
    }
}
