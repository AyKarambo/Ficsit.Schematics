using Ficsit.Schematics.Canvas;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics;

/// <summary>Recipe chooser: opening (double-click and drag-to-add), filtering and placement.</summary>
public partial class MainPage
{
    private const float ChooserWidth = 470f;
    private const float ChooserHeight = 440f;

    private void ShowChooserAt(PointF screen, FactoryNode? avoidNode = null)
    {
        CloseOverlays();
        Chooser.ClearPortFilter();
        ChooserFilterRow.IsVisible = false;
        _chooserWorld = _drawable.ScreenToWorld(screen);
        var position = PlaceChooserClear(screen, avoidNode);
        ChooserPanel.TranslationX = position.X;
        ChooserPanel.TranslationY = position.Y;
        Chooser.SearchText = string.Empty;
        ChooserPanel.IsVisible = true;
        Dispatcher.Dispatch(() => ChooserSearch.Focus());
    }

    /// <summary>
    /// Clamp the chooser to the page and, when it opens from a port drag, keep it
    /// from covering the drag's origin node: if the clamped rect overlaps that
    /// node, try offering the chooser on the node's left / right / above / below,
    /// picking the first on-page candidate that leaves the node fully visible.
    /// </summary>
    private PointF PlaceChooserClear(PointF screen, FactoryNode? avoidNode)
    {
        var position = ClampToPage(screen, ChooserWidth, ChooserHeight);
        if (avoidNode is null || !_drawable.Layouts.TryGetValue(avoidNode, out var layout))
            return position;

        var tl = _drawable.WorldToScreen(new PointF(layout.Bounds.Left, layout.Bounds.Top));
        var br = _drawable.WorldToScreen(new PointF(layout.Bounds.Right, layout.Bounds.Bottom));
        var nodeRect = new RectF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);

        if (!ChooserRect(position).IntersectsWith(nodeRect)) return position;

        const float gap = 16f;
        // Candidate top-left corners around the node, in preference order.
        Span<PointF> candidates =
        [
            new(nodeRect.Left - gap - ChooserWidth, screen.Y), // left of node
            new(nodeRect.Right + gap, screen.Y),               // right of node
            new(screen.X, nodeRect.Bottom + gap),              // below node
            new(screen.X, nodeRect.Top - gap - ChooserHeight), // above node
        ];
        foreach (var candidate in candidates)
        {
            var clamped = ClampToPage(candidate, ChooserWidth, ChooserHeight);
            if (!ChooserRect(clamped).IntersectsWith(nodeRect))
                return clamped;
        }
        return position; // no clear spot (node fills the view) — fall back.
    }

    private static RectF ChooserRect(PointF topLeft)
        => new(topLeft.X, topLeft.Y, ChooserWidth, ChooserHeight);

    /// <summary>
    /// A port was dragged onto empty canvas: open the chooser pre-filtered to
    /// compatible recipes; the chosen machine is placed there and wired up.
    /// </summary>
    private void ShowChooserForPort(PortDragContext context, PointF screen)
    {
        ShowChooserAt(screen, context.Node); // keep the drag-origin node visible
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
