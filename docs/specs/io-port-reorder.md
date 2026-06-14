# Drag-to-reorder a node's input/output ports (#12)

> **Status: ✅ Implemented.**

## Problem

A node's ports are positioned in a fixed, derived order, so the user cannot rearrange them
to avoid crossing/overlapping connections:

- `Recipe` nodes: inputs/outputs follow the recipe definition order
  (`NodeLayout.Compute` :104-108, then `PlacePorts` :171-181 lays them top-to-bottom).
- Specialty nodes (outpost, sink, storage, …): ports are the distinct connected parts in
  connection order (`AddDynamicPorts` :189-207).

There is no per-instance ordering and no way to change it. The user wants to drag a port up
or down within its side to reorder, purely for layout/legibility.

## Decided behavior (interview 2026-06-13)

Confirmed: applies to **all node types** with 2+ ports on a side, ordering is stored
**per-node**, and it **persists in the save**.

### Model
Add two ordered overrides to `FactoryNode` (`Ficsit.Schematics.Core/Model/FactoryNode.cs`):
`List<string> InputOrder` and `List<string> OutputOrder`, each a list of part names. Empty =
use the natural/default order (the common case — keep them empty so untouched nodes serialize
clean).

### Layout ordering
In `NodeLayout`, before `PlacePorts`, reorder each side's part list by the override:
parts present in the override come first in override order; any remaining parts (e.g. a new
recipe output, or a newly connected specialty part not yet in the override) keep their
natural relative order and are appended. Apply in **both** the recipe branch
(`NodeLayout.Compute` :127-130) and `AddDynamicPorts` (:206-207). Factor the merge into one
small helper `OrderBy(parts, override)` used by both.

### Gesture (must not regress connect/detach)
Today a press on a port arms a connection drag (`CanvasController.PointerPressed` :67-74 →
`Mode.Connect` :90-91). Reordering shares the same start (press on a port), so disambiguate
on **drop**:
- Add a `Mode.ReorderPort` (or a branch inside the existing connect flow). While dragging a
  port, if the pointer stays within that node's own port column for that side, treat it as a
  **reorder** and show an **insertion indicator** between ports; on release, move the dragged
  part to the insertion slot and write the override.
- If the pointer leaves the column (toward another node / empty canvas), it is a normal
  **connection** drag — fall through to the existing `CompleteConnection` path unchanged.
- Reorder only makes sense with 2+ ports on that side; with 0/1 it is always a connect.

### Persistence & undo
- Serialize `InputOrder`/`OutputOrder` per node in
  `Ficsit.Schematics.Core/Serialization/SfmdSerializer.cs` (write only when non-empty;
  read defaulting to empty for older saves).
- Apply the reorder through `FactoryEditor` as an undoable `EditCommand`, reusing the
  existing `SetProperty` pattern (see `FactoryEditor` and the property edits in
  `CanvasController`, e.g. :452, :466, :473) so one Ctrl+Z reverts a reorder; call
  `drawable.InvalidateLayouts()` after.

## Recommended model & effort

**Opus 4.8, medium-high.** Spans model + serialization + layout + a new pointer gesture
threaded through the existing press/move/release state machine without breaking
connect/detach. The risk is gesture disambiguation; the rest is mechanical. Add unit tests
for the order-merge helper and serialization round-trip; `/run` to feel the drag and confirm
the insertion indicator.

## Implementation plan

1. **Model** — add `InputOrder`/`OutputOrder` (`List<string> = []`) to `FactoryNode`.
2. **Order helper** — `static List<string> Order(List<string> parts, List<string> over)`
   in `NodeLayout`; apply in the recipe branch and `AddDynamicPorts`.
3. **Serialization** — read/write the two lists in `SfmdSerializer` node read/write paths
   (omit when empty); round-trip test.
4. **Editor command** — a `ReorderPort(node, isInput, part, newIndex)` (or generic
   `SetProperty` on the list) producing one undoable command.
5. **Gesture** — in `CanvasController`: on port press, track candidate reorder; in
   `PointerMoved`, if within the same side's column, set a `drawable.PortInsertionHint`
   (node + side + index) and stay in reorder; else behave as connect. In `PointerReleased`,
   commit the reorder or fall through to `CompleteConnection`.
6. **Indicator** — `FactoryCanvasDrawable` draws a short horizontal line / gap at the
   insertion slot while `PortInsertionHint` is set; clear on release/cancel.
7. **Verify** — `/run`: reorder inputs and outputs on a recipe node and on an outpost;
   save, reload, confirm order persists; Ctrl+Z reverts; starting a connection from a port
   still works.

## Open questions

- Should an override be auto-pruned when a recipe no longer has a listed part (e.g. variant
  change)? Suggest yes — drop unknown parts lazily in the order helper (they simply won't
  match), and rewrite the stored list on the next reorder.
- Insertion indicator styling — reuse `CanvasTheme.Accent`; match the pending-wire look.

## Acceptance criteria

1. On a recipe node with ≥2 inputs, dragging an input up/down reorders the inputs; outputs
   reorder independently; the same works on an outpost node.
2. The new order survives save → reload (`.sfmd` round-trip).
3. One Ctrl+Z reverts a reorder; redo re-applies it.
4. Dragging a port toward another node still creates/detaches a connection exactly as
   before (no reorder side effect).
5. An insertion indicator shows where the port will land during a reorder drag.
