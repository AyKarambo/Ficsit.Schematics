using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveReaderTests
{
    /// <summary>Newest local Satisfactory save, when the game is installed on this machine.</summary>
    private static string? NewestLocalSave()
    {
        var saveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactoryGame", "Saved", "SaveGames");
        if (!Directory.Exists(saveDir)) return null;
        return Directory.EnumerateFiles(saveDir, "*.sav", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 100_000) // skip manager/settings stubs
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    [Fact]
    public void Reads_resource_nodes_from_a_real_save()
    {
        var save = NewestLocalSave();
        if (save is null) return; // no game installed here — nothing to verify

        var nodes = SatisfactorySaveReader.ReadResourceNodes(save);

        Assert.True(nodes.Count > 100, $"Expected hundreds of nodes, got {nodes.Count}.");

        // Positions must be on the map (world is roughly ±5km in cm).
        Assert.All(nodes, n =>
        {
            Assert.InRange(n.X, -600_000, 600_000);
            Assert.InRange(n.Y, -600_000, 600_000);
        });

        // Order-correlation sanity: wells carry well fluids, plain nodes never do well-only gases.
        var wellParts = new[] { "Water", "Crude Oil", "Nitrogen Gas" };
        var satellites = nodes.Where(n => n.Kind == ResourceNodeKind.FrackingSatellite).ToList();
        if (satellites.Count > 0)
            Assert.All(satellites, s => Assert.Contains(s.Part, wellParts));
        var plainNodes = nodes.Where(n => n.Kind == ResourceNodeKind.Node).ToList();
        Assert.All(plainNodes, n => Assert.NotEqual("Nitrogen Gas", n.Part));
        Assert.All(plainNodes, n => Assert.NotEqual("Water", n.Part));

        // No unresolved resources when correlation holds, and purities are sane.
        Assert.DoesNotContain(nodes, n => n.Part == "Unknown");
        Assert.All(nodes, n => Assert.Contains(n.Purity, new[] { "Impure", "Normal", "Pure" }));

        // Geysers exist and are typed as such.
        Assert.Contains(nodes, n => n.Kind == ResourceNodeKind.Geyser && n.Part == "Geyser");
    }
}
