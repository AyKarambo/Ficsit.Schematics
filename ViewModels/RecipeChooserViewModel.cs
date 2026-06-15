using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.ViewModels;

/// <summary>
/// Search state of the recipe chooser: text + the three match toggles
/// (recipe name / inputs / outputs), filtering the full recipe list in data order.
/// </summary>
public sealed partial class RecipeChooserViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IconStore _icons;
    private readonly LocalizationService _loc;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MatchRecipeName { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchInputs { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchOutputs { get; set; } = true;

    public ObservableCollection<RecipeListItem> Recipes { get; } = [];
    public ObservableCollection<RecipeListItem> Specialty { get; } = [];

    /// <summary>Active port filter from a drag-to-empty gesture; null = no filter.</summary>
    public string? PortFilterPart { get; private set; }

    /// <summary>True: only recipes consuming the part (drag came from an output). False: only producers.</summary>
    public bool PortFilterForConsumers { get; private set; }

    private static readonly string[] SpecialtyNames =
    [
        "Outpost", "Blueprint", "Splurger", "Priority Splitter", "Priority Merger",
        "Priority Splurger", "AWESOME Sink", "Storage Container", "Dimensional Depot",
        // Unified fuel generators: one entry each, accepts any of its fuels (the per-fuel
        // generator recipes are hidden from the recipe list below).
        "Fuel-Powered Generator", "Coal-Powered Generator", "Nuclear Power Plant", "Biomass Burner",
    ];

    public RecipeChooserViewModel(AppState state, IconStore icons, LocalizationService loc)
    {
        _state = state;
        _icons = icons;
        _loc = loc;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnMatchRecipeNameChanged(bool value) => Refresh();
    partial void OnMatchInputsChanged(bool value) => Refresh();
    partial void OnMatchOutputsChanged(bool value) => Refresh();

    public void SetPortFilter(string part, bool forConsumers)
    {
        PortFilterPart = part;
        PortFilterForConsumers = forConsumers;
        Refresh();
    }

    public void ClearPortFilter()
    {
        if (PortFilterPart is null) return;
        PortFilterPart = null;
        Refresh();
    }

    public void Refresh()
    {
        // Rebuilt (not cached) so display names follow the active language.
        Specialty.Clear();
        foreach (var name in SpecialtyNames)
        {
            var isGenerator = _state.Data.GeneratorMachines.Contains(name);
            if (PortFilterPart is { } specPart)
            {
                // Hunting for a producer of the part: sinks and generators don't produce it.
                if (!PortFilterForConsumers && (isGenerator || name is "AWESOME Sink" or "Dimensional Depot"))
                    continue;
                // Hunting for a consumer: a generator must actually burn (or take) the part.
                if (PortFilterForConsumers && isGenerator && !GeneratorAccepts(name, specPart))
                    continue;
            }
            Specialty.Add(new RecipeListItem
            {
                Name = name,
                DisplayName = _loc.L(name),
                Icon = _icons.GetSource(name),
            });
        }

        Recipes.Clear();
        var query = SearchText.Trim();
        foreach (var recipe in _state.Data.Document.Recipes)
        {
            // Per-fuel generator recipes are surfaced as one unified generator in Specialty.
            if (_state.Data.GeneratorMachines.Contains(recipe.Machine)) continue;
            if (PortFilterPart is { } filterPart)
            {
                var compatible = PortFilterForConsumers
                    ? recipe.Inputs.Any(p => p.Part == filterPart)
                    : recipe.Outputs.Any(p => p.Part == filterPart);
                if (!compatible) continue;
            }
            if (!Matches(recipe, query)) continue;
            var iconPart = recipe.Outputs.FirstOrDefault()?.Part ?? recipe.Parts.FirstOrDefault()?.Part;
            // OfType drops parts with no icon, keeping the list non-null (the binding
            // target is IReadOnlyList<ImageSource>); a missing icon would render blank anyway.
            var outputIcons = recipe.Outputs
                .Select(p => _icons.GetSource(p.Part))
                .OfType<ImageSource>()
                .ToList();
            var inputIcons = recipe.Inputs
                .Select(p => _icons.GetSource(p.Part))
                .OfType<ImageSource>()
                .ToList();
            Recipes.Add(new RecipeListItem
            {
                Name = recipe.Name,
                DisplayName = _loc.L(recipe.Name),
                Icon = iconPart is not null ? _icons.GetSource(iconPart) : null,
                OutputIcons = outputIcons.AsReadOnly(),
                InputIcons = inputIcons.AsReadOnly(),
            });
        }
    }

    /// <summary>True when a unified generator machine burns or otherwise takes <paramref name="part"/>.</summary>
    private bool GeneratorAccepts(string machine, string part)
        => _state.Data.Document.Recipes.Any(r => r.Machine == machine && r.Inputs.Any(i => i.Part == part));

    private bool Matches(RecipeDefinition recipe, string query)
    {
        if (query.Length == 0) return true;
        bool Contains(string text)
            => text.Contains(query, StringComparison.OrdinalIgnoreCase)
               || _loc.L(text).Contains(query, StringComparison.OrdinalIgnoreCase);

        if (MatchRecipeName && Contains(recipe.Name)) return true;
        if (MatchInputs && recipe.Inputs.Any(p => Contains(p.Part))) return true;
        if (MatchOutputs && recipe.Outputs.Any(p => Contains(p.Part))) return true;
        return false;
    }
}
