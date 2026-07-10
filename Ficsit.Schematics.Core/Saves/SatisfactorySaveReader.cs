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

    // ------------------------------------------------- connection graph

    /// <summary>
    /// Component → connected component links, read from the persistent level's object data
    /// (every <c>mConnectedComponent</c> attributed to its owner). Keys/values are component
    /// instance paths like <c>Persistent_Level:PersistentLevel.Build_X_123.Output0</c>.
    /// Public so the connection-graph parse can be validated against a real save.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadComponentLinks(byte[] saveFile)
        => ReadComponentLinksFromBody(DecompressBody(saveFile));

    private static readonly byte[] ConnMarker = Encoding.ASCII.GetBytes("mConnectedComponent");

    internal static Dictionary<string, string> ReadComponentLinksFromBody(byte[] body)
        => ScanObjectDataFromBody(body).Links;

    /// <summary>Everything the per-object data walk attributes to owners, keyed by instance path.</summary>
    internal sealed class ObjectDataScan
    {
        /// <summary>Component → connected component (<c>mConnectedComponent</c>).</summary>
        public Dictionary<string, string> Links { get; } = new(StringComparer.Ordinal);

        /// <summary>Machine → clock fraction (<c>mCurrentPotential</c>; present only when the
        /// clock was changed — the 100% default is not serialized).</summary>
        public Dictionary<string, float> Clocks { get; } = new(StringComparer.Ordinal);

        /// <summary>Machine → Somersloops installed in its potential inventory.</summary>
        public Dictionary<string, int> Somersloops { get; } = new(StringComparer.Ordinal);

        /// <summary>Machine → its <c>mCurrentRecipe</c> stem ("IronPlate", "Alternate_BoltedFrame").
        /// Exact per-actor attribution — no order correlation needed.</summary>
        public Dictionary<string, string> Recipes { get; } = new(StringComparer.Ordinal);

        /// <summary>Truck/fluid-truck station → the road network its docking path node is on
        /// (<c>mPathNetworkID</c>); stations sharing a network form one truck circuit.</summary>
        public Dictionary<string, int> StationNetworks { get; } = new(StringComparer.Ordinal);

        /// <summary>Drone port → its <c>mPairedStation</c> destination port.</summary>
        public Dictionary<string, string> DronePairs { get; } = new(StringComparer.Ordinal);
    }

    internal static ObjectDataScan ScanObjectDataFromBody(byte[] body)
    {
        var scan = new ObjectDataScan();
        if (FindPersistentLevelBlobs(body) is not { } blobs) return scan;

        var instances = WalkTocInstances(body, blobs.TocStart);
        WalkObjectData(body, blobs.DataStart, instances, scan);
        return scan;
    }

    /// <summary>
    /// Locate the persistent level's TOC and Data blob contents. Both are int64-length-prefixed
    /// (so the high dword of a real, sub-4 GB length is 0 — binary noise rarely is) and both begin
    /// with the same large <c>int32 numObjects</c>; that match, plus building instances in the TOC,
    /// narrows the field without parsing the version-sensitive grid/streaming-level preamble. The
    /// framing checks alone are not enough on large saves — a 107 MB body can contain junk
    /// length-pairs whose fake spans even include the building marker — so every candidate must
    /// prove itself by <see cref="TocWalks">actually walking</see>: all of its object headers must
    /// parse. Among the survivors the highest object count wins (the persistent level has the most).
    /// </summary>
    private static (int TocStart, long TocLen, int DataStart, long DataLen)? FindPersistentLevelBlobs(byte[] body)
    {
        var marker = Encoding.ASCII.GetBytes("PersistentLevel.Build_");
        (int, long, int, long)? best = null;
        var bestCount = 0;
        for (var p = 0; p + 16 < body.Length; p++)
        {
            if (BitConverter.ToInt32(body, p + 4) != 0) continue;           // TOC length high dword
            var tocLen = BitConverter.ToInt64(body, p);
            if (tocLen < 100_000 || p + 8 + tocLen + 12 > body.Length) continue;
            var n1 = BitConverter.ToInt32(body, p + 8);
            if (n1 is < 1000 or > 2_000_000) continue;                      // many objects (filters noise)
            var dataPos = p + 8 + (int)tocLen;
            if (BitConverter.ToInt32(body, dataPos + 4) != 0) continue;     // Data length high dword
            var dataLen = BitConverter.ToInt64(body, dataPos);
            if (dataLen < 4 || dataPos + 8 + dataLen > body.Length) continue;
            if (BitConverter.ToInt32(body, dataPos + 8) != n1) continue;    // TOC/Data numObjects match
            if (n1 > bestCount
                && TocWalks(body, p + 8, tocLen, n1)
                && IndexOf(body, marker, p + 8, p + 8 + (int)tocLen) >= 0)
            {
                best = (p + 8, tocLen, dataPos + 8, dataLen);
                bestCount = n1;
            }
        }
        return best;
    }

    /// <summary>True when the TOC blob at <paramref name="tocStart"/> really is a level TOC: all
    /// <paramref name="count"/> object headers parse without running past its length (a small
    /// trailing tail after the last header is normal). Junk length-pairs that satisfy the cheap
    /// framing checks fail within their first header, so the walk is what tells the real
    /// persistent level from binary noise.</summary>
    private static bool TocWalks(byte[] body, int tocStart, long tocLen, int count)
    {
        var end = (int)Math.Min(tocStart + tocLen, body.Length);
        var pos = tocStart + 4; // past numObjects
        for (var i = 0; i < count; i++)
        {
            if (pos + 4 > end) return false;
            var isActor = BitConverter.ToInt32(body, pos); pos += 4;
            if (isActor is not (0 or 1)) return false;
            if (!SkipFString(body, ref pos, end)) return false;             // className
            if (!SkipFString(body, ref pos, end)) return false;             // levelName
            if (!SkipFString(body, ref pos, end)) return false;             // pathName
            pos += 4;                                                       // ObjectFlags
            if (isActor == 1) pos += 4 + 16 + 12 + 12 + 4;                  // transform block
            else if (!SkipFString(body, ref pos, end)) return false;        // OuterPathName
        }
        return pos <= end;
    }

    /// <summary>Skip one FString (ASCII or UTF-16 framing) staying inside [.., <paramref name="end"/>).</summary>
    private static bool SkipFString(byte[] body, ref int pos, int end)
    {
        if (pos + 4 > end) return false;
        var len = BitConverter.ToInt32(body, pos); pos += 4;
        if (len == 0) return true;
        var bytes = len > 0 ? len : -(long)len * 2;
        if (bytes > 1 << 20 || pos + bytes > end) return false;
        pos += (int)bytes;
        return true;
    }

    /// <summary>Walk the TOC blob's object headers in order, returning each object's instance path.
    /// Actor and (non-actor) object headers differ after the path; both start with int32 isActor.</summary>
    private static List<string> WalkTocInstances(byte[] body, int tocStart)
    {
        var pos = tocStart;
        var count = BitConverter.ToInt32(body, pos); pos += 4;
        var instances = new List<string>(Math.Min(count, 1_000_000));
        for (var i = 0; i < count; i++)
        {
            if (pos + 4 > body.Length) break;
            var isActor = BitConverter.ToInt32(body, pos); pos += 4;
            if (!ReadFString(body, ref pos, out _)) break;            // className
            if (!ReadFString(body, ref pos, out _)) break;            // levelName
            if (!ReadFString(body, ref pos, out var pathName)) break; // pathName (instance)
            instances.Add(pathName);
            pos += 4;                                                 // ObjectFlags
            if (isActor == 1)
                pos += 4 + 16 + 12 + 12 + 4;                          // needTransform + quat + pos + scale + wasPlaced
            else if (!ReadFString(body, ref pos, out _)) break;       // OuterPathName
        }
        return instances;
    }

    private const string PotentialInventorySuffix = ".InventoryPotential";

    /// <summary>Walk the Data blob's object blobs in order (positional with the TOC), scanning each
    /// blob for what we attribute to its owner: <c>mConnectedComponent</c> (belt/pipe wiring),
    /// <c>mCurrentPotential</c> (clock), and — on <c>.InventoryPotential</c> components — the
    /// Somersloop stacks, credited to the machine that owns the inventory.</summary>
    private static void WalkObjectData(byte[] body, int dataStart, List<string> instances, ObjectDataScan scan)
    {
        var pos = dataStart;
        var count = BitConverter.ToInt32(body, pos); pos += 4;
        var n = Math.Min(count, instances.Count);
        for (var i = 0; i < n; i++)
        {
            if (pos + 12 > body.Length) break;
            var saveVer = BitConverter.ToInt32(body, pos); pos += 4;  // per-object save version
            pos += 4;                                                 // ShouldMigrateObjectRefsToPersistent (int32)
            var dataLen = BitConverter.ToInt32(body, pos); pos += 4;
            if (dataLen < 0 || pos + dataLen > body.Length) break;

            var instance = instances[i];
            if (FindLinkTarget(body, pos, dataLen) is { } target) scan.Links[instance] = target;
            if (FindClockValue(body, pos, dataLen) is { } clock) scan.Clocks[instance] = clock;
            if (FindRecipeStem(body, pos, dataLen) is { } recipe) scan.Recipes[instance] = recipe;
            if (instance.EndsWith(PotentialInventorySuffix, StringComparison.Ordinal)
                && CountSomersloops(body, pos, dataLen) is > 0 and var sloops)
                scan.Somersloops[instance[..^PotentialInventorySuffix.Length]] = sloops;

            // A station's docking path node names its road network; the node's parent actor (the
            // first object ref in its data) is the station itself.
            if (instance.Contains(".Build_VehiclePathNode_DockingStation_C_", StringComparison.Ordinal)
                && FindIntProperty(body, pos, dataLen, PathNetworkIdMarker) is { } network
                && FindInstancePath(body, pos, pos + dataLen) is { } station)
                scan.StationNetworks[station] = network;

            if (instance.Contains(".Build_DroneStation_C_", StringComparison.Ordinal)
                && FindPathAfterMarker(body, pos, dataLen, PairedStationMarker) is { } paired)
                scan.DronePairs[instance] = paired;

            pos += dataLen;
            if (saveVer >= 53)                                        // [>=53] per-object version data
            {
                if (pos + 4 > body.Length) break;
                if (BitConverter.ToInt32(body, pos) != 0) { pos += 4; SkipObjectVersionData(body, ref pos); }
                else pos += 4;
            }
        }
    }

    /// <summary>The <c>mConnectedComponent</c> target path inside one object's data blob: the first
    /// instance-path string (contains ':') after the property name.</summary>
    private static string? FindLinkTarget(byte[] body, int start, int len)
        => FindPathAfterMarker(body, start, len, ConnMarker);

    /// <summary>The first instance path after <paramref name="marker"/> within one object's data
    /// blob, or null when the marker isn't there.</summary>
    private static string? FindPathAfterMarker(byte[] body, int start, int len, byte[] marker)
    {
        var at = IndexOf(body, marker, start, start + len);
        return at < 0 ? null : FindInstancePath(body, at + marker.Length, start + len);
    }

    /// <summary>The first instance-path string (a printable run of ≥8 chars containing ':' and
    /// '.') in [<paramref name="from"/>, <paramref name="end"/>), or null.</summary>
    private static string? FindInstancePath(byte[] body, int from, int end)
    {
        var run = new System.Text.StringBuilder();
        for (var i = from; i < end; i++)
        {
            var b = body[i];
            if (b is >= 32 and < 127) run.Append((char)b);
            else
            {
                if (run.Length >= 8 && run.ToString() is var s && s.Contains(':') && s.Contains('.'))
                    return s;
                run.Clear();
            }
        }
        return null;
    }

    /// <summary>The value of the int property named by <paramref name="marker"/> inside one
    /// object's data blob (name FString, "IntProperty" FString, size/index pair, pad byte,
    /// value), or null when absent or framed differently.</summary>
    private static int? FindIntProperty(byte[] body, int start, int len, byte[] marker)
    {
        var end = start + len;
        var at = IndexOf(body, marker, start, end);
        if (at < 4) return null;
        if (BitConverter.ToInt32(body, at - 4) != marker.Length + 1) return null;    // FString length
        if (body[at + marker.Length] != 0) return null;                              // exact name
        var p = at + marker.Length + 1;
        if (!IsFString(body, ref p, IntPropertyMarker, end)) return null;
        if (p + 13 > end) return null;
        return BitConverter.ToInt32(body, p + 9);
    }

    private static readonly byte[] ClockMarker = Encoding.ASCII.GetBytes("mCurrentPotential");
    private static readonly byte[] FloatPropertyMarker = Encoding.ASCII.GetBytes("FloatProperty");
    private static readonly byte[] SloopMarker = Encoding.ASCII.GetBytes("Desc_WAT1.Desc_WAT1_C");
    private static readonly byte[] NumItemsMarker = Encoding.ASCII.GetBytes("NumItems");
    private static readonly byte[] IntPropertyMarker = Encoding.ASCII.GetBytes("IntProperty");
    private static readonly byte[] PathNetworkIdMarker = Encoding.ASCII.GetBytes("mPathNetworkID");
    private static readonly byte[] PairedStationMarker = Encoding.ASCII.GetBytes("mPairedStation");
    private static readonly byte[] CurrentRecipeMarker = Encoding.ASCII.GetBytes("mCurrentRecipe");

    /// <summary>The machine's <c>mCurrentRecipe</c> stem inside one object's data blob, or null.
    /// Same asset-path extraction the whole-body <see cref="ScanRecipeStems"/> uses, but attributed
    /// to its owner exactly rather than correlated by order.</summary>
    private static string? FindRecipeStem(byte[] body, int start, int len)
    {
        var at = IndexOf(body, CurrentRecipeMarker, start, start + len);
        if (at < 4) return null;
        if (BitConverter.ToInt32(body, at - 4) != CurrentRecipeMarker.Length + 1) return null;
        if (body[at + CurrentRecipeMarker.Length] != 0) return null;
        var from = at + CurrentRecipeMarker.Length;
        return RecipeStemAfter(body, from, Math.Min(320, start + len - from));
    }

    /// <summary>The <c>mCurrentPotential</c> float inside one object's data blob — the machine's
    /// clock as a fraction (0.4 = 40%) — or null when the property isn't there. Framing (verified
    /// against a real save): name FString, "FloatProperty" FString, two int32s of which one is the
    /// payload size 4, a zero byte, then the float.</summary>
    private static float? FindClockValue(byte[] body, int start, int len)
    {
        var end = start + len;
        var at = IndexOf(body, ClockMarker, start, end);
        if (at < 0 || at < 4) return null;
        if (BitConverter.ToInt32(body, at - 4) != ClockMarker.Length + 1) return null;   // FString length
        if (body[at + ClockMarker.Length] != 0) return null;                              // exact name
        var p = at + ClockMarker.Length + 1;
        if (!IsFString(body, ref p, FloatPropertyMarker, end)) return null;
        if (p + 13 > end) return null;
        var a = BitConverter.ToInt32(body, p);
        var b = BitConverter.ToInt32(body, p + 4);
        if ((a != 4 && b != 4) || body[p + 8] != 0) return null;                          // size + pad byte
        return BitConverter.ToSingle(body, p + 9);
    }

    /// <summary>Somersloops inside one potential-inventory blob: the summed <c>NumItems</c> of its
    /// <c>Desc_WAT1</c> (Somersloop) stacks. The overclocking shard slots share this inventory, so
    /// the item class is the filter; a vacated slot can keep the class with <c>NumItems</c> 0, and
    /// the building's cached <c>mCurrentProductionBoost</c> can go stale — the items are the truth.</summary>
    private static int CountSomersloops(byte[] body, int start, int len)
    {
        var end = start + len;
        var total = 0;
        var pos = start;
        while (true)
        {
            var at = IndexOf(body, SloopMarker, pos, end);
            if (at < 0) break;
            pos = at + SloopMarker.Length;

            var numAt = IndexOf(body, NumItemsMarker, pos, end);
            if (numAt < 4 || BitConverter.ToInt32(body, numAt - 4) != NumItemsMarker.Length + 1
                || body[numAt + NumItemsMarker.Length] != 0) continue;
            var p = numAt + NumItemsMarker.Length + 1;
            if (!IsFString(body, ref p, IntPropertyMarker, end)) continue;
            if (p + 13 > end) break;
            var count = BitConverter.ToInt32(body, p + 9);                                // size/index + pad
            if (count is > 0 and <= 100) total += count;
        }
        return total;
    }

    /// <summary>True when an FString holding exactly <paramref name="text"/> sits at
    /// <paramref name="pos"/> (length prefix, bytes, null terminator); advances past it.</summary>
    private static bool IsFString(byte[] body, ref int pos, byte[] text, int end)
    {
        if (pos + 4 + text.Length + 1 > end) return false;
        if (BitConverter.ToInt32(body, pos) != text.Length + 1) return false;
        for (var j = 0; j < text.Length; j++)
            if (body[pos + 4 + j] != text[j]) return false;
        if (body[pos + 4 + text.Length] != 0) return false;
        pos += 4 + text.Length + 1;
        return true;
    }

    /// <summary>Skip an FSaveObjectVersionData: version ints, FEngineVersion, branch FString,
    /// and the custom-version array (GUID + int32 each).</summary>
    private static void SkipObjectVersionData(byte[] body, ref int pos)
    {
        pos += 4 + 4 + 4 + 4 + 2 + 2 + 2 + 4; // version, UE4, UE5, licensee, major/minor/patch, changelist
        ReadFString(body, ref pos, out _);    // branch
        var customCount = BitConverter.ToInt32(body, pos); pos += 4;
        pos += customCount * (16 + 4);
    }

    private static bool ReadFString(byte[] body, ref int pos, out string value)
    {
        value = string.Empty;
        if (pos + 4 > body.Length) return false;
        var len = BitConverter.ToInt32(body, pos); pos += 4;
        if (len == 0) return true;
        if (len > 0)
        {
            if (len > 1 << 20 || pos + len > body.Length) return false;
            value = Encoding.ASCII.GetString(body, pos, len - 1);
            pos += len;
        }
        else
        {
            var n = -len;
            if (n > 1 << 20 || pos + n * 2 > body.Length) return false;
            value = Encoding.Unicode.GetString(body, pos, (n - 1) * 2);
            pos += n * 2;
        }
        return true;
    }

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

    public static SaveWorld ReadWorld(byte[] saveFile) => ReadWorldFromBody(DecompressBody(saveFile));

    /// <summary><see cref="ReadWorld(byte[])"/> over an already-inflated body. Public for unit
    /// testing against a synthetic body.</summary>
    public static SaveWorld ReadWorldFromBody(byte[] body)
    {
        var scan = ScanObjectDataFromBody(body);
        var buildings = ReadBuildingsFromBody(body);
        foreach (var building in buildings)
        {
            // 0 < clock ≤ 2.5 is the game's valid range; anything else is a misparse — keep 100%.
            if (scan.Clocks.TryGetValue(building.Instance, out var clock) && clock is > 0f and <= 2.5f)
                building.ClockSpeed = ToClockFraction(clock);
            if (scan.Somersloops.TryGetValue(building.Instance, out var sloops))
                building.Somersloops = sloops;
            if (scan.Recipes.TryGetValue(building.Instance, out var recipe))
                building.RecipeStem = recipe;
        }
        return new SaveWorld
        {
            Buildings = buildings,
            ResourceNodes = ReadResourceNodesFromBody(body),
            RecipeStems = ScanRecipeStems(body),
            ComponentLinks = scan.Links,
            VehicleRoutes = BuildVehicleRoutes(scan),
        };
    }

    /// <summary>Vehicle circuits from the scan: truck stations grouped by road network, drone
    /// ports by their mutual pairing.</summary>
    private static List<SaveVehicleRoute> BuildVehicleRoutes(ObjectDataScan scan)
    {
        var routes = new List<SaveVehicleRoute>();
        foreach (var network in scan.StationNetworks.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
        {
            var stations = network.Select(kv => kv.Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (stations.Count >= 2) routes.Add(new SaveVehicleRoute(Model.LogisticsKind.Truck, stations));
        }
        var seenPairs = new HashSet<(string, string)>();
        foreach (var (port, paired) in scan.DronePairs)
        {
            var pair = string.CompareOrdinal(port, paired) <= 0 ? (port, paired) : (paired, port);
            if (seenPairs.Add(pair))
                routes.Add(new SaveVehicleRoute(Model.LogisticsKind.Drone, [pair.Item1, pair.Item2]));
        }
        return routes;
    }

    /// <summary>A serialized clock float as an exact fraction, rounded at the game's clock
    /// precision (4 decimals of percent), so 0.333333343f reads back as exactly 33.3333%.</summary>
    private static Numerics.Rational ToClockFraction(float value)
        => new((long)Math.Round(value * 1_000_000.0), 1_000_000);

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
