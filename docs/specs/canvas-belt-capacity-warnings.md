# Canvas — belt / pipe capacity warnings

> **Status: ✅ Implemented.** Belt threshold is catalog-derived; the pipe threshold is a documented constant (pipes aren't modelled as catalog capacities yet).

## Problem

A single connection can carry more than any belt or pipe can physically move — a solid
flow above 1200/min (Mk.6 belt) or a fluid flow above 600/min (Mk.2 pipe) **cannot be one
line in-game**, it must be split/manifolded. The solver computes the exact per-connection
flow, but nothing flags an over-capacity edge, so a plan or a hand-built section can look
correct yet be unbuildable as drawn. This is a cheap, high-value correctness hint that
leans entirely on data and flows the app already has.

## Proposed behavior (research 2026-06-14)

- The canvas flags any connection whose solved flow **exceeds the top belt (solids) or
  top pipe (fluids) throughput**: a small warning glyph on the path and a red-tinted
  segment, with a tooltip like *"3000/min exceeds Mk.6 belt (1200/min) — needs 3 belts."*
- The status bar / plan summary shows a count of over-capacity connections so they're
  discoverable without hunting.
- A settings toggle (**default on**) to show/hide the warnings.

**Scope.** v1 uses the **maximum** belt/pipe throughput as the threshold (the "this can
never be one line" case). v2 (noted, not built here): let a connection carry a chosen
belt/pipe mark and warn against *that* mark — needs new per-connection model state and
serialization, so it's deferred.

## Implementation

- Source the thresholds from catalog data, not magic numbers: derive
  `GameDatabase.MaxBeltThroughput` and `MaxPipeThroughput` from the top belt/pipe-mark
  capacities already in the catalog (`ExtractorMachines` / `MachineModule` belt+upload
  marks). If a future Mk.7 belt is added to the data, the threshold tracks it.
- The flow per connection is already known to the solver; `FactoryCanvasDrawable.Connections`
  draws each path. Add an overflow check there: pick belt vs. pipe by the carried part's
  `Fluid` flag, compare flow to the matching threshold, and when it exceeds, draw the
  warning marker + tint. Compute the suggested line count `ceil(flow / threshold)` for the
  tooltip.
- `AppSettings.ShowBeltCapacityWarnings` (`bool`, default true); a switch in the Settings
  panel "Style" section; round-tripped in `StoreTests`.
- Status/summary: a small `N over-capacity` indicator (reuse the existing status line).

## Tests

Core: `GameDatabase.MaxBeltThroughput` / `MaxPipeThroughput` equal the top mark from the
catalog (guard with the snapshot oracle). A flow helper: 1500/min solid over 1200 →
overflow, suggested 2; 1000/min solid → none; 700/min fluid over 600 → overflow. UI
rendering verified by `/run`.

## Acceptance criteria

1. A connection carrying more than the top belt/pipe rate shows a warning glyph + tint and
   a tooltip naming the limit and the number of lines needed.
2. Within-capacity connections and fluid-vs-solid thresholds are distinguished correctly.
3. Thresholds come from catalog belt/pipe data; the toggle hides/shows the warnings and
   persists.
