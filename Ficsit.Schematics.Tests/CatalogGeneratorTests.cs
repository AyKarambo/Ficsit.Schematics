using Ficsit.Schematics.CatalogGenerator;
using Ficsit.Schematics.CatalogGenerator.Derivation;
using Ficsit.Schematics.CatalogGenerator.Emit;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// The generator's contract with the repo: the committed catalog must equal what the
/// committed Docs export derives (oracle), and re-emitting must be byte-identical
/// (idempotency) while never touching the hand-authored machine structure (boundary).
/// </summary>
public class CatalogGeneratorTests
{
    private static string DocsPath => Path.Combine(
        TestData.RepoRoot, "Ficsit.Schematics.CatalogGenerator", "Docs", "en-US.json");

    private static readonly Lazy<(CatalogModel Model, IReadOnlyList<string> Problems)> DerivedLazy = new(() =>
    {
        var derivation = new CatalogDerivation(DocsExport.Load(DocsPath));
        var model = derivation.Derive();
        return (model, derivation.Problems);
    });

    private static CatalogModel Model => DerivedLazy.Value.Model;

    [Fact]
    public void Derivation_reports_no_problems()
        => Assert.Empty(DerivedLazy.Value.Problems);

    /// <summary>The oracle: every name, rate, tier and machine stat in the compiled
    /// catalog matches the committed export — nothing drifted by hand.</summary>
    [Fact]
    public void Compiled_catalog_matches_the_committed_export()
    {
        var diffs = Verify.Collect(Model, DerivedLazy.Value.Problems, TestData.Database.Document);
        Assert.True(diffs.Count == 0, string.Join(Environment.NewLine, diffs));
    }

    /// <summary>Idempotency: re-emitting every generated file reproduces the committed
    /// bytes exactly — two runs against the same export yield zero diff.</summary>
    [Fact]
    public void Reemitting_generated_files_is_byte_identical_to_disk()
    {
        foreach (var (relative, content) in CatalogWriter.Render(Model))
        {
            var path = Path.Combine(TestData.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Generated file missing on disk: {relative}");
            var onDisk = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.True(onDisk == content, $"Generated file differs from disk: {relative} — rerun the generator.");
        }
    }

    /// <summary>The generated/hand-authored boundary: the machine structure files
    /// (families, marks, capacities) are never generator targets.</summary>
    [Fact]
    public void Hand_authored_machine_files_are_not_generator_targets()
    {
        var generated = CatalogWriter.Render(Model).Keys;
        foreach (var hand in (string[])
                 [
                     "ExtractorMachines", "GeneratorMachines", "ProductionMachines",
                     "SmeltingMachines", "SpecialMachines", "StorageMachines",
                 ])
            Assert.DoesNotContain(generated,
                path => path.EndsWith($"{hand}.cs", StringComparison.Ordinal));
    }

    /// <summary>Every alias target must exist in the current catalog, and no alias key may
    /// shadow a live name (that would rewrite valid documents on load).</summary>
    [Fact]
    public void Alias_table_is_consistent_with_the_catalog()
    {
        var db = TestData.Database;
        foreach (var (legacy, official) in Core.Serialization.NameAliases.ByLegacyName)
        {
            Assert.True(
                db.PartsByName.ContainsKey(official) || db.RecipesByName.ContainsKey(official)
                    || db.MachinesByName.ContainsKey(official),
                $"Alias target '{official}' does not exist in the catalog.");
            Assert.False(
                db.PartsByName.ContainsKey(legacy) || db.RecipesByName.ContainsKey(legacy)
                    || db.MachinesByName.ContainsKey(legacy),
                $"Alias key '{legacy}' shadows a live catalog name.");
        }
    }
}
