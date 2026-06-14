namespace Ficsit.Schematics.Core.GameData;

/// <summary>
/// Coarse category for a part, used to group the part picker by type.
/// </summary>
public enum PartCategory
{
    /// <summary>Fluids (water, oil, gases, …) — determined by <see cref="PartDefinition.Fluid"/>.</summary>
    Fluid,

    /// <summary>
    /// Raw extractable resources that are not producible from other parts.
    /// Includes all <see cref="Planning.ScarcityWeights.WeightedResources"/> plus Water.
    /// </summary>
    Raw,

    /// <summary>Everything else: crafted intermediates and end products.</summary>
    Intermediate,
}

/// <summary>
/// UI-free helper that assigns a <see cref="PartCategory"/> to any part.
/// The same logic is used in both the part-picker grouping and in tests.
/// </summary>
public static class PartCategoryClassifier
{
    /// <summary>
    /// The set of raw extractable resource names (weighted + Water).
    /// Kept here so the classifier is self-contained for tests.
    /// </summary>
    private static readonly HashSet<string> RawNames =
        new(Planning.ScarcityWeights.WeightedResources, StringComparer.Ordinal) { "Water" };

    /// <summary>
    /// Returns the coarse category for <paramref name="part"/>.
    /// Priority order: Fluid > Raw > Intermediate.
    /// </summary>
    public static PartCategory Classify(PartDefinition part)
    {
        if (part.Fluid) return PartCategory.Fluid;
        if (RawNames.Contains(part.Name)) return PartCategory.Raw;
        return PartCategory.Intermediate;
    }

    /// <summary>Human-readable header used in the grouped part picker.</summary>
    public static string Header(PartCategory category) => category switch
    {
        PartCategory.Fluid => "FLUIDS",
        PartCategory.Raw => "RAW RESOURCES",
        _ => "PARTS & INTERMEDIATES",
    };
}
