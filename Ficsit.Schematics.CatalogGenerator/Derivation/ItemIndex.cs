using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.CatalogGenerator.Derivation;

/// <summary>One item descriptor the export knows: anything with a form and sink value.</summary>
public sealed record ItemInfo(
    string ClassName,
    string DisplayName,
    bool Fluid,
    int SinkPoints,
    Rational EnergyMJ,
    bool IsResource);

/// <summary>
/// Indexes every item descriptor across all descriptor-like NativeClass groups
/// (FGItemDescriptor, FGResourceDescriptor, biomass, fuels, ammo, consumables, …).
/// An entry counts as an item when it carries mForm and mResourceSinkPoints.
/// </summary>
public sealed class ItemIndex
{
    private readonly Dictionary<string, ItemInfo> _byClass = new(StringComparer.Ordinal);

    public ItemIndex(DocsExport export)
    {
        var resourceClasses = new HashSet<string>(
            export.Group("FGResourceDescriptor")?.Entries.Select(e => e.ClassName) ?? [],
            StringComparer.Ordinal);

        foreach (var group in export.Groups)
            foreach (var entry in group.Entries)
            {
                var form = entry.String("mForm");
                var sink = entry.String("mResourceSinkPoints");
                var name = entry.String("mDisplayName");
                if (form is null || sink is null || string.IsNullOrEmpty(name)) continue;

                _byClass.TryAdd(entry.ClassName, new ItemInfo(
                    entry.ClassName,
                    name,
                    Fluid: form is "RF_LIQUID" or "RF_GAS",
                    SinkPoints: int.Parse(sink),
                    EnergyMJ: Rational.Parse(entry.String("mEnergyValue") ?? "0"),
                    IsResource: resourceClasses.Contains(entry.ClassName)));
            }
    }

    public ItemInfo Require(string className)
        => _byClass.TryGetValue(className, out var item)
            ? item
            : throw new InvalidDataException($"Unknown item descriptor '{className}'.");

    public ItemInfo? Find(string className)
        => _byClass.GetValueOrDefault(className);

    public IEnumerable<ItemInfo> All => _byClass.Values;
}
