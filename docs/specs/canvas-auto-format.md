# Canvas auto-format

> **Status: ✅ Implemented.**

## Problem

The planner lays out a new factory in dependency layers, but once you move nodes
around — or want to tidy a hand-built section — there's no way to re-flow them. A
messy graph is hard to read.

## Decided behavior (discussion 2026-06-14)

- Select nodes (the existing **right-drag rubber-band** already mass-selects), then
  **right-click a selected node → "Format selection"**. The nodes re-flow into
  dependency layers in place.
- Trigger is the **right-click context menu only** (chosen over a toolbar button /
  shortcut). Since right-click on a single node opens the machine editor, the rule
  is: right-clicking a node that belongs to a **multi-selection** (2+) shows the
  group menu instead; a single node keeps opening its editor.
- Format is selection-only (the menu needs 2+ selected). To tidy everything,
  rubber-band the whole area first — there is no separate "Format all" entry.

## Implementation

- Core: `FactoryAutoLayout.Arrange(nodes, FactoryGraph, originX, originY)` — UI-free
  longest-path layering over the **actual graph connections** (only edges within the
  set count, so a selection is arranged in isolation), bounded passes so recycle
  loops terminate. Returns target positions; columns 240 apart, rows 180.
- `CanvasController` — new `OpenSelectionMenu` event; in `HandleClick`'s right-click
  branch, raised when the clicked node is in a 2+ selection (before the single-node
  machine-editor branch). `FormatSelection()` (in `CanvasController.View.cs`) anchors
  at the selection's top-left and applies each node's move as one coalesced,
  undoable group (`MoveNodes` per node, then `BreakCoalescing`).
- `MainPage` — `SelectionMenu` border (mirrors `PortMenu`); `ShowSelectionMenu`
  positions it (`ClampToPage`), `OnSelectionMenuFormat` calls
  `_controller.FormatSelection()`. Added to `CloseOverlays`/`CloseTransientOverlays`.

`BuildPlanOnCanvas` keeps its own layering (it lays out from recipe definitions
*before* the nodes are connected, so it can't share the graph-based pass) — the two
use the same algorithm shape but different adjacency sources.

## Tests

`FactoryAutoLayoutTests`: a chain arranges left-to-right into columns at the origin;
independent nodes stack in one column; a recycle loop terminates and places everyone.

## Acceptance criteria

1. Right-clicking a multi-selection shows "Format selection"; a single-node
   right-click still opens the machine editor.
2. Formatting re-flows the selected nodes into left-to-right dependency layers,
   anchored where they were, as a single undo step.
3. A selection containing a recycle loop formats without hanging.
