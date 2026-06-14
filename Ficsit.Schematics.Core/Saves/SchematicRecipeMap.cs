using System.Text;
using Ficsit.Schematics.Core.GameData;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Maps the unlocked-alternate <em>schematic stems</em> read from a save
/// (<see cref="SatisfactorySaveReader.ReadUnlockedAlternateSchematics(byte[])"/>) onto our
/// catalog alternate-recipe names. The save's stem is Coffee Stain's internal schematic
/// name, which usually differs from our display name only by word order/casing, so the
/// primary match is a <b>token-set</b> comparison ("IronIngot_Leached" ⇔ "Leached Iron
/// Ingot"). A small, catalog-validated override table covers the few that diverge more
/// (e.g. "SteelBeam_Aluminum" → "Aluminum Beam"). Stems with no confident match are
/// returned as <c>Unrecognized</c> so the UI can report them instead of guessing.
/// </summary>
public static class SchematicRecipeMap
{
    /// <summary>
    /// Stem → recipe display name for cases the token matcher can't resolve (the internal
    /// name carries an extra/different word). Only applied when the target actually exists as
    /// an alternate in the catalog, so a stale entry is harmless rather than a mis-enable.
    /// Seeded from docs/specs/from-save-spike.md; numbered variants needing in-game
    /// verification are deliberately omitted (they surface as "unrecognized" instead).
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Word-swap names the token matcher already gets, kept here for clarity / safety.
        ["SteelBeam_Aluminum"] = "Aluminum Beam",
        ["SteelPipe_Iron"] = "Iron Pipe",
        ["SteelBeam_Molded"] = "Molded Beam",
        ["SteelCastedPlate"] = "Steel Cast Plate",
        ["AILimiter_Plastic"] = "Plastic AI Limiter",
        ["ElectroAluminumScrap"] = "Electrode Aluminum Scrap",
        ["EnrichedCoal"] = "Compacted Coal",
        ["HeatSink1"] = "Heat Exchanger",
        ["Plastic1"] = "Recycled Plastic",
        ["Quickwire"] = "Fused Quickwire",
        ["HighSpeedConnector"] = "Silicon High-Speed Connector",
        ["HighSpeedWiring"] = "Automated Speed Wiring",
        ["Gunpowder1"] = "Fine Black Powder",
        ["Quartz_Fused"] = "Fused Quartz Crystal",
        ["Quartz_Purified"] = "Quartz Purification",
        ["CrystalOscillator"] = "Insulated Crystal Oscillator",
        ["OCSupercomputer"] = "OC Supercomputer",

        // Numbered / internal-name variants (verified against the standard 1.x naming;
        // every target is checked against the catalog at runtime, so a wrong entry is a
        // no-op rather than a mis-enable).
        ["Cable1"] = "Insulated Cable",
        ["Cable2"] = "Quickwire Cable",
        ["Wire1"] = "Iron Wire",
        ["Wire2"] = "Caterium Wire",
        ["Coal1"] = "Charcoal",
        ["Coal2"] = "Biocoal",
        ["Computer1"] = "Crystal Computer",
        ["Computer2"] = "Caterium Computer",
        ["CircuitBoard1"] = "Silicon Circuit Board",
        ["CircuitBoard2"] = "Caterium Circuit Board",
        ["IngotIron"] = "Iron Alloy Ingot",
        ["IngotSteel1"] = "Solid Steel Ingot",
        ["IngotSteel2"] = "Compacted Steel Ingot",
        ["ReinforcedIronPlate1"] = "Bolted Iron Plate",
        ["ReinforcedIronPlate2"] = "Stitched Iron Plate",
        ["ModularFrame"] = "Steeled Frame",
        ["HeavyModularFrame"] = "Heavy Encased Frame",
        ["Screw"] = "Cast Screw",
        ["Screw2"] = "Steel Screw",
        ["Rotor"] = "Steel Rotor",
        ["Stator"] = "Quickwire Stator",
        ["Motor1"] = "Rigor Motor",
        ["Silica"] = "Cheap Silica",
        ["Concrete"] = "Fine Concrete",
        ["RadioControlUnit1"] = "Radio Connection Unit",
    };

    /// <summary>The unlocked alternate recipe names and any stems that couldn't be matched.</summary>
    public static (HashSet<string> Unlocked, List<string> Unrecognized) Match(
        GameDatabase data, IEnumerable<string> stems)
    {
        // Token-set key → the unique alternate with that key (null marks an ambiguous key
        // shared by two recipes, which we never auto-match).
        var byTokens = new Dictionary<string, string?>(StringComparer.Ordinal);
        var alternateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in data.Document.Recipes)
        {
            if (!recipe.Alternate) continue;
            alternateNames.Add(recipe.Name);
            var key = TokenKey(recipe.Name);
            byTokens[key] = byTokens.ContainsKey(key) ? null : recipe.Name;
        }

        var unlocked = new HashSet<string>(StringComparer.Ordinal);
        var unrecognized = new List<string>();
        foreach (var stem in stems)
        {
            if (Overrides.TryGetValue(stem, out var mapped) && alternateNames.Contains(mapped))
            {
                unlocked.Add(mapped);
                continue;
            }
            if (byTokens.TryGetValue(TokenKey(stem), out var match) && match is not null)
            {
                unlocked.Add(match);
                continue;
            }
            unrecognized.Add(stem);
        }
        return (unlocked, unrecognized);
    }

    /// <summary>Sorted, lowercased word tokens (split on non-alphanumerics, camelCase, and
    /// letter↔digit boundaries) joined into a canonical key — order-insensitive.</summary>
    private static string TokenKey(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var prev = '\0';

        void Flush()
        {
            if (sb.Length > 0) { tokens.Add(sb.ToString().ToLowerInvariant()); sb.Clear(); }
        }

        foreach (var c in text)
        {
            if (!char.IsLetterOrDigit(c)) { Flush(); prev = c; continue; }
            if (sb.Length > 0 &&
                ((char.IsUpper(c) && char.IsLower(prev)) || (char.IsDigit(c) != char.IsDigit(prev))))
                Flush();
            sb.Append(c);
            prev = c;
        }
        Flush();

        tokens.Sort(StringComparer.Ordinal);
        return string.Join('|', tokens);
    }
}
