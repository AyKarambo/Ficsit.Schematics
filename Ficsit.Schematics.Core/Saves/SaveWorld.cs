namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// The built world read from a Satisfactory save: every machine actor plus the resource
/// nodes (so extractors can snap to the node they sit on). UI-free — the canvas
/// materializes it through <see cref="SaveImport"/> + the editor.
/// </summary>
public sealed class SaveWorld
{
    public IReadOnlyList<SaveBuilding> Buildings { get; init; } = [];
    public IReadOnlyList<ResourceNodeInfo> ResourceNodes { get; init; } = [];

    /// <summary>Every <c>mCurrentRecipe</c> recipe-class stem in the save, in serialization
    /// order (e.g. "PackagedWater", "Alternate_BoltedFrame"). Correlated to machines per type by
    /// <see cref="SaveImport"/>; catalog-free here so the reader stays catalog-free.</summary>
    public IReadOnlyList<string> RecipeStems { get; init; } = [];
}
