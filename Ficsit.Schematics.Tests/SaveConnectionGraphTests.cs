using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

// Real-save validation for the connection-graph parse (runs only when the game is installed).
public class SaveConnectionGraphTests
{
    private static string? NewestFactorySave()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FactoryGame", "Saved", "SaveGames");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.sav", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 1_000_000) // a built factory, not a fresh world
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    [Fact]
    public void Recovers_a_connection_graph_from_a_real_save()
    {
        var save = NewestFactorySave();
        if (save is null) return;

        var links = SatisfactorySaveReader.ReadComponentLinks(File.ReadAllBytes(save));

        Assert.NotEmpty(links);
        // Keys and values are component instance paths (Machine.Port).
        Assert.All(links, kv =>
        {
            Assert.Contains(':', kv.Key);
            Assert.Contains('.', kv.Key);
        });
        // Factory connections are mutual, so (almost) every target is itself a linked component.
        var backRefs = links.Values.Count(links.ContainsKey);
        Assert.True(backRefs > links.Count * 0.9, $"Expected mostly bidirectional links, got {backRefs}/{links.Count}.");
    }
}
