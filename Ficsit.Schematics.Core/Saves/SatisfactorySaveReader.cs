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

    // ------------------------------------------------- unlocked alternates

    private const string PurchasedSchematicsProperty = "mPurchasedSchematics";
    private const string AlternatePrefix = "Schematic_Alternate_";

    public static IReadOnlyList<string> ReadUnlockedAlternateSchematics(string filePath)
        => ReadUnlockedAlternateSchematics(File.ReadAllBytes(filePath));

    /// <summary>
    /// The stems of every unlocked <c>Schematic_Alternate_*</c> in the save's
    /// <c>mPurchasedSchematics</c> array (e.g. "PureIronIngot", "BoltedFrame"). Reuses the
    /// resource-node reader's chunk inflation; the array region is scanned for the alternate
    /// asset paths rather than parsed by struct offset, which is robust across save versions
    /// (see docs/specs/from-save-spike.md).
    /// </summary>
    public static IReadOnlyList<string> ReadUnlockedAlternateSchematics(byte[] saveFile)
        => ReadAlternateStemsFromBody(DecompressBody(saveFile));

    /// <summary>Parses alternate stems out of an already-decompressed save body. Public so it
    /// can be unit-tested against a synthetic body without a real <c>.sav</c> fixture.</summary>
    public static IReadOnlyList<string> ReadAlternateStemsFromBody(byte[] body)
    {
        var stems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var marker = Encoding.ASCII.GetBytes(PurchasedSchematicsProperty);
        foreach (var at in FindAll(body, marker))
        {
            // Validate it's the FString property name: int32 length (= len + 1) precedes it,
            // and it is null-terminated. (Mirrors ScanResourceOverrides' guards.)
            if (at < 4) continue;
            if (BitConverter.ToInt32(body, at - 4) != marker.Length + 1) continue;
            if (at + marker.Length >= body.Length || body[at + marker.Length] != 0) continue;

            CollectAlternateStems(body, at, stems, seen);
            break; // exactly one occurrence in practice
        }
        return stems;
    }

    /// <summary>Walks forward from the property over the contiguous array, collecting the stem
    /// after each <c>Schematic_Alternate_</c> token. Stops once the entries stop appearing
    /// (a large gap = the array ended and other properties follow).</summary>
    private static void CollectAlternateStems(byte[] body, int from, List<string> stems, HashSet<string> seen)
    {
        var token = Encoding.ASCII.GetBytes(AlternatePrefix);
        var hardEnd = Math.Min(body.Length, from + 256 * 1024);
        var pos = from;
        var lastHit = from;
        while (pos < hardEnd)
        {
            var idx = IndexOf(body, token, pos, hardEnd);
            if (idx < 0 || idx - lastHit > 4096) break; // array ended; later refs aren't purchased
            var start = idx + token.Length;
            var end = start;
            while (end < body.Length && (char.IsLetterOrDigit((char)body[end]) || body[end] == '_')) end++;
            var stem = Encoding.ASCII.GetString(body, start, end - start);
            if (stem.EndsWith("_C", StringComparison.Ordinal)) stem = stem[..^2]; // class-name copy
            if (stem.Length > 0 && seen.Add(stem)) stems.Add(stem);
            lastHit = idx;
            pos = end;
        }
    }

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
    {
        var body = DecompressBody(saveFile);

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
