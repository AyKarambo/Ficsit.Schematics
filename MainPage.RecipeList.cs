using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Planning;
using Ficsit.Schematics.Core.Saves;

namespace Ficsit.Schematics;

/// <summary>
/// The per-recipe enable/disable list (#5). One shared, persisted state
/// (<see cref="Core.Model.AppSettings.PlannerDisabledRecipes"/>, the *disabled*
/// set so new data-update recipes default on) with two entry points: the
/// Auto-Plan panel and Settings. Rows are grouped by primary output part and
/// reuse the chooser row's visual style; a checkbox toggles each recipe.
/// </summary>
public partial class MainPage
{
    private readonly List<(RecipeDefinition Recipe, CheckBox Check, Grid Row, string Group)> _recipeListRows = [];
    private Border? _recipeListReturnPanel;
    private bool _recipeListBuilt;

    private HashSet<string> DisabledRecipes => _disabledRecipesCache ??=
        new HashSet<string>(_state.Settings.PlannerDisabledRecipes);
    private HashSet<string>? _disabledRecipesCache;

    private void ApplyRecipeListStrings()
    {
        RecipeListTitle.Text = "Recipes";
        RecipeListSearch.Placeholder = _loc.L("RECIPE_NAME") + "…";
        RecipeListAllOnButton.Text = "All on";
        RecipeListAlternatesOffButton.Text = "Alternates off";
        RecipeListFromSaveButton.Text = "From save";
        ToolTipProperties.SetText(RecipeListBackButton, "Back");

        PlanExcludeManualLabel.Text = "Exclude hand-gathered parts";
        PlanOreConversionLabel.Text = "Allow ore conversion";
        PlanAutoApplyLabel.Text = "Apply plans without review";
        PlanAutoCollapseLabel.Text = "Group plans into outposts";
        PlanRecipesButton.Text = "Recipes…";

        SettingsPlannerHeader.Text = "AUTO-PLANNER";
        SettingsExcludeManualLabel.Text = "Exclude hand-gathered parts";
        SettingsOreConversionLabel.Text = "Allow ore conversion";
        SettingsRecipesButton.Text = "Recipes…";
    }

    /// <summary>Pushes the two toggles into the planner switches (both entry points).</summary>
    private void ApplyPlannerToggleControls()
    {
        var wasInitializing = _initializing;
        _initializing = true;
        PlanExcludeManualSwitch.IsToggled = _state.Settings.PlannerExcludeManualParts;
        PlanOreConversionSwitch.IsToggled = _state.Settings.PlannerAllowOreConversion;
        SettingsExcludeManualSwitch.IsToggled = _state.Settings.PlannerExcludeManualParts;
        SettingsOreConversionSwitch.IsToggled = _state.Settings.PlannerAllowOreConversion;
        PlanAutoApplySwitch.IsToggled = _state.Settings.PlannerAutoApply;
        PlanAutoCollapseSwitch.IsToggled = _state.Settings.PlannerAutoCollapse;
        _initializing = wasInitializing;
    }

    private void OnPlanAutoApplyToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.PlannerAutoApply = e.Value;
        _state.SaveSettings();
    }

    private void OnPlanAutoCollapseToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.PlannerAutoCollapse = e.Value;
        _state.SaveSettings();
    }

    private void OnPlanExcludeManualToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.PlannerExcludeManualParts = e.Value;
        _state.SaveSettings();
        ApplyPlannerToggleControls();
    }

    private void OnPlanOreConversionToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.PlannerAllowOreConversion = e.Value;
        _state.SaveSettings();
        ApplyPlannerToggleControls();
    }

    // ------------------------------------------------------------ list view

    private void OnPlanRecipesClicked(object? sender, EventArgs e)
    {
        // Remember which panel to restore (Auto-Plan vs Settings) on Back.
        _recipeListReturnPanel = AutoPlanPanel.IsVisible ? AutoPlanPanel
            : SettingsPanel.IsVisible ? SettingsPanel
            : null;
        CloseOverlays();
        EnsureRecipeListBuilt();
        SyncRecipeListChecks();
        RecipeListSearch.Text = string.Empty;
        RecipeListPanel.IsVisible = true;
    }

    private void OnRecipeListBack(object? sender, EventArgs e)
    {
        RecipeListPanel.IsVisible = false;
        if (_recipeListReturnPanel is { } panel) panel.IsVisible = true;
        _recipeListReturnPanel = null;
    }

    private void EnsureRecipeListBuilt()
    {
        if (_recipeListBuilt) return;
        _recipeListBuilt = true;

        // Group by machine (the new grouping) using the icon of the primary output part.
        foreach (var recipe in Data.Document.Recipes)
        {
            if (!recipe.Inputs.Any() || !recipe.Outputs.Any()) continue;
            if (recipe.Machine == "Space Elevator" || recipe.Ficsmas) continue;
            var iconPart = recipe.Outputs.First().Part;
            var (check, row) = BuildRecipeRow(recipe, iconPart);
            // Store the machine name as the group key (was: primary output part).
            _recipeListRows.Add((recipe, check, row, recipe.Machine));
        }
        RenderRecipeList(string.Empty);
    }

    private (CheckBox Check, Grid Row) BuildRecipeRow(RecipeDefinition recipe, string group)
    {
        var check = new CheckBox { VerticalOptions = LayoutOptions.Center };
        check.CheckedChanged += (_, _) => OnRecipeChecked(recipe.Name, check.IsChecked);

        var icon = new Image
        {
            Source = _icons.GetSource(group),
            WidthRequest = 20,
            HeightRequest = 20,
            VerticalOptions = LayoutOptions.Center,
        };
        var label = new Label
        {
            Text = _loc.L(recipe.Name) + (recipe.Alternate ? "  (alt)" : ""),
            FontSize = 11.5,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
            TextColor = recipe.Alternate ? CanvasTheme.Accent : RowTextColor(),
        };

        var grid = new Grid { ColumnSpacing = 6, Padding = new Thickness(2, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(24));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(check);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        Grid.SetColumn(label, 2);
        grid.Children.Add(label);
        return (check, grid);
    }

    private void OnRecipeChecked(string recipeName, bool isChecked)
    {
        if (_syncingRecipeChecks) return;
        if (isChecked) DisabledRecipes.Remove(recipeName);
        else DisabledRecipes.Add(recipeName);
        PersistDisabledRecipes();
    }

    private void PersistDisabledRecipes()
    {
        _state.Settings.PlannerDisabledRecipes = DisabledRecipes.OrderBy(n => n).ToList();
        _state.SaveSettings();
    }

    /// <summary>Reflects the persisted disabled set onto every checkbox without
    /// firing the CheckedChanged write-back.</summary>
    private void SyncRecipeListChecks()
    {
        _syncingRecipeChecks = true;
        foreach (var (recipe, check, _, _) in _recipeListRows)
            check.IsChecked = !DisabledRecipes.Contains(recipe.Name);
        _syncingRecipeChecks = false;
    }

    private bool _syncingRecipeChecks;

    private void OnRecipeListSearch(object? sender, TextChangedEventArgs e)
        => RenderRecipeList(e.NewTextValue?.Trim() ?? string.Empty);

    private void RenderRecipeList(string query)
    {
        RecipeListItems.Children.Clear();
        string? lastMachine = null;
        foreach (var (recipe, _, row, machine) in _recipeListRows)
        {
            if (query.Length > 0 && !RecipeMatches(recipe, machine, query)) continue;
            if (machine != lastMachine)
            {
                RecipeListItems.Children.Add(new Label
                {
                    // Show localized machine name as the section header.
                    Text = _loc.L(machine),
                    Style = (Style)Resources["SectionHeader"],
                    Margin = new Thickness(0, 6, 0, 0),
                });
                lastMachine = machine;
            }
            RecipeListItems.Children.Add(row);
        }
    }

    private bool RecipeMatches(RecipeDefinition recipe, string machine, string query)
    {
        bool Contains(string text)
            => text.Contains(query, StringComparison.OrdinalIgnoreCase)
               || _loc.L(text).Contains(query, StringComparison.OrdinalIgnoreCase);
        return Contains(recipe.Name) || Contains(machine);
    }

    // --------------------------------------------------------- bulk actions

    private void OnRecipeListAllOn(object? sender, EventArgs e)
    {
        DisabledRecipes.Clear();
        PersistDisabledRecipes();
        SyncRecipeListChecks();
    }

    private void OnRecipeListAlternatesOff(object? sender, EventArgs e)
    {
        foreach (var (recipe, _, _, _) in _recipeListRows)
            if (recipe.Alternate)
                DisabledRecipes.Add(recipe.Name);
        PersistDisabledRecipes();
        SyncRecipeListChecks();
    }

    /// <summary>
    /// "From save": read the selected save's purchased schematics and enable exactly the
    /// alternate recipes it has unlocked (standard recipes stay on). Stems that can't be
    /// matched to a catalog recipe are reported so the user can toggle them by hand. See
    /// docs/specs/from-save-spike.md.
    /// </summary>
    private async void OnRecipeListFromSave(object? sender, EventArgs e)
    {
        try
        {
            var savType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".sav"],
            });
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Satisfactory save (.sav)",
                FileTypes = savType,
            });
            if (picked is null) return;

            var stems = await Task.Run(() => SatisfactorySaveReader.ReadUnlockedAlternateSchematics(picked.FullPath));
            var (unlocked, unrecognized) = SchematicRecipeMap.Match(Data, stems);

            // Standard recipes stay on; disable every alternate the save has NOT unlocked.
            DisabledRecipes.Clear();
            foreach (var recipe in Data.Document.Recipes)
                if (recipe.Alternate && !unlocked.Contains(recipe.Name))
                    DisabledRecipes.Add(recipe.Name);
            PersistDisabledRecipes();
            SyncRecipeListChecks();

            var message = $"Enabled {unlocked.Count} unlocked alternate recipe(s) from the save; standard recipes stay on.";
            if (unrecognized.Count > 0)
                message += $"\n{unrecognized.Count} unlocked schematic(s) couldn't be matched to a recipe — toggle those by hand if needed.";
            await DisplayAlertAsync("From save", message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("From save", "Couldn't read that save: " + ex.Message, "OK");
        }
    }
}
