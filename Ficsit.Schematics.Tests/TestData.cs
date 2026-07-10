using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.GameData.Catalog;

namespace Ficsit.Schematics.Tests;

/// <summary>Locates repo assets by walking up from the test binary to the solution root.</summary>
public static class TestData
{
    private static readonly Lazy<string> RepoRootLazy = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ficsit.Schematics.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    });

    public static string RepoRoot => RepoRootLazy.Value;

    public static string IconsDir => Path.Combine(RepoRoot, "Resources", "Raw", "icons");
    public static string CustomIconsDir => Path.Combine(RepoRoot, "Resources", "Raw", "custom_icons");
    public static string ReferenceSavePath =>
        Path.Combine(RepoRoot, "Ficsit.Schematics.Tests", "Fixtures", "reference-save.sfmd");

    /// <summary>Real rail save (v60, build 491125): 21 named train stations, 9 timetabled
    /// trains, 40 freight platforms — the train-import fixture.</summary>
    public static string DuneDesertSavePath =>
        Path.Combine(RepoRoot, "Ficsit.Schematics.Tests", "Resources", "dune_desert_240526-214024.sav");

    /// <summary>Real rail-free save (471 machines, 3 truck routes) — the no-false-positives
    /// regression fixture for the train import.</summary>
    public static string RandomNodeSavePath =>
        Path.Combine(RepoRoot, "Ficsit.Schematics.Tests", "Resources", "random.node_070626-181115.sav");

    public static GameDatabase Database => GameDataCatalog.Shared;
}
