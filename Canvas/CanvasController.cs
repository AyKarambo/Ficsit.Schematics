using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Services;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// Pointer state machine for the canvas:
/// left-drag background = pan, left-drag node = move, drag from port = connect
/// (re-drag detaches), right-drag = rubber-band select, double-left on background =
/// recipe chooser, double-left on node = machine editor, right-click = context
/// popups, wheel = zoom around the cursor. Deletion is deliberate only — Delete
/// key or the editor popover, never an accidental double-click.
/// </summary>
public sealed partial class CanvasController(AppState state, FactoryCanvasDrawable drawable)
{
    private enum Mode { Idle, Pressed, Pan, DragNodes, Connect, RubberBand }

    private Mode _mode = Mode.Idle;
    private PointF _pressScreen;
    private PointF _lastScreen;
    private bool _pressWasRight;
    private FactoryNode? _pressNode;
    private PortInfo? _pressPort;
    private NodeConnection? _detachedConnection;

    // Port reorder: active while a port drag stays within its own side's column.
    private bool _reorderActive;
    private int _reorderIndex;

    // Phase 3 "drag out of a resource node": the free marker pressed on empty canvas,
    // and the extractor created from it once the drag starts.
    private ResourceNodeInfo? _pressMarker;
    private FactoryNode? _dragOutNode;
    private DateTime _lastClickTime = DateTime.MinValue;
    private PointF _lastClickPoint;
    private FactoryNode? _lastClickNode;

    public event Action<PointF>? OpenRecipeChooser;        // screen position
    public event Action<PortDragContext, PointF>? OpenChooserForPort;
    public event Action<FactoryNode, PointF>? OpenMachinePopup;
    public event Action<FactoryNode, PortInfo, PointF>? OpenPortMenu;
    public event Action<IReadOnlyList<FactoryNode>, PointF>? OpenSelectionMenu; // right-click on a multi-selection
    public event Action<FactoryNode>? EnterOutpostRequested;
    public event Action<FactoryNode, NodeLayout>? EditLimitRequested;
    public event Action? Invalidate;
    // Press-time close: dismisses the transient panels (chooser, settings, …) but
    // leaves the docked machine editor for the click/selection logic to retarget.
    public event Action? CloseTransientOverlays;

    private float DragThreshold => Math.Max(2, state.Settings.DragSensitivity / 4f);

    /// <summary>True while a pan/drag/connect/rubber-band gesture is in flight. Lets the host
    /// skip the per-move status recompute (full node walk) until the gesture ends.</summary>
    public bool IsInteracting => _mode is Mode.Pan or Mode.DragNodes or Mode.Connect or Mode.RubberBand;

    // ---------------------------------------------------------------- pressed

    public void PointerPressed(PointF screen, bool isRight, bool ctrl)
    {
        CloseTransientOverlays?.Invoke();
        _pressScreen = _lastScreen = screen;
        _pressWasRight = isRight;
        var world = drawable.ScreenToWorld(screen);
        (_pressNode, var layout) = HitNode(world);
        _pressPort = layout?.HitPort(world);
        _detachedConnection = null;
        _pressMarker = null;
        _dragOutNode = null;
        _reorderActive = false;
        _mode = Mode.Pressed;

        // Drag-out-of-a-resource-node: an empty press over a free marker arms the
        // gesture without disturbing pan-on-drag for misses.
        if (!isRight && _pressNode is null && _pressPort is null && drawable.MapActive)
            _pressMarker = FreeMarkerAt(world);

        if (!isRight && _pressPort is { IsInput: true } inputPort && _pressNode is not null)
        {
            // Re-drag an existing single connection to detach it.
            var existing = state.Editor.Graph
                .IncomingTo(_pressNode, inputPort.Part).ToList();
            if (existing.Count == 1)
                _detachedConnection = existing[0];
        }
    }

    public void PointerMoved(PointF screen, bool leftDown, bool rightDown)
    {
        var world = drawable.ScreenToWorld(screen);

        if (_mode == Mode.Pressed)
        {
            var moved = Math.Abs(screen.X - _pressScreen.X) + Math.Abs(screen.Y - _pressScreen.Y);
            if (moved > DragThreshold)
            {
                if (_pressWasRight)
                    _mode = Mode.RubberBand;
                else if (_pressMarker is not null && TryBeginDragOut(_pressMarker))
                    _mode = Mode.Connect;
                else if (_pressPort is not null)
                    _mode = Mode.Connect;
                else if (_pressNode is not null)
                {
                    if (!state.Selection.Contains(_pressNode))
                        state.SetSelection([_pressNode]);
                    _mode = Mode.DragNodes;
                }
                else
                    _mode = Mode.Pan;
            }
        }

        switch (_mode)
        {
            case Mode.Pan:
                drawable.PanX += screen.X - _lastScreen.X;
                drawable.PanY += screen.Y - _lastScreen.Y;
                SyncPanToDocument();
                Invalidate?.Invoke();
                break;

            case Mode.DragNodes:
                var prevWorld = drawable.ScreenToWorld(_lastScreen);
                var dx = world.X - prevWorld.X;
                var dy = world.Y - prevWorld.Y;
                if (dx != 0 || dy != 0)
                {
                    state.Editor.MoveNodes(state.Selection, dx, dy);
                    drawable.InvalidateLayouts();
                }
                UpdateSnapPreview(world);
                Invalidate?.Invoke();
                break;

            case Mode.Connect:
                // Staying within the pressed port's own side reorders it; otherwise it's a
                // connection drag (pending wire).
                if (_dragOutNode is null && TryReorderHint(world, out var insertLine))
                {
                    _reorderActive = true;
                    drawable.PortInsertLine = insertLine;
                    drawable.PendingWire = null;
                }
                else
                {
                    _reorderActive = false;
                    drawable.PortInsertLine = null;
                    var anchorLayout = _pressNode is not null ? GetLayout(_pressNode) : null;
                    var anchor = anchorLayout is not null && _pressPort is not null
                        ? drawable.WorldToScreen(anchorLayout.PortAnchor(_pressPort))
                        : _pressScreen;
                    drawable.PendingWire = (anchor, screen);
                }
                Invalidate?.Invoke();
                break;

            case Mode.RubberBand:
                drawable.RubberBand = new RectF(
                    Math.Min(_pressScreen.X, screen.X),
                    Math.Min(_pressScreen.Y, screen.Y),
                    Math.Abs(screen.X - _pressScreen.X),
                    Math.Abs(screen.Y - _pressScreen.Y));
                Invalidate?.Invoke();
                break;
        }

        _lastScreen = screen;
    }

    public void PointerReleased(PointF screen, bool wasRight, bool ctrl)
    {
        var world = drawable.ScreenToWorld(screen);
        var mode = _mode;
        _mode = Mode.Idle;
        _pressMarker = null;

        switch (mode)
        {
            case Mode.Pressed:
                HandleClick(screen, world, wasRight, ctrl);
                break;

            case Mode.DragNodes:
                drawable.SnapPreviewMarker = null;
                state.Editor.Commands.BreakCoalescing();
                SnapSelectionToGrid();
                SnapToResourceNode(world);
                break;

            case Mode.Connect:
                drawable.PendingWire = null;
                drawable.PortInsertLine = null;
                if (_reorderActive)
                    CommitReorder();
                else if (_dragOutNode is not null)
                    CompleteDragOut(world, screen);
                else
                    CompleteConnection(world, screen);
                _reorderActive = false;
                break;

            case Mode.RubberBand:
                if (drawable.RubberBand is { } band)
                {
                    var rect = new RectF(
                        (band.X - drawable.PanX) / drawable.Zoom,
                        (band.Y - drawable.PanY) / drawable.Zoom,
                        band.Width / drawable.Zoom,
                        band.Height / drawable.Zoom);
                    var hits = drawable.Layouts.Values
                        .Where(l => l.Bounds.IntersectsWith(rect))
                        .Select(l => l.Node);
                    state.SetSelection(ctrl ? state.Selection.Union(hits) : hits);
                }
                drawable.RubberBand = null;
                break;
        }

        Invalidate?.Invoke();
    }

    private void HandleClick(PointF screen, PointF world, bool isRight, bool ctrl)
    {
        var (node, layout) = HitNode(world);
        var isDoubleClick = (DateTime.UtcNow - _lastClickTime).TotalMilliseconds < 450
            && Math.Abs(screen.X - _lastClickPoint.X) < 6
            && Math.Abs(screen.Y - _lastClickPoint.Y) < 6
            && node == _lastClickNode;
        _lastClickTime = isDoubleClick ? DateTime.MinValue : DateTime.UtcNow;
        _lastClickPoint = screen;
        _lastClickNode = node;

        if (isRight)
        {
            // A right-click on a connected port offers "clear connection(s)"; an unconnected
            // port (or the node body) falls through to the machine editor.
            var rightPort = layout?.HitPort(world);
            if (node is not null && rightPort is not null && PortHasConnections(node, rightPort))
            {
                OpenPortMenu?.Invoke(node, rightPort, screen);
                return;
            }
            // Right-clicking a node that's part of a multi-selection offers group
            // actions ("Format selection"); a single node falls through to its editor.
            if (node is not null && state.Selection.Count >= 2 && state.Selection.Contains(node))
            {
                OpenSelectionMenu?.Invoke(state.Selection.ToList(), screen);
                return;
            }
            if (node is not null)
            {
                if (!state.Selection.Contains(node))
                    state.SetSelection([node]);
                OpenMachinePopup?.Invoke(node, screen);
                return;
            }
            var connection = HitConnectionLabel(world);
            if (connection is not null)
            {
                state.Editor.Disconnect(connection);
                drawable.InvalidateLayouts();
                return;
            }
            OpenRecipeChooser?.Invoke(screen);
            return;
        }

        if (isDoubleClick)
        {
            if (node is null)
            {
                OpenRecipeChooser?.Invoke(screen);
            }
            else if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
            {
                state.ClearSelection();
                EnterOutpostRequested?.Invoke(node);
            }
            else
            {
                state.SetSelection([node]);
                OpenMachinePopup?.Invoke(node, screen);
            }
            return;
        }

        if (node is not null)
        {
            if (layout is not null && layout.HasLimitRow && layout.LimitRect.Contains(world))
            {
                EditLimitRequested?.Invoke(node, layout);
                return;
            }
            state.SetSelection(ctrl ? ToggleSelection(node) : [node]);
            return;
        }

        state.ClearSelection();
    }

    private IEnumerable<FactoryNode> ToggleSelection(FactoryNode node)
        => state.Selection.Contains(node)
            ? state.Selection.Where(n => n != node).ToList()
            : state.Selection.Append(node).ToList();

    // -------------------------------------------------------------- cancel

    /// <summary>Esc / cancel during a drag-out removes the just-created extractor as one step.</summary>
    public void Cancel()
    {
        if (_mode == Mode.Connect && _dragOutNode is not null)
        {
            state.Editor.Commands.CancelGroup();
            state.ClearSelection();
            _dragOutNode = null;
        }
        _mode = Mode.Idle;
        _pressMarker = null;
        _reorderActive = false;
        drawable.PendingWire = null;
        drawable.PortInsertLine = null;
        drawable.SnapPreviewMarker = null;
        drawable.RubberBand = null;
        Invalidate?.Invoke();
    }
}
