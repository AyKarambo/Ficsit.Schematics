using System.Text;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class FromSaveTests
{
    /// <summary>Builds a synthetic decompressed-body slice: 8 pad bytes, the length-prefixed
    /// <c>mPurchasedSchematics</c> property name, then raw asset-path strings.</summary>
    private static byte[] BuildBody(params string[] paths)
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[8]); // so the marker isn't at offset 0 (reader checks at-4)

        const string name = "mPurchasedSchematics";
        bytes.AddRange(BitConverter.GetBytes(name.Length + 1)); // FString length prefix
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        bytes.Add(0);

        foreach (var path in paths)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(path));
            bytes.Add(0);
        }
        return bytes.ToArray();
    }

    [Fact]
    public void Extracts_alternate_stems_and_ignores_non_alternates()
    {
        var body = BuildBody(
            "/Game/FactoryGame/Schematics/Alternate/Schematic_Alternate_BoltedFrame.Schematic_Alternate_BoltedFrame_C",
            "/Game/FactoryGame/Schematics/Schematic_Alternate_PureIronIngot.Schematic_Alternate_PureIronIngot_C",
            "/Game/FactoryGame/Schematics/Schematic_1-1.Schematic_1-1_C"); // milestone, not an alternate

        var stems = SatisfactorySaveReader.ReadAlternateStemsFromBody(body);

        Assert.Contains("BoltedFrame", stems);
        Assert.Contains("PureIronIngot", stems);
        Assert.Equal(2, stems.Count); // the milestone schematic is not collected
    }

    [Fact]
    public void Walks_past_non_schematic_named_entries_to_reach_alternates()
    {
        // The purchased array interleaves AWESOME-shop customizer entries (named CBG_*, not
        // Schematic_*) between alternates. The walk must not stop at that run — it nearly did,
        // which is why a real save reported "0 alternates".
        var body = BuildBody(
            "/Game/FactoryGame/Schematics/Alternate/Schematic_Alternate_BoltedFrame.Schematic_Alternate_BoltedFrame_C",
            "/Game/FactoryGame/Schematics/ResourceSink/Customizer_Background/CBG_Stairs_Concrete.CBG_Stairs_Concrete_C",
            "/Game/FactoryGame/Schematics/ResourceSink/Customizer_Background/CBG_Foundations.CBG_Foundations_C",
            "/Game/FactoryGame/Schematics/Alternate/Schematic_Alternate_Screw.Schematic_Alternate_Screw_C");

        var stems = SatisfactorySaveReader.ReadAlternateStemsFromBody(body);

        Assert.Contains("BoltedFrame", stems);
        Assert.Contains("Screw", stems); // reached despite the customizer entries in between
        Assert.Equal(2, stems.Count);
    }

    [Fact]
    public void Cast_screw_stem_maps_to_the_recipe()
    {
        // The user's reported case: a save with Cast Screw unlocked (stem "Screw").
        var (unlocked, _) = SchematicRecipeMap.Match(TestData.Database, ["Screw"]);
        Assert.Contains("Cast Screw", unlocked);
    }

    [Fact]
    public void Matches_most_alternates_by_token_set()
    {
        var data = TestData.Database;
        var alternates = data.Document.Recipes.Where(r => r.Alternate).Select(r => r.Name).ToList();
        Assert.NotEmpty(alternates);

        // Most save stems are the PascalCase of the display name; the token matcher must
        // recover the recipe regardless of word order.
        var stems = alternates.Select(name => name.Replace(" ", "").Replace("-", "")).ToList();
        var (unlocked, _) = SchematicRecipeMap.Match(data, stems);

        Assert.True(unlocked.Count >= alternates.Count * 0.8,
            $"expected most alternates to round-trip, got {unlocked.Count}/{alternates.Count}");
    }

    [Fact]
    public void Token_match_is_word_order_insensitive()
    {
        var data = TestData.Database;
        var multiWord = data.Document.Recipes.First(r => r.Alternate && r.Name.Contains(' '));

        // Reverse the words into a stem — token-set matching should still resolve it.
        var scrambled = string.Concat(multiWord.Name.Split(' ').Reverse());
        var (unlocked, _) = SchematicRecipeMap.Match(data, [scrambled]);

        Assert.Contains(multiWord.Name, unlocked);
    }

    [Fact]
    public void Unknown_stem_is_reported_unrecognized()
    {
        var (unlocked, unrecognized) = SchematicRecipeMap.Match(TestData.Database, ["Totally_Made_Up_Thing"]);
        Assert.Empty(unlocked);
        Assert.Contains("Totally_Made_Up_Thing", unrecognized);
    }
}
