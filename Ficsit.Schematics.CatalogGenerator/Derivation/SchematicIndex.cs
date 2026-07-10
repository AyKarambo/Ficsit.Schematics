using System.Text.RegularExpressions;
using Ficsit.Schematics.Core.GameData;

namespace Ficsit.Schematics.CatalogGenerator.Derivation;

/// <summary>
/// Resolves unlock tiers from FGSchematic. A milestone's tier key is
/// (mTechTier, 1-based position by mMenuPriority within that tier) — the class names
/// (Schematic_3-4_C) are historical and no longer match in-game positions. HUB upgrades
/// ("HUB Upgrade n") map to 0-n. Alternate/MAM/event schematics carry no tier; the
/// recipes they unlock get theirs from the tier fixpoint instead.
/// </summary>
public sealed partial class SchematicIndex
{
    [GeneratedRegex(@"^HUB Upgrade (\d+)$")]
    private static partial Regex HubUpgradePattern();

    private readonly Dictionary<string, Tier> _recipeTier = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ficsmasRecipes = new(StringComparer.Ordinal);

    public SchematicIndex(DocsExport export)
    {
        var schematics = export.Group("FGSchematic")?.Entries ?? [];

        // Milestones: number by position within their tech tier.
        var milestones = schematics
            .Where(s => s.String("mType") == "EST_Milestone")
            .GroupBy(s => int.Parse(s.Require("mTechTier")));
        foreach (var tierGroup in milestones)
        {
            var ordered = tierGroup
                .OrderBy(s => decimal.Parse(s.String("mMenuPriority") ?? "0", System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
                Record(ordered[i], new Tier(tierGroup.Key, i + 1));
        }

        // HUB upgrades: "HUB Upgrade n" → 0-n.
        foreach (var schematic in schematics.Where(s => s.String("mType") == "EST_Tutorial"))
            if (HubUpgradePattern().Match(schematic.String("mDisplayName") ?? "") is { Success: true } match)
                Record(schematic, new Tier(0, int.Parse(match.Groups[1].Value)));

        // The recipes granted at game start (Iron Ingot/Plate/Rod) → 0-0.
        foreach (var schematic in schematics.Where(s => s.ClassName == "Schematic_StartingRecipes_C"))
            Record(schematic, new Tier(0, 0));

        // Tier-less event schematics still tell us which recipes are FICSMAS content.
        foreach (var schematic in schematics)
            if ((schematic.String("mRelevantEvents") ?? "").Contains("Christmas", StringComparison.Ordinal))
                _ficsmasRecipes.UnionWith(UnlockedRecipes(schematic));
    }

    // Scanner unlocks (BP_UnlockScannableResource_C) are deliberately ignored: the game
    // re-adds ores to the scanner at arbitrary later milestones, so they say nothing about
    // availability. Extraction gating lives in Overrides.OreGates instead.
    private void Record(DocsEntry schematic, Tier tier)
    {
        foreach (var recipe in UnlockedRecipes(schematic))
            if (!_recipeTier.TryGetValue(recipe, out var existing) || tier.CompareTo(existing) < 0)
                _recipeTier[recipe] = tier;
    }

    private static IEnumerable<string> UnlockedRecipes(DocsEntry schematic)
        => schematic.Objects("mUnlocks")
            .Where(u => u.UnlockClass == "BP_UnlockRecipe_C")
            .SelectMany(u => UeText.ClassNames(u.String("mRecipes")));

    /// <summary>The milestone/HUB tier that unlocks a recipe class, if any.</summary>
    public Tier? RecipeTier(string recipeClass)
        => _recipeTier.TryGetValue(recipeClass, out var tier) ? tier : (Tier?)null;

    public bool IsFicsmas(string recipeClass) => _ficsmasRecipes.Contains(recipeClass);
}
