using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics;

/// <summary>Recipe chooser: opening (double-click and drag-to-add), filtering and placement.</summary>
public partial class MainPage
{
    private void ShowChooserAt(PointF screen)
    {
        CloseOverlays();
        Chooser.ClearPortFilter();
        ChooserFilterRow.IsVisible = false;
        _chooserWorld = _drawable.ScreenToWorld(screen);
        var position = ClampToPage(screen, 470, 440);
        ChooserPanel.TranslationX = position.X;
        ChooserPanel.TranslationY = position.Y;
        Chooser.SearchText = string.Empty;
        ChooserPanel.IsVisible = true;
        Dispatcher.Dispatch(() => ChooserSearch.Focus());
    }

    /// <summary>
    /// A port was dragged onto empty canvas: open the chooser pre-filtered to
    /// compatible recipes; the chosen machine is placed there and wired up.
    /// </summary>
    private void ShowChooserForPort(PortDragContext context, PointF screen)
    {
        ShowChooserAt(screen);
        if (context.Part == "AnyPart") return; // nothing to filter or connect on

        _pendingPortConnect = context;
        Chooser.SetPortFilter(context.Part, forConsumers: context.FromOutput);
        ChooserFilterRow.IsVisible = true;
        ChooserFilterIcon.Source = _icons.GetSource(context.Part);
        ChooserFilterLabel.Text =
            $"{_loc.L(context.FromOutput ? "INPUTS" : "OUTPUTS")}: {_loc.L(context.Part)}";
    }

    private void OnChooserFilterCleared(object? sender, EventArgs e)
    {
        _pendingPortConnect = null;
        Chooser.ClearPortFilter();
        ChooserFilterRow.IsVisible = false;
        Dispatcher.Dispatch(() => ChooserSearch.Focus());
    }

    private void OnChooserSearchCompleted(object? sender, EventArgs e)
    {
        var first = Chooser.Recipes.FirstOrDefault();
        if (first is not null) AddChosenNode(first.Name);
    }

    private void OnRecipeChosen(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string name) AddChosenNode(name);
    }

    private void OnSpecialtyChosen(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string name) AddChosenNode(name);
    }

    private void AddChosenNode(string name)
    {
        var pending = _pendingPortConnect;
        _pendingPortConnect = null;

        var x = (double)_chooserWorld.X;
        var y = (double)_chooserWorld.Y;
        if (pending is not null)
        {
            // Center the card on the drop point, facing the source port.
            y -= NodeLayout.ImageAreaHeight / 2;
            if (!pending.Value.FromOutput) x -= NodeLayout.CardWidth;
        }

        var node = _state.Editor.AddNode(name, x, y);

        if (pending is { } context && context.Part != "AnyPart")
        {
            if (context.FromOutput && NodeAccepts(node, context.Part))
                _state.Editor.Connect(context.Node, context.Part, node);
            else if (!context.FromOutput && NodeProvides(node, context.Part))
                _state.Editor.Connect(node, context.Part, context.Node);
        }

        _state.SetSelection([node]);
        ChooserPanel.IsVisible = false;
        ChooserFilterRow.IsVisible = false;
        Chooser.ClearPortFilter();
    }

    private bool NodeAccepts(FactoryNode node, string part)
        => node.Kind != NodeKind.Recipe
           || (Data.RecipesByName.TryGetValue(node.Name, out var recipe)
               && recipe.Inputs.Any(i => i.Part == part));

    private bool NodeProvides(FactoryNode node, string part) => node.Kind switch
    {
        NodeKind.Recipe => Data.RecipesByName.TryGetValue(node.Name, out var recipe)
            && recipe.Outputs.Any(o => o.Part == part),
        NodeKind.AwesomeSink or NodeKind.DimensionalDepot => false,
        _ => true,
    };

    private void OnMatchNameChipClicked(object? sender, EventArgs e)
    {
        Chooser.MatchRecipeName = !Chooser.MatchRecipeName;
        UpdateChips();
    }

    private void OnMatchInputsChipClicked(object? sender, EventArgs e)
    {
        Chooser.MatchInputs = !Chooser.MatchInputs;
        UpdateChips();
    }

    private void OnMatchOutputsChipClicked(object? sender, EventArgs e)
    {
        Chooser.MatchOutputs = !Chooser.MatchOutputs;
        UpdateChips();
    }

    private void UpdateChips()
    {
        StyleChip(MatchNameChip, Chooser.MatchRecipeName);
        StyleChip(MatchInputsChip, Chooser.MatchInputs);
        StyleChip(MatchOutputsChip, Chooser.MatchOutputs);
    }

    private void StyleChip(Button chip, bool active)
    {
        chip.BackgroundColor = active
            ? CanvasTheme.Accent
            : IsDark() ? Color.FromArgb("#2E2E2E") : Color.FromArgb("#EBEBEB");
        chip.TextColor = active
            ? Color.FromArgb("#1A1208")
            : IsDark() ? Color.FromArgb("#CCCCCC") : Color.FromArgb("#444444");
    }
}
