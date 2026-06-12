using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Services;
using Ficsit.Schematics.ViewModels;

namespace Ficsit.Schematics;

/// <summary>
/// The canvas page. Construction, startup, localization, editor events and the
/// toolbar live here; the chooser, machine editor, settings, saves and native
/// input each have their own partial (MainPage.*.cs).
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly AppState _state;
    private readonly IconStore _icons;
    private readonly LocalizationService _loc;
    private readonly NumberFormatService _numbers;
    private readonly FactoryCanvasDrawable _drawable;
    private readonly CanvasController _controller;

    public RecipeChooserViewModel Chooser { get; }
    public SummaryViewModel Summary { get; }

    private bool _initializing = true;
    private bool _inputHooked;
    private bool _popupLoading;
    private IReadOnlyList<(string Id, string Name)> _languages = [];
    private PointF _chooserWorld;
    private PointF _lastPointerScreen;
    private FactoryNode? _popupNode;
    private List<RecipeDefinition>? _popupRecipeSiblings;
    private FactoryNode? _limitNode;
    private string? _lastTooltip;
    private IDispatcherTimer? _saveDebounce;
    private PortDragContext? _pendingPortConnect;

    private static readonly string[] SolverIds = ["None", "Manual", "Basic"];
    private static readonly string[] PathIds = ["Curves", "Direct", "2D"];
    private static readonly StorageMode[] StorageModes =
        [StorageMode.PartiallyFull, StorageMode.Full, StorageMode.Empty, StorageMode.InputEqualsOutput];

    public MainPage(AppState state, IconStore icons, LocalizationService loc,
        NumberFormatService numbers, RecipeChooserViewModel chooser, SummaryViewModel summary)
    {
        _state = state;
        _icons = icons;
        _loc = loc;
        _numbers = numbers;
        Chooser = chooser;
        Summary = summary;

        InitializeComponent();
        BindingContext = this;

        _drawable = new FactoryCanvasDrawable(state, icons, numbers);
        _controller = new CanvasController(state, _drawable);
        Canvas.Drawable = _drawable;

        _controller.Invalidate += () =>
        {
            Canvas.Invalidate();
            UpdateStatus();
        };
        _controller.OpenRecipeChooser += ShowChooserAt;
        _controller.OpenChooserForPort += ShowChooserForPort;
        _controller.OpenMachinePopup += ShowMachinePopup;
        _controller.EnterOutpostRequested += node => _state.Editor.EnterOutpost(node);
        _controller.EditLimitRequested += ShowLimitEditor;
        _controller.CloseOverlays += CloseOverlays;

        _state.Editor.Solved += OnSolved;
        _state.Editor.DocumentReplaced += OnDocumentReplaced;
        _state.SelectionChanged += () =>
        {
            if (SummaryPanel.IsVisible) Summary.Refresh();
            Canvas.Invalidate();
        };
        _state.MapNodesChanged += () =>
        {
            UpdateMapNodesInfo();
            Canvas.Invalidate();
        };

        ApplyTheme();
        ApplyViewFromScope();
        Loaded += OnPageLoaded;
    }

    private GameDatabase Data => _state.Data;

    // ------------------------------------------------------------ startup

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        HookNativeInput();
        if (_initializing)
            _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _loc.LoadAsync(_state.Settings.LanguageId);
        _languages = await _loc.ListLanguagesAsync();
        PopulateStaticPickers();
        ApplyStrings();
        ApplySettingsControls();
        Chooser.Refresh();
        UpdateUndoRedo();
        UpdateStatus();
        _state.StartAutosave(Dispatcher);
        _initializing = false;
    }

    private void PopulateStaticPickers()
    {
        var wasInitializing = _initializing;
        _initializing = true;

        LanguagePicker.ItemsSource = _languages.Select(l => l.Name).ToList();
        var langIndex = _languages.ToList().FindIndex(l => l.Id == _state.Settings.LanguageId);
        LanguagePicker.SelectedIndex = Math.Max(0, langIndex);

        SolverPicker.ItemsSource = new List<string>
        {
            _loc.L("NONE_SOLVER"), _loc.L("MANUAL_SOLVER"), _loc.L("BASIC_SOLVER"),
        };
        PathStylePicker.ItemsSource = new List<string>
        {
            _loc.L("CURVES"), _loc.L("DIRECT"), _loc.L("TWO_D"),
        };
        ApplyDocumentControls();

        _initializing = wasInitializing;
    }

    /// <summary>Controls that mirror the open document (solver, path style, multipliers).</summary>
    private void ApplyDocumentControls()
    {
        var wasInitializing = _initializing;
        _initializing = true;

        var doc = _state.Editor.Document;
        SolverPicker.SelectedIndex = Math.Max(0, Array.IndexOf(SolverIds, doc.Solver));
        PathStylePicker.SelectedIndex = Math.Max(0, Array.IndexOf(PathIds, doc.Path));
        SpaceElevatorMultEntry.Text = doc.SpaceElevatorMultiplier;
        InputMultEntry.Text = doc.InputMultiplier;
        PowerMultEntry.Text = doc.PowerMultiplier;

        _initializing = wasInitializing;
    }

    private void ApplySettingsControls()
    {
        var wasInitializing = _initializing;
        _initializing = true;

        DarkModeSwitch.IsToggled = _state.Settings.DarkMode;
        DragSensitivityEntry.Text = _state.Settings.DragSensitivity.ToString();
        AutosaveSwitch.IsToggled = _state.Settings.Autosave;
        AutosaveIntervalEntry.Text = _state.Settings.AutosaveIntervalMinutes.ToString();

        _initializing = wasInitializing;
    }

    private void ApplyStrings()
    {
        ToolTipProperties.SetText(UndoButton, _loc.L("UNDO"));
        ToolTipProperties.SetText(RedoButton, _loc.L("REDO"));
        ToolTipProperties.SetText(ZoomOutButton, "Zoom out (Ctrl+wheel)");
        ToolTipProperties.SetText(ZoomInButton, "Zoom in");
        ToolTipProperties.SetText(ZoomResetButton, "Reset zoom (Ctrl+0)");
        ToolTipProperties.SetText(ZoomFitButton, "Zoom to fit");
        ToolTipProperties.SetText(SummaryButton, _loc.L("SUMMARY"));
        ToolTipProperties.SetText(ImportButton, _loc.L("IMPORT_FILE"));
        ToolTipProperties.SetText(ExportButton, _loc.L("EXPORT"));
        ToolTipProperties.SetText(SavesButton, _loc.L("SAVES"));
        ToolTipProperties.SetText(SettingsButton, _loc.L("SETTINGS"));

        SummaryTitle.Text = _loc.L("SUMMARY");
        SummaryPowerHeader.Text = _loc.L("POWER").ToUpperInvariant();
        SummaryOverclockHeader.Text = _loc.L("OVERCLOCK").ToUpperInvariant();
        SummaryOutputHeader.Text = _loc.L("OUTPUT").ToUpperInvariant();
        SummaryInputHeader.Text = _loc.L("INPUT").ToUpperInvariant();

        ChooserSearch.Placeholder = _loc.L("RECIPE_NAME") + "…";
        ChooserMatchLabel.Text = string.Empty;
        MatchNameChip.Text = _loc.L("RECIPE_NAME");
        MatchInputsChip.Text = _loc.L("INPUTS");
        MatchOutputsChip.Text = _loc.L("OUTPUTS");
        UpdateChips();

        PopupTitleEntry.Placeholder = _loc.L("TITLE");
        PopupRecipeLabel.Text = _loc.L("RECIPE_NAME");
        PopupClockLabel.Text = _loc.L("CLOCKSPEED");
        PopupSloopLabel.Text = _loc.L("Somersloop");
        PopupAutoRoundLabel.Text = _loc.L("AUTO_ROUND");
        PopupPpmLabel.Text = _loc.L("PPM");
        PopupStorageLabel.Text = _loc.L("INTO_STORAGE");
        PopupVariantLabel.Text = _loc.L("MACHINE");
        PopupCapacityLabel.Text = _loc.L("TIER");
        ToolTipProperties.SetText(PopupCutButton, _loc.L("CUT"));
        ToolTipProperties.SetText(PopupCopyButton, _loc.L("COPY"));
        ToolTipProperties.SetText(PopupPasteButton, _loc.L("PASTE"));
        ToolTipProperties.SetText(PopupDeleteButton, _loc.L("DELETE"));

        SettingsTitle.Text = _loc.L("SETTINGS");
        SettingsGeneralHeader.Text = _loc.L("GENERAL").ToUpperInvariant();
        LanguageLabel.Text = _loc.L("LANGUAGE");
        DarkModeLabel.Text = _loc.L("DARK_MODE");
        DragSensitivityLabel.Text = _loc.L("DRAG_SENSITIVITY");
        CalculatorLabel.Text = _loc.L("CALCULATOR");
        SettingsStyleHeader.Text = _loc.L("STYLE").ToUpperInvariant();
        PathStyleLabel.Text = _loc.L("CONNECTION_STYLE");
        SettingsMultipliersHeader.Text = _loc.L("CALCULATOR").ToUpperInvariant();
        SpaceElevatorMultLabel.Text = _loc.L("SPACE_ELEVATOR_MULTIPLIER");
        InputMultLabel.Text = _loc.L("INPUT_MULTIPLIER");
        PowerMultLabel.Text = _loc.L("POWER_MULTIPLIER");
        SettingsAutosaveHeader.Text = _loc.L("AUTO_SAVE").ToUpperInvariant();
        AutosaveLabel.Text = _loc.L("AUTO_SAVE");
        AutosaveIntervalLabel.Text = _loc.L("AUTO_SAVE_INTERVAL");
        HelpButton.Text = _loc.L("HELP");
        AboutButton.Text = _loc.L("ABOUT");

        SavesTitle.Text = _loc.L("SAVES");
        SaveNameEntry.Placeholder = _loc.L("TITLE");
        SaveAsButton.Text = _loc.L("SAVE");
        BackupsHeader.Text = _loc.L("AUTO_SAVE").ToUpperInvariant();

        ToolTipProperties.SetText(MapButton, "World map");
        SettingsMapHeader.Text = "MAP";
        ImportSaveButton.Text = "Import resource nodes from game save (.sav)…";
        UpdateMapButton();
        UpdateMapNodesInfo();

        UpdateScopePill();
    }

    // ------------------------------------------------------- editor events

    private void OnSolved()
    {
        _drawable.InvalidateLayouts();
        Canvas.Invalidate();
        UpdateUndoRedo();
        UpdateStatus();
        if (SummaryPanel.IsVisible) Summary.Refresh();
        QueueSave();
    }

    /// <summary>
    /// Persist shortly after the last edit instead of only on exit or the
    /// autosave interval — a crash can no longer lose more than ~2 seconds.
    /// </summary>
    private void QueueSave()
    {
        if (_saveDebounce is null)
        {
            _saveDebounce = Dispatcher.CreateTimer();
            _saveDebounce.Interval = TimeSpan.FromSeconds(2);
            _saveDebounce.IsRepeating = false;
            _saveDebounce.Tick += (_, _) => _state.SaveNow();
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void OnDocumentReplaced()
    {
        _state.ClearSelection();
        _drawable.InvalidateLayouts();
        ApplyViewFromScope();
        ApplyDocumentControls();
        UpdateScopePill();
        Canvas.Invalidate();
        UpdateStatus();
    }

    private void ApplyViewFromScope()
    {
        var editor = _state.Editor;
        if (editor.ScopePath.Count > 0)
        {
            var outpost = editor.ScopePath[^1];
            _drawable.Zoom = (float)outpost.InnerZoom;
            _drawable.PanX = (float)outpost.InnerPanX;
            _drawable.PanY = (float)outpost.InnerPanY;
        }
        else
        {
            var doc = editor.Document;
            _drawable.Zoom = (float)doc.Zoom;
            _drawable.PanX = (float)doc.PanX;
            _drawable.PanY = (float)doc.PanY;
        }
    }

    private void UpdateScopePill()
    {
        var path = _state.Editor.ScopePath;
        ScopePill.IsVisible = path.Count > 0;
        if (path.Count > 0)
            ScopeLabel.Text = string.Join("  /  ",
                path.Select(n => string.IsNullOrWhiteSpace(n.Title) ? _loc.L(n.Name) : n.Title));
    }

    private void UpdateUndoRedo()
    {
        // Dim instead of disable: disabled MAUI buttons vanish from the WinUI
        // toolbar, and CommandStack.Undo/Redo already no-op when empty.
        UndoButton.Opacity = _state.Editor.Commands.CanUndo ? 1 : 0.35;
        RedoButton.Opacity = _state.Editor.Commands.CanRedo ? 1 : 0.35;
    }

    private void UpdateStatus()
    {
        ZoomResetButton.Text = $"{Math.Round(_drawable.Zoom * 100)}%";

        var net = Rational.Zero;
        var machines = 0;
        foreach (var node in _state.Editor.Document.Root.AllNodes())
        {
            if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint) continue;
            machines++;
            net += _state.Editor.Result.For(node).Power;
        }
        StatusLabel.Text =
            $"{machines} {_loc.L("MACHINES")}   ·   {_numbers.Summary(net)} MW   ·   {_loc.L(_state.Editor.Document.Solver.ToUpperInvariant() + "_SOLVER")}";
    }

    // --------------------------------------------------------- top toolbar

    private void OnUndoClicked(object? sender, EventArgs e) => _state.Editor.Commands.Undo();

    private void OnRedoClicked(object? sender, EventArgs e) => _state.Editor.Commands.Redo();

    private PointF ViewportCenter() => new((float)(Canvas.Width / 2), (float)(Canvas.Height / 2));

    private void OnZoomOutClicked(object? sender, EventArgs e) => _controller.ZoomAround(ViewportCenter(), 1 / 1.25f);

    private void OnZoomInClicked(object? sender, EventArgs e) => _controller.ZoomAround(ViewportCenter(), 1.25f);

    private void OnZoomResetClicked(object? sender, EventArgs e)
        => _controller.ZoomAround(ViewportCenter(), 1f / Math.Max(0.001f, _drawable.Zoom));

    private void OnZoomFitClicked(object? sender, EventArgs e)
        => _controller.ZoomToFit(new SizeF((float)Canvas.Width, (float)Canvas.Height));

    private void OnSummaryToggleClicked(object? sender, EventArgs e)
    {
        SummaryPanel.IsVisible = !SummaryPanel.IsVisible;
        if (SummaryPanel.IsVisible) Summary.Refresh();
    }

    private void OnMapToggleClicked(object? sender, EventArgs e)
    {
        _state.Settings.ShowMap = !_state.Settings.ShowMap;
        _state.SaveSettings();
        UpdateMapButton();
        Canvas.Invalidate();
    }

    private void UpdateMapButton()
        => MapButton.TextColor = _state.Settings.ShowMap
            ? CanvasTheme.Accent
            : (Color)(IsDark() ? Color.FromArgb("#E6E6E6") : Color.FromArgb("#333333"));

    private void UpdateMapNodesInfo()
        => MapNodesInfo.Text = _state.MapNodes.Count > 0
            ? $"{_state.MapNodes.Count} resource nodes loaded"
            : "No game save imported yet";

    private void OnScopeBackClicked(object? sender, EventArgs e) => _state.Editor.LeaveOutpost();

    private void OnSavesClicked(object? sender, EventArgs e)
    {
        var show = !SavesPanel.IsVisible;
        CloseOverlays();
        SavesPanel.IsVisible = show;
        if (show) RefreshSavesPanel();
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        var show = !SettingsPanel.IsVisible;
        CloseOverlays();
        SettingsPanel.IsVisible = show;
    }

    private void CloseOverlays()
    {
        ChooserPanel.IsVisible = false;
        MachinePopup.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SavesPanel.IsVisible = false;
        CommitLimitEditor();
        _popupNode = null;
        _pendingPortConnect = null;
    }

    // ------------------------------------------------------------- helpers

    private PointF ClampToPage(PointF wanted, float width, float height)
    {
        var maxX = Math.Max(8, (float)Width - width - 8);
        var maxY = Math.Max(8, (float)Height - height - 8);
        return new PointF(Math.Clamp(wanted.X, 8, maxX), Math.Clamp(wanted.Y, 8, maxY));
    }

    private void HandleEscape()
    {
        if (ChooserPanel.IsVisible || MachinePopup.IsVisible
            || SettingsPanel.IsVisible || SavesPanel.IsVisible || LimitEditor.IsVisible)
        {
            CloseOverlays();
        }
        else if (_state.Selection.Count > 0)
        {
            _state.ClearSelection();
        }
        else if (_state.Editor.ScopePath.Count > 0)
        {
            _state.Editor.LeaveOutpost();
        }
    }

    private bool IsDark() => Application.Current?.RequestedTheme != AppTheme.Light;

    private Color RowTextColor() => IsDark() ? Color.FromArgb("#F2F2F2") : Color.FromArgb("#1A1A1A");

    private Color MutedTextColor() => IsDark() ? Color.FromArgb("#8C8C8C") : Color.FromArgb("#8A8A8A");
}
