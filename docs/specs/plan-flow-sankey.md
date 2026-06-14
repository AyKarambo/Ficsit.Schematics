# Plan — Sankey flow view

> **Status: ✅ Implemented.** `PlanFlows` (Core) + a `SankeyDrawable`, behind a List⇄Flow toggle in the draft panel.

## Problem

The draft review ([planner-draft-review.md](planner-draft-review.md)) presents a plan as
two lists (raw inputs, recipes used). That's scannable but flat — you can't *see* which
flows dominate or where a bottleneck sits. Every leading community planner (SCIM,
Satisfactory-Tools, Sankeyfactory) offers a **Sankey** where band width is proportional
to throughput, which is the fastest way to read a production chain at a glance. We already
produce a fully-solved `PlanResult`; a Sankey is a pure visualization over it.

## Proposed behavior (research 2026-06-14)

- A **"Flow" toggle** in the draft panel renders the held plan as a left-to-right Sankey:
  - nodes = raw inputs (far left) → recipes (by dependency depth) → targets / sink (far
    right);
  - links = part flows, **width ∝ parts/min**, colored by part (reuse the wire-color
    palette);
  - hover a link → *"Iron Ingot · 240/min"*; hover a node → its machine count.
- Read-only; it visualizes the **draft** before it's applied (and, as a stretch, the
  current canvas graph via the same renderer).

## Implementation

- **Flows in Core (UI-free).** Factor the producer→consumer flow computation out of
  `BuildPlanOnCanvas` (which already wires producers to consumers part-wise) into
  `PlanFlows.From(PlanResult, GameDatabase)` returning `(FromRecipe, ToRecipe, Part, Ppm)`
  links plus raw-input and target/sink edges. This is the single source of truth for both
  the canvas wiring and the Sankey, so they can't disagree.
- **Layout.** Reuse the dependency layering from `FactoryAutoLayout` for the x-axis
  (depth = column); stack nodes on y by cumulative flow.
- **Render.** A dedicated `IDrawable` (same immediate-mode idiom as
  `FactoryCanvasDrawable`) on a `GraphicsView` hosted in the draft panel; cubic-bezier
  bands, palette from `CanvasTheme`. No model change, no new persisted state.

## Tests

Core: `PlanFlows.From` over a known plan (e.g. the zero-waste plastic fixture) yields the
expected link set and magnitudes, and conserves flow at each intermediate node
(Σ in = Σ out). Rendering verified by `/run` + screenshot.

## Acceptance criteria

1. The draft "Flow" view shows the plan as proportional Sankey bands; band widths match
   the per-part rates in the list view.
2. Raw inputs enter on the left, targets/sink exit on the right, recipes are layered by
   depth; hover reveals exact rates.
3. The flow numbers are computed by the same `PlanFlows` helper the canvas wiring uses.
