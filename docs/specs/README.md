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

## Suggested implementation order (dependencies, risk):

1. **catalog-restructure** — pure generator/code-organization work, no behavior change; gives
   `IsManuallyGathered` part metadata a clean home that planner-recipe-control needs.
2. **auto-round** — self-contained solver + popup work.
3. **planner-recipe-control** — builds on part metadata from (1).
4. **map-mode-extractors** — canvas work, independent.
5. **ui-readability-ux** — incremental polish, can be interleaved anytime.
