using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;

namespace Ficsit.Schematics;

/// <summary>Settings panel handlers, theming, and the help/about dialogs.</summary>
public partial class MainPage
{
    private async void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_initializing || LanguagePicker.SelectedIndex < 0) return;
        var language = _languages[LanguagePicker.SelectedIndex];
        _state.Settings.LanguageId = language.Id;
        _state.Settings.LanguageName = language.Name;
        _state.SaveSettings();
        await _loc.LoadAsync(language.Id);
        PopulateStaticPickers();
        ApplyStrings();
        Chooser.Refresh();
        if (SummaryPanel.IsVisible) Summary.Refresh();
        Canvas.Invalidate();
        UpdateStatus();
    }

    private void OnDarkModeToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.DarkMode = e.Value;
        _state.SaveSettings();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.UserAppTheme = _state.Settings.DarkMode ? AppTheme.Dark : AppTheme.Light;
        _drawable.Theme = _state.Settings.DarkMode ? CanvasTheme.Dark : CanvasTheme.Light;
        Canvas.Invalidate();
    }

    private void OnDragSensitivityChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
        if (int.TryParse(DragSensitivityEntry.Text, out var value) && value is > 0 and <= 200)
        {
            _state.Settings.DragSensitivity = value;
            _state.SaveSettings();
        }
        ApplySettingsControls();
    }

    private void OnSolverChanged(object? sender, EventArgs e)
    {
        if (_initializing || SolverPicker.SelectedIndex < 0) return;
        _state.Editor.Document.Solver = SolverIds[SolverPicker.SelectedIndex];
        _state.Editor.Resolve();
    }

    private void OnPathStyleChanged(object? sender, EventArgs e)
    {
        if (_initializing || PathStylePicker.SelectedIndex < 0) return;
        var path = PathIds[PathStylePicker.SelectedIndex];
        _state.Editor.Document.Path = path;
        _state.Settings.Path = path;
        _state.SaveSettings();
        Canvas.Invalidate();
    }

    private void OnColorWiresToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.WireColorByPart = e.Value;
        _state.SaveSettings();
        Canvas.Invalidate();
    }

    private void OnFocusHighlightToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.FocusHighlight = e.Value;
        _state.SaveSettings();
        Canvas.Invalidate();
    }

    private void OnBeltCapacityWarningsToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.ShowBeltCapacityWarnings = e.Value;
        _state.SaveSettings();
        UpdateStatus();
        Canvas.Invalidate();
    }

    private void OnMultiplierChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
        var doc = _state.Editor.Document;
        if (Rational.TryParse(SpaceElevatorMultEntry.Text?.Trim() ?? "", out _))
            doc.SpaceElevatorMultiplier = SpaceElevatorMultEntry.Text!.Trim();
        if (Rational.TryParse(InputMultEntry.Text?.Trim() ?? "", out _))
            doc.InputMultiplier = InputMultEntry.Text!.Trim();
        if (Rational.TryParse(PowerMultEntry.Text?.Trim() ?? "", out _))
            doc.PowerMultiplier = PowerMultEntry.Text!.Trim();
        ApplyDocumentControls();
        _state.Editor.Resolve();
    }

    private void OnAutosaveToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializing) return;
        _state.Settings.Autosave = e.Value;
        _state.SaveSettings();
        _state.StartAutosave(Dispatcher);
    }

    private void OnAutosaveIntervalChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
        if (int.TryParse(AutosaveIntervalEntry.Text, out var minutes) && minutes is > 0 and <= 240)
        {
            _state.Settings.AutosaveIntervalMinutes = minutes;
            _state.SaveSettings();
            _state.StartAutosave(Dispatcher);
        }
        ApplySettingsControls();
    }

    /// <summary>
    /// Reads a Satisfactory save: its resource nodes (shown on the world map for snapping) and,
    /// optionally, its built machines — placed on the map at their real locations, miners snapped
    /// to their nodes (the "import built factories" feature, Phase 1).
    /// </summary>
    private async void OnImportSaveClicked(object? sender, EventArgs e)
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

            var world = await Task.Run(() => SatisfactorySaveReader.ReadWorld(picked.FullPath));
            _state.ImportMapNodes(world.ResourceNodes);
            _state.Settings.ShowMap = true;
            _state.SaveSettings();
            UpdateMapButton();
            Canvas.Invalidate();

            // Loading a save also imports its unlocked alternate recipes (so the planner
            // matches what the player has actually unlocked).
            var recipeSummary = await ApplyUnlockedRecipesFromSaveAsync(picked.FullPath);

            // Offer to place the save's built machines onto the map (one undoable step), wired up
            // from the save's belt/pipe graph, with parallel manifolds collapsed into counted
            // nodes and the result grouped into outposts by location.
            var (rawNodes, rawConnections) = SaveImport.Build(world, _state.Data);
            var (factories, connections) = SaveConsolidation.Consolidate(rawNodes, rawConnections);
            var placed = 0;
            var outpostCount = 0;
            var wireCount = 0;
            if (factories.Count > 0
                && await DisplayAlertAsync("Import built factories",
                    $"This save has {rawNodes.Count} machine(s) we can place on the map at their real "
                    + $"locations, wired from the belt/pipe connections. Parallel machines (manifolds / "
                    + $"load-balancers) are merged into {factories.Count} counted nodes and grouped into "
                    + "outposts. Add them to the current factory?\n\n"
                    + "Extractors and machines keep their real recipe; vehicle (train/truck/drone) "
                    + "links aren't imported yet.",
                    "Place machines", "Skip"))
            {
                var outposts = SaveClustering.GroupByLocation(factories, _state.Data, SaveClustering.DefaultRadius);
                SaveLayout.ArrangeOutposts(factories, connections, outposts);
                _state.Editor.AddNodes([.. factories, .. outposts], connections);
                placed = factories.Count;
                outpostCount = outposts.Count;
                wireCount = connections.Count;
                _drawable.InvalidateLayouts();
                Canvas.Invalidate();
            }

            var placedLine = placed > 0
                ? $"\nPlaced {placed} node(s) in {outpostCount} outpost(s), {wireCount} connection(s)."
                : "";
            await DisplayAlertAsync("Import save",
                $"Imported {world.ResourceNodes.Count} resource node(s).{placedLine}\n{recipeSummary}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(_loc.L("ERROR"), ex.Message, "OK");
        }
    }

    private async void OnHelpClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(_loc.L("HELP"),
            "Double-click the canvas to add a machine.\n"
            + "Drag from a part icon to a matching one to connect —\n"
            + "or drop it on empty space to pick a matching recipe.\n"
            + "Click a machine's bottom box to set a limit.\n"
            + "Double-click or right-click a machine to edit it.\n"
            + "Right-drag for box select · Del deletes the selection.\n"
            + "Ctrl+Z/Y undo/redo · Ctrl+X/C/V clipboard · Ctrl+A select all\n"
            + "Mouse wheel zooms · drag the background to pan · Ctrl+0 resets.",
            "OK");
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(_loc.L("ABOUT"),
            "Ficsit Schematics 1.0\n\n"
            + "A visual factory calculator for Satisfactory with exact fraction math.\n"
            + "Inspired by Satisfactory Modeler; reads and writes its .sfmd saves.",
            "OK");
    }
}
