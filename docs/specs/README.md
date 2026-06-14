# Feature specs

## Status at a glance

Every spec file carries a `> **Status:**` banner under its title. Legend: ✅ Implemented ·
🚧 In progress · 📋 Planned (not yet built) · 📚 Research spike (complete, not a feature).

**📋 To implement — batches 4 & 5 (these are the open work):**
- [planner-somersloop-amplification.md](planner-somersloop-amplification.md)
- [planner-power-generation.md](planner-power-generation.md)
- [canvas-belt-capacity-warnings.md](canvas-belt-capacity-warnings.md)
- [plan-flow-sankey.md](plan-flow-sankey.md)
- [planner-clock-optimization.md](planner-clock-optimization.md) *(optional)*
- [machine-copyable-overclock.md](machine-copyable-overclock.md)
- [planner-panel-layout-ux.md](planner-panel-layout-ux.md)
- [planner-from-port-drag.md](planner-from-port-drag.md)
- [save-world-import.md](save-world-import.md) *(large, phased)*

**🚧 In progress:** [outpost-flat-model.md](outpost-flat-model.md) (active branch
`outpost-flat-model`; supersedes [outpost-linking.md](outpost-linking.md)).

**✅ Implemented (batches 1–3):** [auto-round](auto-round.md), [auto-round-fixes](auto-round-fixes.md),
[ui-readability-ux](ui-readability-ux.md), [map-mode-extractors](map-mode-extractors.md),
[planner-recipe-control](planner-recipe-control.md), [catalog-restructure](catalog-restructure.md),
[canvas-pan-performance](canvas-pan-performance.md), [io-port-reorder](io-port-reorder.md),
[clear-connection-context-menu](clear-connection-context-menu.md), [planner-tier-cap](planner-tier-cap.md),
[planner-draft-review](planner-draft-review.md), [canvas-auto-format](canvas-auto-format.md),
[outpost-linking](outpost-linking.md) *(superseded)*.
**📚 Spike:** [from-save-spike](from-save-spike.md).

---

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
| #14 | Outpost linking + connect-to-outpost crash | [outpost-linking.md](outpost-linking.md) — ✅ superseded by [outpost-flat-model.md](outpost-flat-model.md) |
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

### Third batch (discussion 2026-06-14) — planner UX & canvas

Planner-quality and review improvements, plus a canvas tidy. **All implemented ✅.**

| Title | Spec |
|---|---|
| Auto-Planner tier cap ("only what I've unlocked") | [planner-tier-cap.md](planner-tier-cap.md) |
| Auto-Planner draft review + background planning (spinner/cancel, "Plan ready" chip, summary review, apply/discard) | [planner-draft-review.md](planner-draft-review.md) |
| Canvas auto-format (right-click a selection → tidy into layers) | [canvas-auto-format.md](canvas-auto-format.md) |

> Also landed this batch (no standalone spec): a **resource-preference budget** in the
> Auto-Plan panel — normalized "prefer this raw" sliders mapping onto per-resource
> `PlanRequest.WeightMultipliers`, with a global default in
> `AppSettings.PlannerResourcePreferences`.

### Fourth batch (game-research + user notes 2026-06-14) — mechanics depth & UX

**Status: 📋 Planned — none implemented yet.**

Derived from researching *Satisfactory* 1.0 mechanics against the current app, plus two
user notes (copyable overclock, planner-panel layout). One spec per feature, sequenced so
they can land one at a time.

| Title | Spec |
|---|---|
| Somersloop production amplification in the planner | [planner-somersloop-amplification.md](planner-somersloop-amplification.md) |
| Power-generation planning ("plan me X MW") | [planner-power-generation.md](planner-power-generation.md) |
| Belt / pipe over-capacity warnings on the canvas | [canvas-belt-capacity-warnings.md](canvas-belt-capacity-warnings.md) |
| Sankey flow view of a plan | [plan-flow-sankey.md](plan-flow-sankey.md) |
| Clock fit (overclock/underclock to a budget) — *optional* | [planner-clock-optimization.md](planner-clock-optimization.md) |
| Copyable overclock value (improved over the reference) | [machine-copyable-overclock.md](machine-copyable-overclock.md) |
| Auto-Plan panel: centered/bigger + categorized options & lists | [planner-panel-layout-ux.md](planner-panel-layout-ux.md) |
| Auto-Plan upstream from a backward port drag | [planner-from-port-drag.md](planner-from-port-drag.md) |

The headline finding: the **canvas/solver already model overclocking and Somersloops**
correctly (`NodeProfile`, `MachineDefinition`), but the **auto-planner ignores both** — the
first two specs close that gap.

#### Model & effort

| Spec | Model | Effort | Character of the work |
|---|---|---|---|
| planner-somersloop-amplification | Opus 4.8 | high | Shared amplification math + a greedy post-solve allocator; exact `Rational`; honest heuristic, not MILP. |
| planner-power-generation | Opus 4.8 | high | One new LP concept (virtual `#Power` part); relaxes the output-eligibility filter; net-power accounting must stay coherent. |
| canvas-belt-capacity-warnings | Sonnet 4.6 | low-medium | Catalog-derived thresholds + one render-path check; small new toggle. |
| plan-flow-sankey | Opus 4.8 | medium-high | UI-free `PlanFlows` extraction + a new `IDrawable` Sankey; no model change. |
| planner-clock-optimization | Sonnet 4.6 | low-medium | Uniform-clock post-pass reusing the overclock power helper; optional, overlaps Auto-Round. |
| machine-copyable-overclock | Sonnet 4.6 | low | Two copy buttons + exact-precision formatting + a confirmation toast. |
| planner-panel-layout-ux | Opus 4.8 | medium-high | Centering + responsive 2-column layout + grouped picker/chips/recipe lists; presentation only. |
| planner-from-port-drag | Opus 4.8 | medium | Gesture → seed existing `PlanRequest` + a wire-in intent applied inside `BuildPlanOnCanvas`. No planner concept change. |

#### Suggested implementation order

1. **machine-copyable-overclock** — smallest, self-contained, immediate user value.
2. **canvas-belt-capacity-warnings** — independent, low risk, leans on existing flows/data.
3. **planner-somersloop-amplification** — extract the shared amplification helper here;
   later specs reuse it.
4. **planner-clock-optimization** *(optional)* — reuses that helper; can be skipped.
5. **planner-power-generation** — the deepest planner change; do it once the planner specs
   above have settled the shared helpers.
6. **plan-flow-sankey** — extracts `PlanFlows`; benefits from power/sloop already being in
   the result so the diagram shows the full picture.
7. **planner-from-port-drag** — after the planner inputs exist; it just seeds the existing
   panel from a gesture and adds one wire-in on apply.
8. **planner-panel-layout-ux** — last, so it can surface the new planner inputs (sloop
   budget, power target, clock fit) and the port-drag entry point in the reorganized panel
   rather than being reworked twice.

### Fifth batch (vision 2026-06-14) — save → live world map

**Status: 📋 Planned — not implemented.**

A standalone, large initiative tracked on its own because it dwarfs the others and is
internally phased.

| Title | Spec |
|---|---|
| Import built factories from a save onto the world map (buildings, recipes, connections, outpost clustering, train/truck/drone networks) | [save-world-import.md](save-world-import.md) |

| Spec | Model | Effort | Character of the work |
|---|---|---|---|
| save-world-import | Opus 4.8 (+ Fable 5 for the parser spike) | **very high, phased** | Real object-level save parsing (vs. today's surgical header scan), world→canvas placement, belt-graph + logistics inference, spatial outpost clustering. Phases 0–1 ship the core; 2–4 enrich. |

Phasing is the plan of record (see the spec): **P0** object parser → **P1** place factories
at world locations (first shippable "wow") → **P2** belt/pipe connections (graph, with an
adjacency fallback) → **P3** outpost clustering → **P4** train/truck/drone logistics links.
The open implementation decision is the **parser strategy** (recommended: port object
parsing into the C# reader, validated against a reference parser's JSON).
