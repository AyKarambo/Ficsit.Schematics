namespace Ficsit.Schematics.ViewModels;

/// <summary>One row in the recipe chooser (a recipe or a specialty machine).</summary>
public sealed class RecipeListItem
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public ImageSource? Icon { get; init; }
    public IReadOnlyList<ImageSource> OutputIcons { get; init; } = [];
    public IReadOnlyList<ImageSource> InputIcons { get; init; } = [];
}
