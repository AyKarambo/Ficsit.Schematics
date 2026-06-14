# Feature specs

Specs derived from the issue list, interview held 2026-06-12. Issue → spec mapping:

| Issue | Title | Spec |
|---|---|---|
| #1 | Auto-Round whole machines | [auto-round.md](auto-round.md) |
| #2 | Output icons in recipe chooser | [ui-readability-ux.md](ui-readability-ux.md) |
| #3 | Hard-to-read numbers (port PPM labels) | [ui-readability-ux.md](ui-readability-ux.md) |
| #4 | Map-mode extractor size & snapping | [map-mode-extractors.md](map-mode-extractors.md) |
| #5 | Planner recipe toggle list + alts from save | [planner-recipe-control.md](planner-recipe-control.md) |
| #6 | Planner must not default to biomass | [planner-recipe-control.md](planner-recipe-control.md) |
| #7 | Machine category base classes, merge families | [catalog-restructure.md](catalog-restructure.md) |
| #8 | Machines/recipes into subfolders | [catalog-restructure.md](catalog-restructure.md) |
| #9 | UI readability (settings scrollbar, editor placement) | [ui-readability-ux.md](ui-readability-ux.md) |
| #10 | Further UI/UX improvements (discussion) | [ui-readability-ux.md](ui-readability-ux.md) |
| #11 | Laggy canvas panning | [canvas-pan-performance.md](canvas-pan-performance.md) |
| #12 | Drag-to-reorder input/output ports | [io-port-reorder.md](io-port-reorder.md) |
| #13 | Right-click a port → clear connection | [clear-connection-context-menu.md](clear-connection-context-menu.md) |
| #14 | Outpost linking + connect-to-outpost crash | [outpost-linking.md](outpost-linking.md) |
| #15 | Auto-Round stepper bugs (limited nodes; clock +/- when off) | [auto-round-fixes.md](auto-round-fixes.md) |

> A reported planner "packaged-fuel sink" bug (plastic, zero-waste) was **withdrawn** after
> investigation — a disabled-alternate-recipes repro mistake, not a defect. With all recipes
> on, the Auto-Plan panel builds the same `PlanRequest` as the passing
> `Finds_the_zero_waste_plastic_optimum` test, so there is no code change.

## Model & effort per spec

Each spec carries a detailed implementation plan and a recommended model/effort.
Effort key — **low**: single pass, build + existing tests; **medium**: plan
first, new tests, build verification; **high**: plan mode + extended thinking,
`/run` verification, self-review of the diff.

| Spec | Model | Effort | Character of the work |
|---|---|---|---|
| auto-round | Fable 5 | high | Small but subtle: exact Rational math, power-at-effective-clock, flow invariants. |
| planner-recipe-control | Opus 4.8 (Fable 5 for the save spike) | medium / high | Layered wiring + one genuinely uncertain save-format spike. |
| map-mode-extractors | Opus 4.8 | medium (high for gestures) | Canvas geometry + pointer state machine; verify by running. |
| catalog-restructure | Sonnet 4.6 | medium | High-volume mechanical generator work guarded by a snapshot test. |
| ui-readability-ux | per slice: Haiku → Sonnet → Opus | low → high | Three independent slices; side panel is the hard one. |
| canvas-pan-performance | Sonnet 4.6 | medium | Render hot-path caching + status gating; measurable, no behavior change. |
| io-port-reorder | Opus 4.8 | medium-high | Model + serialization + layout + a new pointer gesture (must not regress connect). |
| clear-connection-context-menu | Sonnet 4.6 | low-medium | Small: port-first right-click hit-test + one grouped-undo disconnect. |
| outpost-linking | Opus 4.8 | high | Solver-semantics change (outpost as open boundary) + side-aware gesture. **Contains a crash fix.** |
| auto-round-fixes | Fable 5 | high (B alone: Sonnet low) | Exact-Rational throughput invariant vs. a binding limit; plus a small off-state clock stepper. |

## Suggested implementation order (dependencies, risk):

1. **catalog-restructure** — pure generator/code-organization work, no behavior change; gives
   `IsManuallyGathered` part metadata a clean home that planner-recipe-control needs.
2. **auto-round** — self-contained solver + popup work.
3. **planner-recipe-control** — builds on part metadata from (1).
4. **map-mode-extractors** — canvas work, independent.
5. **ui-readability-ux** — incremental polish, can be interleaved anytime.

### Second batch (interview 2026-06-13) — canvas editor

User's stated order: performance → reorder → clear-connection → outpost. All four are
largely independent, but reorder/clear-connection/outpost all touch
`Canvas/CanvasController.cs` (pointer handling + `HitPort`/right-click), so expect minor
merge overlap if run in parallel.

1. **canvas-pan-performance** — isolated to the render/invalidate path; no model change.
2. **io-port-reorder** — model + serialization + a new drag gesture.
3. **clear-connection-context-menu** — small; adds a port-first right-click branch.
4. **outpost-linking** — **contains a genuine crash fix** (connecting to an outpost throws);
   may warrant prioritizing ahead of the others despite the user's stated order.
5. **auto-round-fixes** — independent solver/editor fix (follow-up to #1); can run anytime.
