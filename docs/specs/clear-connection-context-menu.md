# Right-click a port → "Clear connection" (#13)

> **Status: ✅ Implemented.**

## Problem

There is no port-level context menu. Right-click is routed through the controller
(`MainPage.Input.cs` → `CanvasController.PointerPressed/Released` with `isRight=true`) and
handled in `CanvasController.HandleClick` (:206-224):

- right-click on a **node** → opens the machine editor (`OpenMachinePopup`, :208-213);
- right-click on a **connection label** → immediately disconnects
  (`state.Editor.Disconnect`, :215-221);
- right-click on **empty canvas** → recipe chooser (:222).

No `MenuFlyout`/`ContextFlyout` exists anywhere in the app; popups are surfaced via
`Action`-delegate events on the controller (`OpenMachinePopup`, `OpenRecipeChooser`,
`OpenChooserForPort`) wired in `MainPage.xaml.cs:67-72`. Because a port sits inside the
node's expanded hit area, a right-click on a port currently just opens the machine editor —
there is no way to clear a single port's connections without hunting each connection label.

## Decided behavior

- **Right-click on a port** (input or output, detected via `layout.HitPort(world)`) opens a
  small context menu whose item is **"Clear connection(s)"**. Choosing it removes **all**
  connections attached to that exact port — same node, same part, same side — in **one undo
  step**. Use the existing `FactoryEditor.Disconnect` (:161-171) per connection, grouped.
  - Input port: clear `CurrentScope.IncomingTo(node, port.Part)`.
  - Output port: clear `CurrentScope.OutgoingFrom(node, port.Part)`.
- **Right-click on the node body (not a port)** keeps opening the machine editor — unchanged.
- The existing right-click-on-connection-label disconnect stays as-is.
- The menu also makes a natural home for future port actions; keep it to the single item
  for now.

## Recommended model & effort

**Sonnet 4.6, low-medium.** Small and contained. The two points of care: (a) in the
right-click branch, hit-test the **port first** and only fall back to the node-editor branch
when no port is hit; (b) group the multi-`Disconnect` into one undo via the command stack.

## Implementation plan

1. **Controller** — in `HandleClick`'s `isRight` branch (`CanvasController.cs:206-224`),
   before the node branch: if `layout?.HitPort(world)` returns a port, raise a new
   `OpenPortMenu?.Invoke(node, port, screen)` event and return. Add the event next to
   `OpenMachinePopup`.
2. **Clear action** — a controller method `ClearPort(FactoryNode node, PortInfo port)` that
   begins a command group, calls `state.Editor.Disconnect` for each matching connection
   (`IncomingTo`/`OutgoingFrom` by `port.Part`), ends the group, and
   `drawable.InvalidateLayouts(); Invalidate?.Invoke();`. Confirm `FactoryEditor.Commands`
   supports grouping (used by the drag-out gesture, `CanvasController.cs:530/580`); reuse
   that.
3. **Menu UI** — handle `OpenPortMenu` in `MainPage` (mirror `ShowMachinePopup` wiring,
   `MainPage.xaml.cs:67-72`): show a `MenuFlyout` at the pointer (or a minimal themed popup
   consistent with existing overlays) with one item, "Clear connection(s)", invoking
   `_controller.ClearPort(node, port)`. Localize the label via `_loc`.
4. **Verify** — `/run`: right-click an input and an output with multiple wires; confirm only
   that port's wires clear; Ctrl+Z restores them all at once; right-click on the node body
   still opens the editor; check both light/dark themes.

## Open questions

- Disable / hide the item when the port has no connections, or show it greyed? Suggest hide
  it (no menu, or show nothing) when the port is unconnected.
- MAUI `MenuFlyout` vs. a hand-rolled popup: prefer whichever matches the app's existing
  overlay styling; a one-item popup is acceptable if `MenuFlyout` placement at an arbitrary
  pointer point is awkward on the target platform.

## Acceptance criteria

1. Right-clicking an input or output port shows a "Clear connection(s)" menu.
2. Choosing it removes exactly that port's connections (correct part + side) and nothing
   else.
3. One Ctrl+Z restores all cleared connections together; redo re-clears.
4. Right-clicking the node body still opens the machine editor; empty-canvas and
   connection-label right-clicks are unchanged.
5. Works in both themes.
