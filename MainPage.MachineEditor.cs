using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics;

/// <summary>Machine editor popover and the inline limit editor.</summary>
public partial class MainPage
{
    // Docked right-side panel geometry: WidthRequest + outer Margins (12,60,12,12).
    private const float MachinePanelWidth = 300f;
    private const float MachinePanelMargin = 12f;
    private const float MachinePanelTop = 60f;
    // Keep this much empty canvas between a node and the panel's left edge.
    private const float NodeRevealMargin = 24f;

    private void ShowMachinePopup(FactoryNode node, PointF screen)
    {
        var wasOpen = MachinePopup.IsVisible;
        if (!wasOpen) CloseOverlays();
        _popupNode = node;
        PopulateMachinePopup(node);
        MachinePopup.IsVisible = true;
        EnsureNodeClearOfPanel(node);
    }

    /// <summary>
    /// Live retarget: while the docked editor is open, selecting a different single
    /// node rebinds the panel to it (re-populate + re-pan) without closing.
    /// </summary>
    private void RetargetMachinePopup()
    {
        if (!MachinePopup.IsVisible) return;
        // Empty canvas click (or entering an outpost) clears the selection → close.
        if (_state.Selection.Count == 0)
        {
            MachinePopup.IsVisible = false;
            _popupNode = null;
            return;
        }
        if (_state.Selection.Count != 1) return;
        var node = _state.Selection[0];
        if (node == _popupNode) return;
        ShowMachinePopup(node, default);
    }

    /// <summary>
    /// Auto-pan so <paramref name="node"/> is never hidden under the docked editor
    /// panel: if the node's screen rect intersects the panel's screen rect, shift
    /// the view by the minimum delta to bring it fully into the region left of the
    /// panel (plus a small margin). No-op if the node already fits.
    /// </summary>
    private void EnsureNodeClearOfPanel(FactoryNode node)
    {
        if (!_drawable.Layouts.TryGetValue(node, out var layout)) return;

        var pageW = (float)Width;
        var pageH = (float)Height;
        if (pageW <= 0 || pageH <= 0) return;

        // Node's on-screen rect from its world bounds.
        var topLeft = _drawable.WorldToScreen(new PointF(layout.Bounds.Left, layout.Bounds.Top));
        var bottomRight = _drawable.WorldToScreen(new PointF(layout.Bounds.Right, layout.Bounds.Bottom));
        var nodeRect = new RectF(topLeft.X, topLeft.Y,
            bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

        // Panel's on-screen rect (anchored End, full-ish height).
        var panelLeft = pageW - MachinePanelMargin - MachinePanelWidth;
        var panelRect = new RectF(panelLeft, MachinePanelTop,
            MachinePanelWidth + MachinePanelMargin, pageH - MachinePanelTop);

        if (!nodeRect.IntersectsWith(panelRect)) return;

        // Minimum horizontal shift to push the node's right edge left of the panel.
        var dx = panelLeft - NodeRevealMargin - nodeRect.Right;
        // Don't shove the node off the left edge; clamp so its left stays visible.
        if (nodeRect.Left + dx < NodeRevealMargin)
            dx = NodeRevealMargin - nodeRect.Left;

        // Vertical nudge only if the node sits above the panel's top band.
        float dy = 0;
        if (nodeRect.Top < MachinePanelTop)
            dy = MachinePanelTop + NodeRevealMargin - nodeRect.Top;

        _controller.PanBy(dx, dy);
    }

    private void PopulateMachinePopup(FactoryNode node)
    {
        _popupLoading = true;

        PopupIcon.Source = _icons.GetSource(_drawable.MachineImageName(node));
        PopupName.Text = _loc.L(node.Name);
        PopupTitleEntry.Text = node.Title ?? string.Empty;

        var isRecipe = node.Kind == NodeKind.Recipe
            && Data.RecipesByName.TryGetValue(node.Name, out _);

        // Every recipe this machine can run — the fuel selector on generators,
        // a recipe switcher everywhere else.
        PopupRecipeRow.IsVisible = false;
        _popupRecipeSiblings = null;
        if (isRecipe && Data.RecipesByName.TryGetValue(node.Name, out var currentRecipe))
        {
            var siblings = Data.Document.Recipes
                .Where(r => r.Machine == currentRecipe.Machine)
                .ToList();
            if (siblings.Count > 1)
            {
                _popupRecipeSiblings = siblings;
                PopupRecipeRow.IsVisible = true;
                PopupRecipePicker.ItemsSource = siblings.Select(r => _loc.L(r.Name)).ToList();
                PopupRecipePicker.SelectedIndex = Math.Max(0, siblings.FindIndex(r => r.Name == node.Name));
            }
        }

        // Auto-Round swaps the free clock entry for the machine-count stepper.
        PopupClockRow.IsVisible = isRecipe && !node.AutoRound;
        if (PopupClockRow.IsVisible)
            PopupClockEntry.Text = TrimNumber((node.ClockSpeed * 100).ToDecimalString(4, RoundingMode.Nearest));
        PopupAutoClockRow.IsVisible = isRecipe && node.AutoRound;
        RefreshAutoRoundClockRow();

        var machine = MachineFor(node);
        PopupSloopRow.IsVisible = machine is { MaxProductionShards: > 0 };
        if (PopupSloopRow.IsVisible)
            PopupSloopValue.Text = $"{node.Somersloops} / {machine!.MaxProductionShards}";

        PopupCopySetupRow.IsVisible = isRecipe;

        PopupAutoRoundRow.IsVisible = isRecipe;
        PopupAutoRoundSwitch.IsToggled = node.AutoRound;

        PopupPpmRow.IsVisible = isRecipe;
        if (isRecipe)
        {
            var family = FamilyFor(node);
            PopupPpmSwitch.IsToggled = node.ShowPpm ?? family?.ShowPpm ?? false;
        }

        PopupStorageRow.IsVisible = node.Kind == NodeKind.StorageContainer;
        if (PopupStorageRow.IsVisible)
        {
            PopupStoragePicker.ItemsSource = new List<string>
            {
                _loc.L("Partially Full"), _loc.L("Full"), _loc.L("Empty"), _loc.L("Input = Output"),
            };
            PopupStoragePicker.SelectedIndex = Array.IndexOf(StorageModes, node.StorageMode);
        }

        PopupVariantRow.IsVisible = false;
        if (FamilyFor(node)?.Machines is { Count: > 1 } variants)
        {
            PopupVariantRow.IsVisible = true;
            PopupVariantPicker.ItemsSource = variants.Select(v => _loc.L(v.Name)).ToList();
            var current = variants.FindIndex(v => v.Name == node.MachineVariant);
            if (current < 0) current = variants.FindIndex(v => v.Default);
            PopupVariantPicker.SelectedIndex = Math.Max(0, current);
        }

        PopupCapacityRow.IsVisible = false;
        if (CapacityFamilyFor(node)?.Capacities is { Count: > 1 } capacities)
        {
            PopupCapacityRow.IsVisible = true;
            PopupCapacityPicker.ItemsSource = capacities.Select(c => _loc.L(c.Name)).ToList();
            var current = capacities.FindIndex(c => c.Name == node.Capacity);
            if (current < 0) current = capacities.FindIndex(c => c.Default);
            PopupCapacityPicker.SelectedIndex = Math.Max(0, current);
        }

        PopupPasteButton.IsEnabled = _state.Editor.CanPaste;
        _popupLoading = false;
    }

    private void OnPopupRecipeChanged(object? sender, EventArgs e)
    {
        if (_popupLoading || _popupNode is null || PopupRecipePicker.SelectedIndex < 0) return;
        var siblings = _popupRecipeSiblings;
        if (siblings is null || PopupRecipePicker.SelectedIndex >= siblings.Count) return;
        var name = siblings[PopupRecipePicker.SelectedIndex].Name;
        if (name == _popupNode.Name) return;
        _state.Editor.SwitchRecipe(_popupNode, name);
        PopulateMachinePopup(_popupNode);
    }

    private MultiMachineDefinition? FamilyFor(FactoryNode node)
        => node.Kind == NodeKind.Recipe && Data.RecipesByName.TryGetValue(node.Name, out var recipe)
            ? Data.MultiMachineFor(recipe.Machine)
            : null;

    private MultiMachineDefinition? CapacityFamilyFor(FactoryNode node) => node.Kind switch
    {
        NodeKind.AwesomeSink => Data.MultiMachinesByName.GetValueOrDefault("AWESOME Sink"),
        NodeKind.DimensionalDepot => Data.MultiMachinesByName.GetValueOrDefault("Dimensional Depot Uploader"),
        _ => FamilyFor(node),
    };

    private MachineDefinition? MachineFor(FactoryNode node)
    {
        if (node.Kind != NodeKind.Recipe
            || !Data.RecipesByName.TryGetValue(node.Name, out var recipe))
            return null;
        var family = Data.MultiMachineFor(recipe.Machine);
        var machineName = recipe.Machine;
        if (family is { Machines.Count: > 0 })
        {
            var variant = family.Machines.FirstOrDefault(v => v.Name == node.MachineVariant)
                ?? family.Machines.FirstOrDefault(v => v.Default)
                ?? family.Machines[0];
            machineName = variant.Name;
        }
        return Data.MachinesByName.GetValueOrDefault(machineName);
    }

    private static string TrimNumber(string text)
        => text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;

    private void OnPopupCloseClicked(object? sender, EventArgs e)
    {
        MachinePopup.IsVisible = false;
        _popupNode = null;
    }

    // -------------------------------------------------- port context menu

    private FactoryNode? _portMenuNode;
    private PortInfo? _portMenuPort;

    /// <summary>Right-click on a connected port: float a one-item "clear connections" menu.</summary>
    private void ShowPortMenu(FactoryNode node, PortInfo port, PointF screen)
    {
        _portMenuNode = node;
        _portMenuPort = port;
        var pos = ClampToPage(screen, 170, 40);
        PortMenu.TranslationX = pos.X;
        PortMenu.TranslationY = pos.Y;
        PortMenu.IsVisible = true;
    }

    private void OnPortMenuClear(object? sender, EventArgs e)
    {
        PortMenu.IsVisible = false;
        if (_portMenuNode is not null && _portMenuPort is not null)
            _controller.ClearPortConnections(_portMenuNode, _portMenuPort);
        _portMenuNode = null;
        _portMenuPort = null;
    }

    private void ShowSelectionMenu(IReadOnlyList<FactoryNode> nodes, PointF screen)
    {
        var pos = ClampToPage(screen, 170, 76);
        SelectionMenu.TranslationX = pos.X;
        SelectionMenu.TranslationY = pos.Y;
        SelectionMenu.IsVisible = true;
    }

    private void OnSelectionMenuFormat(object? sender, EventArgs e)
    {
        SelectionMenu.IsVisible = false;
        _controller.FormatSelection();
    }

    private void OnSelectionMenuGroup(object? sender, EventArgs e)
    {
        SelectionMenu.IsVisible = false;
        _controller.GroupSelection();
    }

    private void OnPopupTitleCompleted(object? sender, EventArgs e)
    {
        if (_popupNode is null) return;
        var title = string.IsNullOrWhiteSpace(PopupTitleEntry.Text) ? null : PopupTitleEntry.Text.Trim();
        _state.Editor.SetProperty(_popupNode, "Title", n => n.Title, (n, v) => n.Title = v, title);
    }

    private void OnPopupClockCompleted(object? sender, EventArgs e)
    {
        if (_popupNode is null) return;
        if (Rational.TryParse(PopupClockEntry.Text?.Trim() ?? "", out var percent) && percent.IsPositive)
            _state.Editor.SetClockSpeed(_popupNode, percent / 100);
        if (_popupNode is not null)
            PopupClockEntry.Text = TrimNumber((_popupNode.ClockSpeed * 100).ToDecimalString(4, RoundingMode.Nearest));
    }

    // Off-state clock stepper increment, in percentage points.
    private const int ClockStepPercent = 10;

    private void OnClockStepDown(object? sender, EventArgs e) => StepClock(-1);

    private void OnClockStepUp(object? sender, EventArgs e) => StepClock(+1);

    /// <summary>Auto-Round OFF: nudge the clock by a fixed increment, clamped to the game's
    /// (1%, 250%] range. Works regardless of the solved count (including an unconnected node
    /// at count 0), unlike the old whole-machine rounding which no-opped there.</summary>
    private void StepClock(int direction)
    {
        if (_popupNode is null) return;
        var newClock = _popupNode.ClockSpeed + new Rational(direction * ClockStepPercent, 100);
        if (newClock < FactoryNode.MinClockSpeed) newClock = FactoryNode.MinClockSpeed;
        if (newClock > FactoryNode.MaxClockSpeed) newClock = FactoryNode.MaxClockSpeed;
        _state.Editor.SetClockSpeed(_popupNode, newClock);
        PopupClockEntry.Text = TrimNumber((_popupNode.ClockSpeed * 100).ToDecimalString(4, RoundingMode.Nearest));
    }

    // ------------------------------------------- Auto-Round machine stepper

    // "−" rebalances the clock down by adding a machine; "+" up by removing one.
    private void OnAutoRoundStepDown(object? sender, EventArgs e) => StepMachineCount(+1);

    private void OnAutoRoundStepUp(object? sender, EventArgs e) => StepMachineCount(-1);

    private void StepMachineCount(int delta)
    {
        if (_popupNode is null) return;
        // The editor holds the throughput constant and moves a count-display Max alongside the
        // clock so a limited node's machine count actually changes (not its output).
        _state.Editor.StepAutoRound(_popupNode, delta);
    }

    /// <summary>
    /// Exact workload W (machine-equivalents at 100%) and the whole machine count
    /// from the last solve. When the solver rounded, Count is already whole and
    /// W = Count × EffectiveClock; otherwise W = Count × ClockSpeed.
    /// </summary>
    private (Rational Workload, Rational WholeCount) AutoRoundState(FactoryNode node)
    {
        var solved = _state.Editor.Result.For(node);
        if (solved.EffectiveClock is { } effective)
            return (solved.Count * effective, solved.Count);
        return (solved.Count * node.ClockSpeed, new Rational(solved.Count.Ceiling()));
    }

    /// <summary>W = 0, count &lt; 1, or a rebalanced clock outside (1%, 250%] blocks the step.</summary>
    private static bool CanStepTo(Rational workload, Rational target)
    {
        if (!workload.IsPositive || !target.IsPositive) return false;
        var clock = workload / target;
        return clock > FactoryNode.MinClockSpeed && clock <= FactoryNode.MaxClockSpeed;
    }

    /// <summary>Live "N × clock%" display; called on every solve while the popup shows it.</summary>
    private void RefreshAutoRoundClockRow()
    {
        if (!PopupAutoClockRow.IsVisible || _popupNode is null) return;
        var (workload, count) = AutoRoundState(_popupNode);
        PopupAutoClockValue.Text = workload.IsPositive
            ? $"{count} × {TrimNumber((workload / count * 100).ToDecimalString(4, RoundingMode.Nearest))}%"
            : "0";
        // Dim instead of disable (clicks no-op at the bounds): disabled MAUI buttons
        // have WinUI styling quirks — same pattern as UpdateUndoRedo.
        PopupAutoClockDownButton.Opacity = CanStepTo(workload, count + 1) ? 1 : 0.35;
        PopupAutoClockUpButton.Opacity = CanStepTo(workload, count - 1) ? 1 : 0.35;
    }

    private void OnSloopDown(object? sender, EventArgs e) => StepSloop(-1);

    private void OnSloopUp(object? sender, EventArgs e) => StepSloop(+1);

    private void StepSloop(int delta)
    {
        if (_popupNode is null || MachineFor(_popupNode) is not { } machine) return;
        var value = Math.Clamp(_popupNode.Somersloops + delta, 0, machine.MaxProductionShards);
        _state.Editor.SetProperty(_popupNode, "Somersloop", n => n.Somersloops, (n, v) => n.Somersloops = v, value);
        PopupSloopValue.Text = $"{_popupNode.Somersloops} / {machine.MaxProductionShards}";
    }

    // ------------------------------------------------------- copy-clock handlers

    private void OnCopyClockClicked(object? sender, EventArgs e)
    {
        if (_popupNode is null) return;
        var clockText = ClockClipboard.FormatClockPercent(_popupNode.ClockSpeed);
        _ = CopyClockToClipboardAsync(clockText);
    }

    private void OnCopyAutoClockClicked(object? sender, EventArgs e)
    {
        if (_popupNode is null) return;
        var (workload, count) = AutoRoundState(_popupNode);
        if (!workload.IsPositive) return;
        var clockText = ClockClipboard.FormatClockPercent(workload / count);
        _ = CopyClockToClipboardAsync(clockText);
    }

    private async Task CopyClockToClipboardAsync(string clockText)
    {
        await Clipboard.SetTextAsync(clockText + "%");
        ShowCopyToast($"Copied {clockText}%");
    }

    private void OnCopySetupClicked(object? sender, EventArgs e)
    {
        if (_popupNode is null) return;
        var machine = MachineFor(_popupNode);
        var machineName = machine?.Name ?? _popupNode.Name;
        var recipeName = _loc.L(_popupNode.Name);

        string clockText;
        string countStr;
        if (_popupNode.AutoRound)
        {
            var (workload, count) = AutoRoundState(_popupNode);
            clockText = workload.IsPositive
                ? ClockClipboard.FormatClockPercent(workload / count)
                : ClockClipboard.FormatClockPercent(_popupNode.ClockSpeed);
            countStr = workload.IsPositive ? count.Ceiling().ToString() : "?";
        }
        else
        {
            clockText = ClockClipboard.FormatClockPercent(_popupNode.ClockSpeed);
            countStr = "1";
        }

        var sloopPart = machine is { MaxProductionShards: > 0 } && _popupNode.Somersloops > 0
            ? $" · {_popupNode.Somersloops} Somersloop{(_popupNode.Somersloops == 1 ? "" : "s")}"
            : string.Empty;

        var summary = $"{machineName} · {recipeName} · ×{countStr} @ {clockText}%{sloopPart}";
        _ = CopySetupToClipboardAsync(summary);
    }

    private async Task CopySetupToClipboardAsync(string summary)
    {
        await Clipboard.SetTextAsync(summary);
        ShowCopyToast("Copied setup");
    }

    private void ShowCopyToast(string message)
    {
        CopyClockToastLabel.Text = message;
        CopyClockToast.IsVisible = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
            CopyClockToast.IsVisible = false);
    }

    private void OnAutoRoundToggled(object? sender, ToggledEventArgs e)
    {
        if (_popupLoading || _popupNode is null) return;
        _state.Editor.SetProperty(_popupNode, "Auto Round", n => n.AutoRound, (n, v) => n.AutoRound = v, e.Value);
        PopulateMachinePopup(_popupNode); // swap clock entry ↔ machine stepper in place
    }

    private void OnPpmToggled(object? sender, ToggledEventArgs e)
    {
        if (_popupLoading || _popupNode is null) return;
        _state.Editor.SetProperty(_popupNode, "Parts Per Minute", n => n.ShowPpm, (n, v) => n.ShowPpm = v, (bool?)e.Value);
    }

    private void OnStorageModeChanged(object? sender, EventArgs e)
    {
        if (_popupLoading || _popupNode is null || PopupStoragePicker.SelectedIndex < 0) return;
        var mode = StorageModes[PopupStoragePicker.SelectedIndex];
        _state.Editor.SetProperty(_popupNode, "Mode", n => n.StorageMode, (n, v) => n.StorageMode = v, mode);
    }

    private void OnVariantChanged(object? sender, EventArgs e)
    {
        if (_popupLoading || _popupNode is null || PopupVariantPicker.SelectedIndex < 0) return;
        var variants = FamilyFor(_popupNode)?.Machines;
        if (variants is null || PopupVariantPicker.SelectedIndex >= variants.Count) return;
        var name = variants[PopupVariantPicker.SelectedIndex].Name;
        _state.Editor.SetProperty(_popupNode, "Machine", n => n.MachineVariant, (n, v) => n.MachineVariant = v, name);
        PopupIcon.Source = _icons.GetSource(_drawable.MachineImageName(_popupNode));
    }

    private void OnCapacityChanged(object? sender, EventArgs e)
    {
        if (_popupLoading || _popupNode is null || PopupCapacityPicker.SelectedIndex < 0) return;
        var capacities = CapacityFamilyFor(_popupNode)?.Capacities;
        if (capacities is null || PopupCapacityPicker.SelectedIndex >= capacities.Count) return;
        var name = capacities[PopupCapacityPicker.SelectedIndex].Name;
        _state.Editor.SetProperty(_popupNode, "Capacity", n => n.Capacity, (n, v) => n.Capacity = v, name);
    }

    private void OnCutClicked(object? sender, EventArgs e)
    {
        _state.Editor.Cut(SelectionOrPopupNode());
        _state.ClearSelection();
        MachinePopup.IsVisible = false;
    }

    private void OnCopyClicked(object? sender, EventArgs e)
    {
        _state.Editor.Copy(SelectionOrPopupNode());
        MachinePopup.IsVisible = false;
    }

    private void OnPasteClicked(object? sender, EventArgs e)
    {
        // Offset from the edited node when present, else the viewport centre.
        var world = _popupNode is not null && _drawable.Layouts.TryGetValue(_popupNode, out var layout)
            ? new PointF(layout.Bounds.Left, layout.Bounds.Top)
            : _drawable.ScreenToWorld(new PointF((float)Width / 2, (float)Height / 2));
        var pasted = _state.Editor.Paste(world.X + 30, world.Y + 30);
        _state.SetSelection(pasted);
        MachinePopup.IsVisible = false;
    }

    private void OnPopupDeleteClicked(object? sender, EventArgs e)
    {
        var doomed = SelectionOrPopupNode();
        MachinePopup.IsVisible = false;
        _popupNode = null;
        _state.Editor.DeleteNodes(doomed);
        _state.ClearSelection();
    }

    private IReadOnlyList<FactoryNode> SelectionOrPopupNode()
        => _popupNode is not null && !_state.Selection.Contains(_popupNode)
            ? [_popupNode]
            : _state.Selection.ToList();

    // -------------------------------------------------------- limit editor

    private void ShowLimitEditor(FactoryNode node, NodeLayout layout)
    {
        _limitNode = node;
        var topLeft = _drawable.WorldToScreen(new PointF(layout.LimitRect.X, layout.LimitRect.Y));
        LimitEditor.WidthRequest = Math.Max(80, layout.LimitRect.Width * _drawable.Zoom);
        LimitEditor.TranslationX = topLeft.X;
        LimitEditor.TranslationY = topLeft.Y - 4;
        LimitEditor.Text = node.Max ?? string.Empty;
        LimitEditor.IsVisible = true;
        LimitEditor.Focus();
    }

    private void OnLimitEditorCompleted(object? sender, EventArgs e) => CommitLimitEditor();

    private void OnLimitEditorUnfocused(object? sender, FocusEventArgs e) => CommitLimitEditor();

    private void CommitLimitEditor()
    {
        if (_limitNode is null)
        {
            LimitEditor.IsVisible = false;
            return;
        }
        var node = _limitNode;
        _limitNode = null;
        _state.Editor.SetLimit(node, LimitEditor.Text);
        LimitEditor.IsVisible = false;
    }
}
