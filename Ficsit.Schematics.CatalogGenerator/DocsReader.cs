using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>
/// The parsed Docs export: the game's <c>CommunityResources/Docs/en-US.json</c>, a JSON
/// array of NativeClass groups each holding the class-default entries of one UE class.
/// The repo commits the file re-encoded as UTF-8/LF; a freshly copied UTF-16LE export
/// (the game's native encoding) is detected and read as well.
/// </summary>
public sealed class DocsExport
{
    public IReadOnlyList<DocsGroup> Groups { get; }

    /// <summary>Every entry across all groups, keyed by ClassName (first occurrence wins).</summary>
    public IReadOnlyDictionary<string, DocsEntry> ByClassName { get; }

    private DocsExport(IReadOnlyList<DocsGroup> groups)
    {
        Groups = groups;
        var byClassName = new Dictionary<string, DocsEntry>(StringComparer.Ordinal);
        foreach (var group in groups)
            foreach (var entry in group.Entries)
                byClassName.TryAdd(entry.ClassName, entry);
        ByClassName = byClassName;
    }

    public static DocsExport Load(string path)
    {
        var text = ReadAllTextDetectEncoding(path);
        var root = JsonNode.Parse(text) as JsonArray
            ?? throw new InvalidDataException($"'{path}' is not a JSON array of NativeClass groups.");

        var groups = new List<DocsGroup>(root.Count);
        foreach (var groupNode in root)
        {
            if (groupNode is not JsonObject obj) continue;
            var nativeClass = obj["NativeClass"]?.GetValue<string>() ?? string.Empty;
            var entries = new List<DocsEntry>();
            if (obj["Classes"] is JsonArray classes)
                foreach (var entryNode in classes)
                    if (entryNode is JsonObject entryObj)
                        entries.Add(new DocsEntry(entryObj));
            groups.Add(new DocsGroup(ShortName(nativeClass), nativeClass, entries));
        }
        return new DocsExport(groups);
    }

    /// <summary>Groups by short native-class name (e.g. "FGRecipe").</summary>
    public DocsGroup? Group(string shortName)
        => Groups.FirstOrDefault(g => g.ShortName == shortName);

    /// <summary>"…FactoryGame.FGRecipe'" → "FGRecipe".</summary>
    private static string ShortName(string nativeClass)
    {
        var text = nativeClass.TrimEnd('\'');
        var dot = text.LastIndexOf('.');
        return dot >= 0 ? text[(dot + 1)..] : text;
    }

    /// <summary>Reads the export as UTF-8 (committed form) or UTF-16LE (as the game ships it).</summary>
    private static string ReadAllTextDetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[1] == 0x00)
            return Encoding.Unicode.GetString(bytes); // UTF-16LE without BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>One NativeClass group of the export.</summary>
public sealed record DocsGroup(string ShortName, string NativeClass, IReadOnlyList<DocsEntry> Entries);

/// <summary>One class entry: a flat bag of stringly-typed UE properties.</summary>
public sealed class DocsEntry
{
    private readonly JsonObject _properties;

    public DocsEntry(JsonObject properties)
    {
        _properties = properties;
        ClassName = properties["ClassName"]?.GetValue<string>()
            ?? properties["Class"]?.GetValue<string>() // nested unlock objects carry "Class"
            ?? string.Empty;
    }

    public string ClassName { get; }

    /// <summary>A string property, or null when absent. Most Docs values are strings.</summary>
    public string? String(string name)
        => _properties[name] switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            { } node => node.ToJsonString(),
        };

    /// <summary>A required string property.</summary>
    public string Require(string name)
        => String(name) ?? throw new InvalidDataException($"{ClassName}: missing '{name}'.");

    /// <summary>A nested array property (e.g. FGSchematic's mUnlocks), empty when absent.</summary>
    public IReadOnlyList<DocsEntry> Objects(string name)
    {
        if (_properties[name] is not JsonArray array) return [];
        var result = new List<DocsEntry>(array.Count);
        foreach (var node in array)
            if (node is JsonObject obj)
                result.Add(new DocsEntry(obj));
        return result;
    }

    /// <summary>For nested unlock objects that carry "Class" instead of "ClassName".</summary>
    public string? UnlockClass => String("Class") ?? String("ClassName");
}
