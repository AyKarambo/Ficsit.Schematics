using System.Text.RegularExpressions;

namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>
/// Parsers for the stringly-encoded UE property values in the Docs export:
/// item-amount lists, class-reference lists, and asset paths.
/// </summary>
public static partial class UeText
{
    [GeneratedRegex("""ItemClass=[^,]*?\.([A-Za-z0-9_]+)'?"?,Amount=(\d+)""")]
    private static partial Regex ItemAmountPattern();

    [GeneratedRegex(@"\.([A-Za-z0-9_]+)'?""?\s*[,)]")]
    private static partial Regex ClassRefPattern();

    /// <summary>
    /// Parses an item-amount list like
    /// <c>((ItemClass=".../Desc_OreIron.Desc_OreIron_C'",Amount=1),…)</c>
    /// into (class name, amount) pairs.
    /// </summary>
    public static IReadOnlyList<(string ClassName, long Amount)> ItemAmounts(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return ItemAmountPattern().Matches(text)
            .Select(m => (m.Groups[1].Value, long.Parse(m.Groups[2].Value)))
            .ToList();
    }

    /// <summary>
    /// Parses a class-reference list like <c>("/Game/…/Build_ConstructorMk1.Build_ConstructorMk1_C",…)</c>
    /// into bare class names ("Build_ConstructorMk1_C").
    /// </summary>
    public static IReadOnlyList<string> ClassNames(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        // Normalize so every reference ends with a terminator the regex can anchor on.
        return ClassRefPattern().Matches(text + ")")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }
}
