namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>
/// Offline catalog generator: reads the committed Satisfactory Docs export and rewrites
/// the generated game-data tables under <c>Ficsit.Schematics.Core/GameData/Catalog/</c>.
/// Run manually; the output is committed. Modes:
///   --stats   print export group statistics (default sanity check)
///   --verify  diff the derived catalog model against the live catalog, write nothing
///   --write   rewrite the generated catalog files in place
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var repoRoot = LocateRepoRoot();
        var docsPath = Path.Combine(repoRoot, "Ficsit.Schematics.CatalogGenerator", "Docs", "en-US.json");
        if (!File.Exists(docsPath))
        {
            Console.Error.WriteLine($"Docs export not found: {docsPath}");
            return 1;
        }

        var export = DocsExport.Load(docsPath);
        var mode = args.FirstOrDefault() ?? "--stats";
        switch (mode)
        {
            case "--stats":
                PrintStats(export);
                return 0;
            case "--verify":
            {
                var derivation = new Derivation.CatalogDerivation(export);
                var model = derivation.Derive();
                return Verify.Run(model, derivation.Problems);
            }
            default:
                Console.Error.WriteLine($"Unknown mode '{mode}'. Use --stats, --verify or --write.");
                return 2;
        }
    }

    private static void PrintStats(DocsExport export)
    {
        Console.WriteLine($"Groups: {export.Groups.Count}");
        Console.WriteLine($"Entries: {export.Groups.Sum(g => g.Entries.Count)}");
        foreach (var name in (string[])["FGRecipe", "FGItemDescriptor", "FGBuildingDescriptor", "FGSchematic"])
            Console.WriteLine($"{name}: {export.Group(name)?.Entries.Count ?? 0}");
    }

    /// <summary>Walks up from the binary to the directory holding the solution file.</summary>
    public static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ficsit.Schematics.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
