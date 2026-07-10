namespace Ficsit.Schematics.ViewModels;

/// <summary>
/// One category group in the grouped part-picker <c>CollectionView</c>.
/// </summary>
public sealed class PartPickerGroup(string header) : List<RecipeListItem>
{
    public string Header { get; } = header;
}
