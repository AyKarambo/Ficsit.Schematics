namespace Ficsit.Schematics;

/// <summary>
/// Native (WinUI) input wiring: pointer events feed the canvas controller,
/// page-level key handling provides the shortcuts, hover drives the tooltip.
/// </summary>
public partial class MainPage
{
    private void HookNativeInput()
    {
#if WINDOWS
        if (_inputHooked) return;

        if (Canvas.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement canvasNative)
        {
            _inputHooked = true;

            canvasNative.PointerPressed += (s, e) =>
            {
                var element = (Microsoft.UI.Xaml.UIElement)s;
                var point = e.GetCurrentPoint(element);
                var pos = new PointF((float)point.Position.X, (float)point.Position.Y);
                element.CapturePointer(e.Pointer);
                _controller.PointerPressed(pos, point.Properties.IsRightButtonPressed, IsCtrlDown());
                _lastPointerScreen = pos;
                e.Handled = true;
            };

            canvasNative.PointerMoved += (s, e) =>
            {
                var element = (Microsoft.UI.Xaml.UIElement)s;
                var point = e.GetCurrentPoint(element);
                var pos = new PointF((float)point.Position.X, (float)point.Position.Y);
                _lastPointerScreen = pos;
                var leftDown = point.Properties.IsLeftButtonPressed;
                var rightDown = point.Properties.IsRightButtonPressed;
                _controller.PointerMoved(pos, leftDown, rightDown);

                if (!leftDown && !rightDown)
                    UpdateHoverTooltip(pos);
                else if (_lastTooltip is not null)
                    UpdateHoverTooltip(null);
            };

            canvasNative.PointerReleased += (s, e) =>
            {
                var element = (Microsoft.UI.Xaml.UIElement)s;
                var point = e.GetCurrentPoint(element);
                var pos = new PointF((float)point.Position.X, (float)point.Position.Y);
                var wasRight = point.Properties.PointerUpdateKind
                    == Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased;
                element.ReleasePointerCaptures();
                _controller.PointerReleased(pos, wasRight, IsCtrlDown());
                e.Handled = true;
            };

            canvasNative.PointerWheelChanged += (s, e) =>
            {
                var element = (Microsoft.UI.Xaml.UIElement)s;
                var point = e.GetCurrentPoint(element);
                var pos = new PointF((float)point.Position.X, (float)point.Position.Y);
                _controller.Wheel(pos, point.Properties.MouseWheelDelta);
                e.Handled = true;
            };

            canvasNative.PointerExited += (_, _) => UpdateHoverTooltip(null);
        }

        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement pageNative)
        {
            pageNative.AddHandler(
                Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                new Microsoft.UI.Xaml.Input.KeyEventHandler(OnNativeKeyDown),
                handledEventsToo: true);
        }
#endif
    }

#if WINDOWS
    private static bool IsCtrlDown()
        => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private bool IsTextInputFocused()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement root || root.XamlRoot is null)
            return false;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot);
        return focused is Microsoft.UI.Xaml.Controls.TextBox;
    }

    private void OnNativeKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                _limitNode = null; // Esc cancels an in-progress limit edit instead of committing it
                CloseOverlays();
            }
            return;
        }

        var ctrl = IsCtrlDown();
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Delete:
            case Windows.System.VirtualKey.Back:
                _controller.DeleteSelection();
                break;
            case Windows.System.VirtualKey.Escape:
                _controller.Cancel();
                HandleEscape();
                break;
            case Windows.System.VirtualKey.Z when ctrl:
                _state.Editor.Commands.Undo();
                break;
            case Windows.System.VirtualKey.Y when ctrl:
                _state.Editor.Commands.Redo();
                break;
            case Windows.System.VirtualKey.A when ctrl:
                _state.SetSelection(_state.Editor.VisibleNodes.ToList());
                break;
            case Windows.System.VirtualKey.C when ctrl:
                _state.Editor.Copy(_state.Selection.ToList());
                break;
            case Windows.System.VirtualKey.X when ctrl:
                _state.Editor.Cut(_state.Selection.ToList());
                _state.ClearSelection();
                break;
            case Windows.System.VirtualKey.V when ctrl:
            {
                var world = _drawable.ScreenToWorld(_lastPointerScreen);
                var pasted = _state.Editor.Paste(world.X, world.Y);
                _state.SetSelection(pasted);
                break;
            }
            case Windows.System.VirtualKey.S when ctrl:
                _state.SaveNow();
                break;
            case Windows.System.VirtualKey.Number0 when ctrl:
            case Windows.System.VirtualKey.NumberPad0 when ctrl:
                OnZoomResetClicked(this, EventArgs.Empty);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
#endif

    private void UpdateHoverTooltip(PointF? screen)
    {
        var text = screen is { } pos ? _controller.TooltipTextAt(pos, _numbers, _loc) : null;
        if (text == _lastTooltip && text is null) return;
        _lastTooltip = text;
        _drawable.Tooltip = text is not null && screen is { } at ? (at, text) : null;
        Canvas.Invalidate();
    }
}
