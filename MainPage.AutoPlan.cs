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

        _planPhases = FactoryPlanner.SpaceElevatorPhases(Data);
        PlanPhasePicker.ItemsSource = _planPhases.Select(p => _loc.L(p.Phase)).ToList();

        PlanBiasPicker.ItemsSource = new List<string> { "Resources", "Power", "Machines" };
        PlanBiasPicker.SelectedIndex = 0;
        PlanByproductPicker.ItemsSource = new List<string> { "Eliminate (zero waste)", "Allow sinking" };
        PlanByproductPicker.SelectedIndex = 0;
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
        PlanByproductLabel.Text = "Byproducts";
        PlanAlternatesLabel.Text = "Use alternate recipes";
        PlanRunButton.Text = "Plan factory";
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

    // ----------------------------------------------------------------- run

    private async void OnPlanRunClicked(object? sender, EventArgs e)
    {
        if (_planRunning) return;
        var request = new PlanRequest
        {
            MaximizeFromProvisions = PlanMaximizeSwitch.IsToggled,
            Bias = (PlanBias)Math.Max(0, PlanBiasPicker.SelectedIndex),
            Byproducts = PlanByproductPicker.SelectedIndex == 1 ? ByproductMode.AllowSink : ByproductMode.Eliminate,
            UseAlternateRecipes = PlanAlternatesSwitch.IsToggled,
        };
        foreach (var banned in _planBanned) request.BannedResources.Add(banned);

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

        _planRunning = true;
        PlanRunButton.IsEnabled = false;
        PlanSummaryLabel.Text = "Planning… deep chains can take a minute.";
        var mapNodes = _state.MapNodes;
        try
        {
            var plan = await Task.Run(() => FactoryPlanner.Plan(Data, request, mapNodes));
            switch (plan.Status)
            {
                case PlanStatus.Infeasible:
                    PlanSummaryLabel.Text = "No factory can satisfy this — check banned resources and provided inputs.";
                    break;
                case PlanStatus.Unbounded:
                    PlanSummaryLabel.Text = "Unbounded — in maximize mode every required raw needs a provided cap.";
                    break;
                default:
                    BuildPlanOnCanvas(plan);
                    PlanSummaryLabel.Text = Summarize(plan, request);
                    break;
            }
        }
        catch (Exception ex)
        {
            PlanSummaryLabel.Text = "Planning failed: " + ex.Message;
        }
        finally
        {
            _planRunning = false;
            PlanRunButton.IsEnabled = true;
        }
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

    /// <summary>Materializes the plan: nodes per recipe (limit = machine count),
    /// connected part-wise, laid out in dependency layers, plus a sink if used.</summary>
    private void BuildPlanOnCanvas(PlanResult plan)
    {
        var editor = _state.Editor;
        var defs = plan.Recipes
            .Select(r => (Def: Data.RecipesByName[r.Recipe], r.Machines))
            .ToList();

        // Dependency layers (longest path; bounded passes tolerate recycle loops).
        var producersOf = new Dictionary<string, List<string>>();
        foreach (var (def, _) in defs)
            foreach (var output in def.Outputs)
            {
                if (!producersOf.TryGetValue(output.Part, out var list))
                    producersOf[output.Part] = list = [];
                list.Add(def.Name);
            }
        var layer = defs.ToDictionary(d => d.Def.Name, _ => 0);
        for (var pass = 0; pass < defs.Count; pass++)
        {
            var changed = false;
            foreach (var (def, _) in defs)
                foreach (var input in def.Inputs)
                    foreach (var producer in producersOf.GetValueOrDefault(input.Part) ?? [])
                    {
                        if (producer == def.Name) continue;
                        var lift = Math.Min(layer[producer] + 1, defs.Count);
                        if (lift > layer[def.Name]) { layer[def.Name] = lift; changed = true; }
                    }
            if (!changed) break;
        }

        // Place to the right of any existing content.
        var scope = editor.CurrentScope;
        var originX = scope.Nodes.Count > 0 ? scope.Nodes.Max(n => n.X) + 400 : (double)_drawable.ScreenToWorld(new PointF(120, 140)).X;
        var originY = scope.Nodes.Count > 0 ? scope.Nodes.Min(n => n.Y) : (double)_drawable.ScreenToWorld(new PointF(120, 140)).Y;

        var created = new Dictionary<string, FactoryNode>();
        foreach (var column in layer.GroupBy(l => l.Value).OrderBy(g => g.Key))
        {
            var y = originY;
            foreach (var name in column.Select(c => c.Key).OrderBy(n => n))
            {
                var node = editor.AddNode(name, originX + column.Key * 240, y);
                var machines = plan.Recipes.First(r => r.Recipe == name).Machines;
                editor.SetLimit(node, machines.ToString());
                created[name] = node;
                y += 180;
            }
        }

        FactoryNode? sink = null;
        if (plan.Sinks.Count > 0)
        {
            var lastLayer = layer.Count > 0 ? layer.Values.Max() + 1 : 1;
            sink = editor.AddNode("AWESOME Sink", originX + lastLayer * 240, originY);
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

        _state.SetSelection(created.Values);
        _drawable.InvalidateLayouts();
        _controller.ZoomToFit(new SizeF((float)Canvas.Width, (float)Canvas.Height));
    }
}
