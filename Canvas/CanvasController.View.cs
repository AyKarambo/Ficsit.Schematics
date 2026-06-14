using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

public sealed partial class CanvasController
{
    // ------------------------------------------------------------------- misc

    /// <summary>Track the node under the cursor for focus highlighting. Returns true when it
    /// changed (the host should redraw). Cleared while a gesture is in flight.</summary>
    public bool UpdateHover(PointF? screen)
    {
        FactoryNode? node = null;
        if (screen is { } s && !IsInteracting)
            (node, _) = HitNode(drawable.ScreenToWorld(s));
        if (ReferenceEquals(drawable.HoverNode, node)) return false;
        drawable.HoverNode = node;
        return true;
    }

    public void Wheel(PointF screen, int delta)
        => ZoomAround(screen, (float)Math.Pow(1.1, delta / 120.0));

    public void ZoomAround(PointF screen, float factor)
    {
        var newZoom = Math.Clamp(drawable.Zoom * factor, 0.1f, 5f);
        var world = drawable.ScreenToWorld(screen);
        drawable.Zoom = newZoom;
        drawable.PanX = screen.X - world.X * newZoom;
        drawable.PanY = screen.Y - world.Y * newZoom;
        SyncPanToDocument();
        RepinEdgeHandles();
        Invalidate?.Invoke();
    }

    /// <summary>Frame all machines in the viewport (toolbar "fit" and Ctrl+0).</summary>
    public void ZoomToFit(SizeF viewport)
    {
        var layouts = drawable.Layouts.Values.ToList();
        if (layouts.Count == 0 || viewport.Width <= 0 || viewport.Height <= 0)
        {
            drawable.Zoom = 1f;
            drawable.PanX = viewport.Width / 2;
            drawable.PanY = viewport.Height / 2;
        }
        else
        {
            var bounds = layouts[0].Bounds;
            foreach (var layout in layouts.Skip(1))
                bounds = bounds.Union(layout.Bounds);
            bounds = bounds.Inflate(60, 60);
            var zoom = Math.Clamp(Math.Min(viewport.Width / bounds.Width, viewport.Height / bounds.Height), 0.1f, 1.5f);
            drawable.Zoom = zoom;
            drawable.PanX = viewport.Width / 2 - bounds.Center.X * zoom;
            drawable.PanY = viewport.Height / 2 - bounds.Center.Y * zoom;
        }
        SyncPanToDocument();
        Invalidate?.Invoke();
    }

    /// <summary>
    /// Tidy the selected nodes into dependency layers in place (anchored at the
    /// selection's top-left), as one undoable move. Right-click "Format selection".
    /// </summary>
    public void FormatSelection()
    {
        var nodes = state.Selection.ToList();
        if (nodes.Count < 2) return;

        var originX = nodes.Min(n => n.X);
        var originY = nodes.Min(n => n.Y);
        var targets = FactoryAutoLayout.Arrange(nodes, state.Editor.Graph, originX, originY);

        // One undo step; suspended so the grouped moves don't each re-solve.
        using (state.Editor.SuspendSolve())
        {
            state.Editor.Commands.BeginGroup("Format selection");
            foreach (var node in nodes)
                if (targets.TryGetValue(node, out var pos))
                    state.Editor.MoveNodes([node], pos.X - node.X, pos.Y - node.Y, coalesce: false);
            state.Editor.Commands.EndGroup();
        }

        drawable.InvalidateLayouts();
        Invalidate?.Invoke();
    }

    /// <summary>Collapse the selected machines into a new outpost (right-click "Group into outpost").</summary>
    public void GroupSelection()
    {
        if (state.Selection.Count < 2) return;
        var outpost = state.Editor.GroupIntoOutpost(state.Selection.ToList(), null);
        if (outpost is not null) state.SetSelection([outpost]);
        drawable.InvalidateLayouts();
        Invalidate?.Invoke();
    }

    /// <summary>Delete the current selection (Delete key / editor popover).</summary>
    public void DeleteSelection()
    {
        if (state.Selection.Count == 0) return;
        state.Editor.DeleteNodes(state.Selection.ToList());
        state.ClearSelection();
        drawable.InvalidateLayouts();
        Invalidate?.Invoke();
    }

    /// <summary>
    /// Shift the view by a screen-space delta and persist it. Used by the docked
    /// machine editor to nudge a node out from under the panel.
    /// </summary>
    public void PanBy(float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;
        drawable.PanX += dx;
        drawable.PanY += dy;
        SyncPanToDocument();
        RepinEdgeHandles();
        Invalidate?.Invoke();
    }

    /// <summary>Outpost boundary handles are pinned to the canvas edge, so their world position
    /// is derived from pan/zoom — re-lay them out whenever the view moves inside an outpost.</summary>
    private void RepinEdgeHandles()
    {
        if (state.Editor.ActiveOutpost is not null) drawable.InvalidateLayouts();
    }

    private void SyncPanToDocument()
    {
        // Outposts remember their own view; the root view lives on the document.
        if (state.Editor.ActiveOutpost is { } outpost)
        {
            outpost.InnerZoom = drawable.Zoom;
            outpost.InnerPanX = drawable.PanX;
            outpost.InnerPanY = drawable.PanY;
        }
        else
        {
            var doc = state.Editor.Document;
            doc.Zoom = drawable.Zoom;
            doc.PanX = drawable.PanX;
            doc.PanY = drawable.PanY;
        }
    }

    private void SnapSelectionToGrid()
    {
        if (!state.Settings.UseBuildingGrid) return;
        if (!double.TryParse(state.Settings.BuildingGridX, out var gx) || gx <= 0) return;
        if (!double.TryParse(state.Settings.BuildingGridY, out var gy) || gy <= 0) return;
        foreach (var node in state.Selection)
        {
            var snappedX = Math.Round(node.X / gx) * gx;
            var snappedY = Math.Round(node.Y / gy) * gy;
            state.Editor.MoveNodes([node], snappedX - node.X, snappedY - node.Y, coalesce: true);
        }
        state.Editor.Commands.BreakCoalescing();
        drawable.InvalidateLayouts();
    }
}
