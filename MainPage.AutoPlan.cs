using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Planning;
using Ficsit.Schematics.ViewModels;

namespace Ficsit.Schematics;

/// <summary>
/// Auto-Plan: the user states what they want (or what they can provide), picks
/// a bias and byproduct policy, and the LP planner synthesizes the factory
/// straight onto the canvas. Parts are chosen via a searchable picker; provided
/// inputs can be locked ("this is all there is") so an undersupplied
/// intermediate scales the whole output down instead of being topped up.
/// </summary>
public partial class MainPage
{
    private readonly List<PlanRowControls> _planTargetRows = [];
    private readonly List<PlanRowControls> _planProvisionRows = [];
    private readonly HashSet<string> _planBanned = [];
    private bool _planPanelInitialized;
    private bool _planRunning;
    private IReadOnlyList<(string Phase, IReadOnlyList<PlanTarget> Bundle)> _planPhases = [];
    private List<RecipeListItem> _allPartItems = [];
    private Action<string>? _partPickerCallback;

    // Resource-preference budget: one normalized slider per extractable raw.
    private readonly List<string> _prefResources = [];
    private readonly List<Slider> _prefSliders = [];
    private readonly List<Label> _prefPercentLabels = [];
    private readonly List<ColumnDefinition> _prefBarColumns = [];
    private bool _prefUpdating;
    private bool _prefBodyOpen;
    private bool _tierSeeding;

    // Background planning job + the draft it produces (held until the user applies).
    private CancellationTokenSource? _planCts;
    private PlanResult? _planDraft;
    private PlanRequest? _planDraftRequest;
    private string? _planDraftLabel;

    /// <summary>Segment colours for the budget bar, aligned to <see cref="ScarcityWeights.WeightedResources"/>.</summary>
    private static readonly string[] PrefColors =
    [
        "#9AA6B0", "#C8B98C", "#6E6E6E", "#CC7A45", "#D9B23A", "#C77FB0",
        "#6E5BA6", "#B0694C", "#5FB0C9", "#D2C24A", "#B05CC9", "#6FAF3F",
    ];

    private void OnAutoPlanClicked(object? sender, EventArgs e)
    {
        var show = !AutoPlanPanel.IsVisible;
        CloseOverlays();
        AutoPlanPanel.IsVisible = show;
        if (show && !_planPanelInitialized) InitializeAutoPlanPanel();
    }

    private void InitializeAutoPlanPanel()
    {
        _planPanelInitialized = true;

        AddPlanRow(PlanTargetsList, _planTargetRows, withLock: false);

        foreach (var resource in ScarcityWeights.Build(null).Keys.OrderBy(k => k))
        {
            var chip = new Button
            {
                Text = _loc.L(resource),
                FontSize = 11,
                HeightRequest = 26,
                Padding = new Thickness(10, 0),
                CornerRadius = 13,
                Margin = new Thickness(0, 0, 4, 4),
            };
            StyleChip(chip, active: false);
            chip.Clicked += (_, _) =>
            {
                if (!_planBanned.Add(resource)) _planBanned.Remove(resource);
                StyleChip(chip, _planBanned.Contains(resource));
            };
            PlanBannedChips.Children.Add(chip);
        }

        BuildPreferenceRows();

        _planPhases = FactoryPlanner.SpaceElevatorPhases(Data);
        PlanPhasePicker.ItemsSource = _planPhases.Select(p => _loc.L(p.Phase)).ToList();

        PlanBiasPicker.ItemsSource = new List<string> { "Resources", "Power", "Machines" };
        PlanBiasPicker.SelectedIndex = 0;
        PlanByproductPicker.ItemsSource = new List<string> { "Eliminate (zero waste)", "Allow sinking" };
        PlanByproductPicker.SelectedIndex = 0;

        _tierSeeding = true;
        var tiers = new List<string> { "All tiers" };
        for (var t = 1; t <= 9; t++) tiers.Add($"Tier {t}");
        PlanMaxTierPicker.ItemsSource = tiers;
        PlanMaxTierPicker.SelectedIndex = TierIndexFromCap(_state.Settings.PlannerMaxTierPhase);
        _tierSeeding = false;
    }

    /// <summary>Picker index 0 = "All tiers" (cap 99); index 1..9 = "Tier N" (cap N).</summary>
    private static int TierIndexFromCap(int cap) => cap is >= 1 and <= 9 ? cap : 0;

    private static int TierCapFromIndex(int index) => index is >= 1 and <= 9 ? index : 99;

    private void OnPlanMaxTierChanged(object? sender, EventArgs e)
    {
        if (!_planPanelInitialized || _tierSeeding) return;
        _state.Settings.PlannerMaxTierPhase = TierCapFromIndex(PlanMaxTierPicker.SelectedIndex);
        _state.SaveSettings();
    }

    private void ApplyAutoPlanStrings()
    {
        ToolTipProperties.SetText(AutoPlanButton, "Auto-Plan a factory");
        AutoPlanTitle.Text = "Auto-Plan";
        PlanTargetsHeader.Text = "TARGETS (ANY PART · RATE/MIN)";
        PlanMaximizeLabel.Text = "Maximize output from provided inputs";
        PlanProvidedHeader.Text = "PROVIDED INPUTS (PART · MAX/MIN)";
        PlanBannedHeader.Text = "EXCLUDED RESOURCES (CLICK TO BAN)";
        PlanBiasLabel.Text = "Optimize for";
        UpdatePreferenceToggleText();
        PlanPreferenceHint.Text = "Drag right to prefer a resource — the planner reaches for it first; "
            + "the others rebalance so the budget stays 100%. Equal = the built-in scarcity defaults.";
        PlanPrefEqualButton.Text = "Equal";
        PlanPrefResetButton.Text = "Reset to default";
        PlanPrefSaveButton.Text = "Save as default";
        PlanByproductLabel.Text = "Byproducts";
        PlanMaxTierLabel.Text = "Available up to";
        PlanRunButton.Text = "Plan factory";
        DraftApplyButton.Text = "Apply to canvas";
        DraftDiscardButton.Text = "Discard";
        DraftInputsHeader.Text = "BASE RESOURCES IN";
        DraftRecipesHeader.Text = "RECIPES USED";
        PlanBusyCancel.Text = "Cancel";
        ApplyRecipeListStrings();
        PartPickerSearch.Placeholder = _loc.L("RECIPE_NAME") + "…";
        ToolTipProperties.SetText(PlanAddTargetButton, "Add target");
        ToolTipProperties.SetText(PlanAddProvisionButton, "Add provided input");
    }

    // ------------------------------------------------------------ row setup

    private void OnPlanAddTarget(object? sender, EventArgs e)
        => AddPlanRow(PlanTargetsList, _planTargetRows, withLock: false);

    private void OnPlanAddProvision(object? sender, EventArgs e)
        => AddPlanRow(PlanProvisionsList, _planProvisionRows, withLock: true);

    private PlanRowControls AddPlanRow(VerticalStackLayout list, List<PlanRowControls> rows, bool withLock)
    {
        var partButton = new Button
        {
            Text = "Choose part…",
            FontSize = 12,
            HeightRequest = 30,
            Padding = new Thickness(8, 0),
            CornerRadius = 7,
            BackgroundColor = IsDark() ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#F0F0F0"),
            TextColor = MutedTextColor(),
            HorizontalOptions = LayoutOptions.Fill,
        };
        var rate = new Entry
        {
            Placeholder = "/min",
            FontSize = 12,
            HeightRequest = 30,
            WidthRequest = 64,
            HorizontalTextAlignment = TextAlignment.End,
            TextColor = RowTextColor(),
            BackgroundColor = IsDark() ? Color.FromArgb("#2C2C2C") : Color.FromArgb("#F0F0F0"),
            PlaceholderColor = MutedTextColor(),
        };
        var remove = MakeIconButton("", "Remove");
        remove.FontSize = 9;
        remove.WidthRequest = 26;
        remove.HeightRequest = 26;

        Button? lockButton = null;
        if (withLock)
        {
            lockButton = MakeIconButton("", "Unlocked: the planner builds extra production when this runs short.");
            lockButton.FontSize = 11;
            lockButton.WidthRequest = 28;
            lockButton.HeightRequest = 26;
        }

        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (withLock) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var column = 0;
        grid.Children.Add(partButton);
        Grid.SetColumn(rate, ++column);
        grid.Children.Add(rate);
        if (lockButton is not null)
        {
            Grid.SetColumn(lockButton, ++column);
            grid.Children.Add(lockButton);
        }
        Grid.SetColumn(remove, ++column);
        grid.Children.Add(remove);

        var row = new PlanRowControls { Row = grid, PartButton = partButton, Rate = rate, LockButton = lockButton };
        rows.Add(row);
        list.Children.Add(grid);

        partButton.Clicked += (_, _) => OpenPartPicker(part =>
        {
            row.Part = part;
            partButton.Text = _loc.L(part);
            partButton.TextColor = RowTextColor();
        });
        if (lockButton is not null)
        {
            lockButton.Clicked += (_, _) =>
            {
                row.Exclusive = !row.Exclusive;
                lockButton.Text = row.Exclusive ? "" : "";
                lockButton.TextColor = row.Exclusive ? CanvasTheme.Accent : RowTextColor();
                ToolTipProperties.SetText(lockButton, row.Exclusive
                    ? "Locked: this is all there is — the output scales down if it runs short."
                    : "Unlocked: the planner builds extra production when this runs short.");
            };
        }
        remove.Clicked += (_, _) =>
        {
            rows.Remove(row);
            list.Children.Remove(grid);
        };
        return row;
    }

    // ----------------------------------------------------------- part picker

    private void OpenPartPicker(Action<string> onPicked)
    {
        if (_allPartItems.Count == 0)
            _allPartItems = Data.Document.Parts
                .Select(p => new RecipeListItem
                {
                    Name = p.Name,
                    DisplayName = _loc.L(p.Name),
                    Icon = _icons.GetSource(p.Name),
                })
                .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        _partPickerCallback = onPicked;
        PartPickerSearch.Text = string.Empty;
        PartPickerList.ItemsSource = _allPartItems;
        PartPickerPanel.IsVisible = true;
        Dispatcher.Dispatch(() => PartPickerSearch.Focus());
    }

    private void OnPartPickerSearch(object? sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim() ?? string.Empty;
        PartPickerList.ItemsSource = query.Length == 0
            ? _allPartItems
            : _allPartItems.Where(i =>
                    i.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void OnPartPicked(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string part) return;
        PartPickerPanel.IsVisible = false;
        var callback = _partPickerCallback;
        _partPickerCallback = null;
        callback?.Invoke(part);
    }

    // -------------------------------------------------------------- presets

    private void OnPlanPhasePicked(object? sender, EventArgs e)
    {
        if (PlanPhasePicker.SelectedIndex < 0 || PlanPhasePicker.SelectedIndex >= _planPhases.Count) return;
        var bundle = _planPhases[PlanPhasePicker.SelectedIndex].Bundle;

        _planTargetRows.Clear();
        PlanTargetsList.Children.Clear();
        foreach (var target in bundle)
        {
            var row = AddPlanRow(PlanTargetsList, _planTargetRows, withLock: false);
            row.Part = target.Part;
            row.PartButton.Text = _loc.L(target.Part);
            row.PartButton.TextColor = RowTextColor();
            row.Rate.Text = target.Rate.ToString();
        }
        PlanMaximizeSwitch.IsToggled = true;
        PlanSummaryLabel.Text = "Phase bundle loaded as ratios — add provided inputs, then plan.";
    }

    // ------------------------------------------------- resource preference

    /// <summary>Builds the budget bar and one normalized slider per extractable raw.</summary>
    private void BuildPreferenceRows()
    {
        _prefResources.Clear();
        _prefResources.AddRange(ScarcityWeights.WeightedResources);
        var n = _prefResources.Count;
        // One resource can claim everything but the others' 1% floor.
        var max = Math.Max(2, 100 - (n - 1));

        for (var i = 0; i < n; i++)
        {
            var color = Color.FromArgb(PrefColors[i % PrefColors.Length]);

            var column = new ColumnDefinition(new GridLength(1, GridUnitType.Star));
            _prefBarColumns.Add(column);
            PlanPreferenceBar.ColumnDefinitions.Add(column);
            var segment = new BoxView { Color = color };
            Grid.SetColumn(segment, i);
            PlanPreferenceBar.Children.Add(segment);

            var name = new Label
            {
                Text = _loc.L(_prefResources[i]),
                FontSize = 11.5,
                TextColor = RowTextColor(),
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center,
            };
            var percent = new Label
            {
                FontSize = 11.5,
                TextColor = MutedTextColor(),
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 38,
            };
            var head = new Grid { ColumnSpacing = 6 };
            head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            head.Children.Add(name);
            Grid.SetColumn(percent, 1);
            head.Children.Add(percent);

            var slider = new Slider
            {
                Minimum = 1,
                Maximum = max,
                MinimumTrackColor = color,
                ThumbColor = color,
            };
            var index = i;
            slider.ValueChanged += (_, _) => OnPrefSliderChanged(index);

            _prefSliders.Add(slider);
            _prefPercentLabels.Add(percent);

            var row = new VerticalStackLayout { Spacing = 0 };
            row.Children.Add(head);
            row.Children.Add(slider);
            PlanPreferenceList.Children.Add(row);
        }

        SeedPreferences(_state.Settings.PlannerResourcePreferences);
    }

    /// <summary>Dragging one slider drains the others proportionally so the budget stays 100%.</summary>
    private void OnPrefSliderChanged(int index)
    {
        if (_prefUpdating) return;
        _prefUpdating = true;
        try
        {
            var n = _prefSliders.Count;
            var dragged = _prefSliders[index].Value;
            var othersOld = 0.0;
            for (var i = 0; i < n; i++)
                if (i != index) othersOld += _prefSliders[i].Value;

            var remaining = Math.Max(0, 100 - dragged);
            for (var i = 0; i < n; i++)
            {
                if (i == index) continue;
                var share = othersOld > 0 ? _prefSliders[i].Value * remaining / othersOld : remaining / (n - 1);
                _prefSliders[i].Value = Math.Clamp(share, _prefSliders[i].Minimum, _prefSliders[i].Maximum);
            }
            UpdatePreferenceReadouts();
        }
        finally { _prefUpdating = false; }
    }

    private void UpdatePreferenceReadouts()
    {
        var n = _prefSliders.Count;
        if (n == 0) return;

        var total = 0.0;
        for (var i = 0; i < n; i++) total += _prefSliders[i].Value;

        // Largest-remainder rounding so the displayed percentages sum to exactly 100%.
        var percent = new int[n];
        var remainder = new double[n];
        var allotted = 0;
        for (var i = 0; i < n; i++)
        {
            var exact = total > 0 ? _prefSliders[i].Value / total * 100 : 0;
            percent[i] = (int)Math.Floor(exact);
            remainder[i] = exact - percent[i];
            allotted += percent[i];
        }
        foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => remainder[i]).Take(Math.Max(0, 100 - allotted)))
            percent[i]++;

        for (var i = 0; i < n; i++)
        {
            _prefPercentLabels[i].Text = $"{percent[i]}%";
            _prefBarColumns[i].Width = new GridLength(Math.Max(0.0001, _prefSliders[i].Value), GridUnitType.Star);
        }
    }

    /// <summary>Loads slider positions from a saved budget; null/empty seeds an equal split.</summary>
    private void SeedPreferences(IReadOnlyDictionary<string, int>? prefs)
    {
        if (_prefSliders.Count == 0) return;
        _prefUpdating = true;
        try
        {
            var n = _prefResources.Count;
            var equal = 100.0 / n;
            for (var i = 0; i < n; i++)
            {
                var value = equal;
                if (prefs is not null && prefs.TryGetValue(_prefResources[i], out var saved) && saved > 0)
                    value = saved;
                _prefSliders[i].Value = Math.Clamp(value, _prefSliders[i].Minimum, _prefSliders[i].Maximum);
            }
            UpdatePreferenceReadouts();
        }
        finally { _prefUpdating = false; }
    }

    /// <summary>
    /// Folds the budget into per-resource cost-weight multipliers. A neutral (equal)
    /// budget contributes nothing, leaving the scarcity defaults untouched; otherwise
    /// multiplier = (1/n) / share = sum / (n · position), exact via <see cref="Rational"/>.
    /// </summary>
    private void ApplyResourcePreferences(PlanRequest request)
    {
        var n = _prefResources.Count;
        if (n == 0) return;

        var positions = new int[n];
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            positions[i] = Math.Max(1, (int)Math.Round(_prefSliders[i].Value));
            sum += positions[i];
        }

        var neutral = true;
        for (var i = 1; i < n; i++)
            if (positions[i] != positions[0]) { neutral = false; break; }
        if (neutral || sum == 0) return;

        for (var i = 0; i < n; i++)
            request.WeightMultipliers[_prefResources[i]] = new Rational(sum, n * positions[i]);
    }

    private void OnPlanPreferenceToggle(object? sender, EventArgs e)
    {
        _prefBodyOpen = !_prefBodyOpen;
        PlanPreferenceBody.IsVisible = _prefBodyOpen;
        UpdatePreferenceToggleText();
    }

    private void UpdatePreferenceToggleText()
        => PlanPreferenceToggle.Text = (_prefBodyOpen ? "▾ " : "▸ ") + "Resource preference";

    private void OnPlanPrefEqual(object? sender, EventArgs e) => SeedPreferences(null);

    private void OnPlanPrefReset(object? sender, EventArgs e)
        => SeedPreferences(_state.Settings.PlannerResourcePreferences);

    private void OnPlanPrefSave(object? sender, EventArgs e)
    {
        var prefs = new Dictionary<string, int>();
        for (var i = 0; i < _prefResources.Count; i++)
            prefs[_prefResources[i]] = (int)Math.Round(_prefSliders[i].Value);
        _state.Settings.PlannerResourcePreferences = prefs;
        _state.SaveSettings();
        PlanSummaryLabel.Text = "Saved this budget as your default resource preference.";
    }

    // ----------------------------------------------------------------- run

    private void OnPlanRunClicked(object? sender, EventArgs e)
    {
        var request = new PlanRequest
        {
            MaximizeFromProvisions = PlanMaximizeSwitch.IsToggled,
            Bias = (PlanBias)Math.Max(0, PlanBiasPicker.SelectedIndex),
            Byproducts = PlanByproductPicker.SelectedIndex == 1 ? ByproductMode.AllowSink : ByproductMode.Eliminate,
        };
        foreach (var banned in _planBanned) request.BannedResources.Add(banned);

        // Map the persisted recipe controls onto the primitive request.
        foreach (var disabled in _state.Settings.PlannerDisabledRecipes)
            request.DisabledRecipes.Add(disabled);
        if (_state.Settings.PlannerExcludeManualParts)
            foreach (var part in Data.Document.Parts)
                if (part.IsManuallyGathered)
                    request.BannedResources.Add(part.Name);
        if (!_state.Settings.PlannerAllowOreConversion)
            foreach (var recipe in FactoryPlanner.OreConversionRecipes(Data))
                request.DisabledRecipes.Add(recipe);
        if (_state.Settings.PlannerMaxTierPhase < 99)
            foreach (var recipe in FactoryPlanner.RecipesAboveTier(Data, _state.Settings.PlannerMaxTierPhase))
                request.DisabledRecipes.Add(recipe);
        ApplyResourcePreferences(request);

        foreach (var row in _planTargetRows)
        {
            if (row.Part is null) continue;
            if (!Rational.TryParse(row.Rate.Text?.Trim() ?? "", out var rate) || !rate.IsPositive)
            { PlanSummaryLabel.Text = $"Enter a positive rate for {_loc.L(row.Part)}."; return; }
            request.Targets.Add(new PlanTarget(row.Part, rate));
        }
        if (request.Targets.Count == 0) { PlanSummaryLabel.Text = "Pick at least one target part."; return; }

        foreach (var row in _planProvisionRows)
        {
            if (row.Part is null) continue;
            if (!Rational.TryParse(row.Rate.Text?.Trim() ?? "", out var max) || !max.IsPositive)
            { PlanSummaryLabel.Text = $"Enter a positive amount for {_loc.L(row.Part)}."; return; }
            request.Provisions.Add(new PlanProvision(row.Part, max, row.Exclusive));
        }
        if (request.MaximizeFromProvisions && request.Provisions.Count == 0)
        { PlanSummaryLabel.Text = "Maximize mode needs at least one provided input."; return; }

        var label = string.Join(", ", request.Targets
            .Select(t => $"{TrimNumber(t.Rate.ToDecimalString(2, RoundingMode.Nearest))} {_loc.L(t.Part)}/min"));
        if (request.MaximizeFromProvisions) label = "Max from inputs · " + label;
        StartPlanning(request, label);
    }

    // ----------------------------------------------- background plan + draft

    /// <summary>
    /// Runs the solve as a cancelable background job so the canvas stays usable.
    /// On success it holds a draft for review (or auto-applies, per settings) — the
    /// canvas is never touched until the user applies.
    /// </summary>
    private async void StartPlanning(PlanRequest request, string label)
    {
        _planCts?.Cancel();
        var cts = new CancellationTokenSource();
        _planCts = cts;
        _planRunning = true;
        PlanRunButton.IsEnabled = false;
        DiscardDraft();
        ShowBusy("Planning");

        var mapNodes = _state.MapNodes;
        var progress = new Progress<PlanProgress>(p => PlanBusyLabel.Text = p.Phase + "…");
        try
        {
            var plan = await Task.Run(() => FactoryPlanner.Plan(Data, request, mapNodes, progress, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            switch (plan.Status)
            {
                case PlanStatus.Infeasible:
                    PlanSummaryLabel.Text = "No factory can satisfy this — check banned resources and provided inputs.";
                    break;
                case PlanStatus.Unbounded:
                    PlanSummaryLabel.Text = "Unbounded — in maximize mode every required raw needs a provided cap.";
                    break;
                default:
                    if (_state.Settings.PlannerAutoApply)
                    {
                        BuildPlanOnCanvas(plan);
                        PlanSummaryLabel.Text = Summarize(plan, request);
                    }
                    else
                    {
                        HoldDraft(plan, request, label);
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            PlanSummaryLabel.Text = "Planning cancelled.";
        }
        catch (Exception ex)
        {
            PlanSummaryLabel.Text = "Planning failed: " + ex.Message;
        }
        finally
        {
            // Only the latest job owns the shared UI; a superseded one bows out.
            if (_planCts == cts)
            {
                _planCts = null;
                _planRunning = false;
                PlanRunButton.IsEnabled = true;
                HideBusy();
                RefreshDraftChip();
            }
        }
    }

    private void OnPlanCancel(object? sender, EventArgs e) => _planCts?.Cancel();

    private void ShowBusy(string phase)
    {
        PlanBusyLabel.Text = phase + "…";
        PlanReadyChip.IsVisible = false;
        PlanBusySpinner.IsRunning = true;
        PlanBusyChip.IsVisible = true;
    }

    private void HideBusy()
    {
        PlanBusyChip.IsVisible = false;
        PlanBusySpinner.IsRunning = false;
    }

    /// <summary>The ready chip shows whenever an unreviewed draft is waiting.</summary>
    private void RefreshDraftChip()
        => PlanReadyChip.IsVisible = _planDraft is not null && !DraftPanel.IsVisible && !_planRunning;

    private void HoldDraft(PlanResult plan, PlanRequest request, string label)
    {
        _planDraft = plan;
        _planDraftRequest = request;
        _planDraftLabel = label;
        PlanReadyButton.Text = "  Plan ready — review  ";
        PlanSummaryLabel.Text = "Plan ready — review it before applying.";
        RefreshDraftChip();
    }

    private void DiscardDraft()
    {
        _planDraft = null;
        _planDraftRequest = null;
        _planDraftLabel = null;
        PlanReadyChip.IsVisible = false;
    }

    private void OnPlanReadyReview(object? sender, EventArgs e)
    {
        if (_planDraft is null) return;
        CloseOverlays();
        RenderDraft(_planDraft, _planDraftRequest, _planDraftLabel ?? "Draft plan");
        DraftPanel.IsVisible = true;
        RefreshDraftChip();
    }

    private void OnDraftApply(object? sender, EventArgs e)
    {
        if (_planDraft is null || _planDraftRequest is null) return;
        var plan = _planDraft;
        var request = _planDraftRequest;
        DiscardDraft();
        DraftPanel.IsVisible = false;
        BuildPlanOnCanvas(plan);
        PlanSummaryLabel.Text = Summarize(plan, request);
    }

    private void OnDraftDiscard(object? sender, EventArgs e)
    {
        DiscardDraft();
        DraftPanel.IsVisible = false;
    }

    /// <summary>Renders the held plan as a scannable summary: totals, raw inputs, recipes.</summary>
    private void RenderDraft(PlanResult plan, PlanRequest? request, string label)
    {
        string N(Rational v) => TrimNumber(v.ToDecimalString(2, RoundingMode.Nearest));

        DraftTitle.Text = "Draft plan";
        DraftSubtitle.Text = label + (plan.AchievedFraction < Rational.One
            ? $"  ·  scaled to {N(plan.AchievedFraction * 100)}%"
            : string.Empty);

        var zeroWaste = plan.Sinks.Count == 0;
        DraftBadge.Text = zeroWaste ? "Zero waste" : "Sinks used";
        DraftBadge.TextColor = zeroWaste ? CanvasTheme.Accent : MutedTextColor();

        DraftStatsLabel.Text = $"{N(plan.TotalMachines)} machines  ·  {N(plan.TotalPowerMW)} MW  ·  {plan.Recipes.Count} recipes";

        DraftInputsList.Children.Clear();
        foreach (var supply in plan.Supplies.OrderByDescending(s => s.Value))
            DraftInputsList.Children.Add(DraftRow(_loc.L(supply.Key), $"{N(supply.Value)} /min", null));

        DraftRecipesList.Children.Clear();
        foreach (var recipe in plan.Recipes.OrderByDescending(r => r.Machines))
        {
            var machine = Data.RecipesByName.TryGetValue(recipe.Recipe, out var def) ? _loc.L(def.Machine) : null;
            DraftRecipesList.Children.Add(DraftRow(_loc.L(recipe.Recipe), $"×{N(recipe.Machines)}", machine));
        }

        DraftByproductsLabel.Text = zeroWaste
            ? "Zero waste — no byproducts sunk."
            : "Sunk: " + string.Join(", ", plan.Sinks.Select(s => $"{_loc.L(s.Key)} {N(s.Value)}"));
        if (request is { MaximizeFromProvisions: true })
            DraftByproductsLabel.Text = $"Bundle multiplier {N(plan.BundleMultiplier)}×/min  ·  " + DraftByproductsLabel.Text;
    }

    /// <summary>One draft line: name (truncating) · optional muted middle · right-aligned value.</summary>
    private Grid DraftRow(string name, string value, string? middle)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var nameLabel = new Label
        {
            Text = name,
            FontSize = 12.5,
            TextColor = RowTextColor(),
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
        };
        grid.Children.Add(nameLabel);

        if (middle is not null)
        {
            var middleLabel = new Label
            {
                Text = middle,
                FontSize = 10.5,
                TextColor = MutedTextColor(),
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(middleLabel, 1);
            grid.Children.Add(middleLabel);
        }

        var valueLabel = new Label
        {
            Text = value,
            FontSize = 12.5,
            TextColor = RowTextColor(),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(valueLabel, 2);
        grid.Children.Add(valueLabel);
        return grid;
    }

    private string Summarize(PlanResult plan, PlanRequest request)
    {
        string N(Rational v) => TrimNumber(v.ToDecimalString(2, RoundingMode.Nearest));
        var lines = new List<string>
        {
            $"{plan.Recipes.Count} recipes · {N(plan.TotalMachines)} machines · {N(plan.TotalPowerMW)} MW",
        };
        if (plan.AchievedFraction < Rational.One)
            lines.Add($"⚠ Output scaled to {N(plan.AchievedFraction * 100)}% — bottleneck: "
                + string.Join(", ", plan.Bottlenecks.Select(_loc.L)));
        else if (request.MaximizeFromProvisions)
        {
            lines.Add($"Bundle multiplier: {N(plan.BundleMultiplier)}×/min");
            if (plan.Bottlenecks.Count > 0)
                lines.Add("Limiting input: " + string.Join(", ", plan.Bottlenecks.Select(_loc.L)));
        }
        if (plan.Supplies.Count > 0)
            lines.Add("In: " + string.Join(", ", plan.Supplies
                .OrderByDescending(s => s.Value)
                .Select(s => $"{_loc.L(s.Key)} {N(s.Value)}")));
        lines.Add(plan.Sinks.Count > 0
            ? "Sunk: " + string.Join(", ", plan.Sinks.Select(s => $"{_loc.L(s.Key)} {N(s.Value)}"))
            : "Zero waste — no byproducts sunk.");
        return string.Join("\n", lines);
    }

    // ------------------------------------------------------- canvas builder

    /// <summary>Materializes the plan: one node per recipe (limit = machine count),
    /// wired part-wise, plus a sink if used. Built in a single suspended, grouped
    /// batch (one solve, one undo step) then laid out with the shared smart layout.</summary>
    private void BuildPlanOnCanvas(PlanResult plan)
    {
        var editor = _state.Editor;
        var defs = plan.Recipes
            .Select(r => (Def: Data.RecipesByName[r.Recipe], r.Machines))
            .ToList();

        // Place to the right of any existing content in the current scope.
        var existing = editor.VisibleNodes.ToList();
        var originX = existing.Count > 0 ? existing.Max(n => n.X) + 400 : (double)_drawable.ScreenToWorld(new PointF(120, 140)).X;
        var originY = existing.Count > 0 ? existing.Min(n => n.Y) : (double)_drawable.ScreenToWorld(new PointF(120, 140)).Y;

        var created = new Dictionary<string, FactoryNode>();
        var placed = new List<FactoryNode>();

        // One suspended, grouped batch: otherwise every AddNode/SetLimit/Connect
        // re-solves the whole graph — hundreds of solves freeze a dense plan.
        using (editor.SuspendSolve())
        {
            editor.Commands.BeginGroup("Apply plan");

            foreach (var (def, machines) in defs)
            {
                var node = editor.AddNode(def.Name, originX, originY);
                editor.SetLimit(node, machines.ToString());
                created[def.Name] = node;
                placed.Add(node);
            }

            FactoryNode? sink = null;
            if (plan.Sinks.Count > 0)
            {
                sink = editor.AddNode("AWESOME Sink", originX, originY);
                placed.Add(sink);
            }

            // Wire every producer of a part to every consumer of it.
            var parts = defs.SelectMany(d => d.Def.Parts.Select(p => p.Part)).Distinct();
            foreach (var part in parts)
            {
                var producers = defs.Where(d => d.Def.RatePerMinute(part).IsPositive).Select(d => d.Def.Name).ToList();
                var consumers = defs.Where(d => d.Def.RatePerMinute(part).IsNegative).Select(d => d.Def.Name).ToList();
                foreach (var producer in producers)
                {
                    foreach (var consumer in consumers)
                        editor.Connect(created[producer], part, created[consumer]);
                    if (sink is not null && plan.Sinks.ContainsKey(part))
                        editor.Connect(created[producer], part, sink);
                }
            }

            editor.Commands.EndGroup();
        }

        // Lay the freshly-wired nodes out with the shared smart layout.
        var arranged = FactoryAutoLayout.Arrange(placed, editor.Graph, originX, originY);
        foreach (var (node, pos) in arranged) { node.X = pos.X; node.Y = pos.Y; }

        // Collapse each key-intermediate sub-chain into a named outpost (reparent only,
        // so flows are untouched) — a dense plan reads as a handful of blocks.
        var selection = placed;
        if (_state.Settings.PlannerAutoCollapse)
        {
            var groups = FactoryAutoGroup.KeyIntermediateGroups(placed, editor.Graph);
            if (groups.Count > 0)
            {
                var outposts = new List<FactoryNode>();
                using (editor.SuspendSolve())
                {
                    editor.Commands.BeginGroup("Group plan");
                    foreach (var (part, groupNodes) in groups)
                        if (editor.GroupIntoOutpost(groupNodes, _loc.L(part)) is { } outpost)
                            outposts.Add(outpost);
                    editor.Commands.EndGroup();
                }
                selection = placed.Where(n => n.Parent == editor.ActiveOutpost).Concat(outposts).ToList();
            }
        }

        _state.SetSelection(selection);
        _drawable.InvalidateLayouts();
        _controller.ZoomToFit(new SizeF((float)Canvas.Width, (float)Canvas.Height));
    }
}
