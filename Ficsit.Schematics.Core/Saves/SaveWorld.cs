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
}
