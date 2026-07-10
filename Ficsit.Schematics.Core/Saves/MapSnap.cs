using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Pure map-snapping logic: which resource marker a recipe matches, and which
/// matching free marker is nearest a reference point. Coordinates are in canvas
/// units (= metres); markers carry world centimetres, converted with
/// <see cref="CmPerUnit"/>. Kept free of UI types so it can be unit-tested.
/// </summary>
public static class MapSnap
{
    /// <summary>Centimetres per canvas unit (1 unit = 1 metre). Mirrors the map geometry.</summary>
    public const double CmPerUnit = 100.0;

    /// <summary>How close a dropped extractor must be to snap, in canvas units.</summary>
    public const double SnapRadius = 150.0;

    /// <summary>Can this recipe machine sit on that map resource node?</summary>
    public static bool Matches(GameDatabase data, FactoryNode node, ResourceNodeInfo map)
        => data.RecipesByName.TryGetValue(node.Name, out var recipe) && Matches(data, recipe, map);

    /// <summary>Can this recipe sit on that map resource node?</summary>
    public static bool Matches(GameDatabase data, RecipeDefinition recipe, ResourceNodeInfo map)
    {
        var family = data.MultiMachineFor(recipe.Machine)?.Name ?? recipe.Machine;
        return map.Kind switch
        {
            ResourceNodeKind.Geyser => family == "Geothermal Generator",
            ResourceNodeKind.FrackingCore => recipe.Machine == "Resource Well Pressurizer",
            ResourceNodeKind.FrackingSatellite => recipe.Machine == "Resource Well Extractor"
                && recipe.Outputs.Any(o => o.Part == map.Part),
            _ => (family == "Miner" || recipe.Machine == "Oil Extractor")
                && recipe.Outputs.Any(o => o.Part == map.Part),
        };
    }

    /// <summary>
    /// The matching, unoccupied marker nearest <paramref name="referenceX"/>/<paramref name="referenceY"/>
    /// (canvas units) within <see cref="SnapRadius"/>. The extractor's own current marker counts as
    /// available so a small wobble does not unsnap it. Returns null when nothing qualifies.
    /// </summary>
    public static ResourceNodeInfo? NearestCandidate(
        GameDatabase data,
        FactoryNode node,
        IReadOnlyList<ResourceNodeInfo> markers,
        IReadOnlySet<string> occupied,
        double referenceX,
        double referenceY)
    {
        ResourceNodeInfo? best = null;
        var bestDistance = SnapRadius;
        foreach (var candidate in markers)
        {
            if (!Matches(data, node, candidate)) continue;
            if (occupied.Contains(candidate.Instance) && node.ResourceNodeId != candidate.Instance) continue;
            var dx = candidate.X / CmPerUnit - referenceX;
            var dy = candidate.Y / CmPerUnit - referenceY;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// Inverse of <see cref="Matches(GameDatabase, RecipeDefinition, ResourceNodeInfo)"/>: the recipe
    /// to create for a bare marker (ore part → that part's Miner recipe, oil → Oil Extractor,
    /// geyser → Geothermal, fracking core → Pressurizer). Returns null when no recipe fits.
    /// </summary>
    public static string? RecipeForMarker(GameDatabase data, ResourceNodeInfo map)
        => data.Document.Recipes.FirstOrDefault(r => Matches(data, r, map))?.Name;

    /// <summary>The sole output part of a snapped extractor's recipe, if any (extractors have one).</summary>
    public static string? ExtractorOutputPart(GameDatabase data, FactoryNode node)
        => data.RecipesByName.TryGetValue(node.Name, out var recipe)
            ? recipe.Outputs.Select(p => p.Part).FirstOrDefault()
            : null;
}
