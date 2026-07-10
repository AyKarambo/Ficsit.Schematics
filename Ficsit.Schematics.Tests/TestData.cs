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

    private static readonly Lazy<GameDatabase> DatabaseLazy = new(GameDataCatalog.BuildDatabase);

    public static GameDatabase Database => DatabaseLazy.Value;
}
