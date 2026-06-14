# Full calculator (priority-aware solver)

> **Status: ✅ Implemented (v1 — priority routing).** Global-LP optimality is a noted future refinement.

## Problem

The SPEC lists four calculators — None / Manual / Basic / **Full** — but Full was never built:
`SolverFactory.Create` fell back to Basic for "Full" (with a "not implemented yet" comment),
and the Settings *Calculator* picker (`SolverIds`) didn't even offer it. The localization
already ships `FULL_SOLVER` = "Full" with help *"The most accurate. Takes into account
splitter and merger preferences. Values entered are limits. May be slow on large builds."*

Basic's defining limitation (its own help text): *"Does not take into account splitter and
merger preferences"* — surplus at a split is shared **proportionally to demand**. The Full
calculator's headline difference is that it **honors priority splitter / merger ordering**.

## Decided behavior (2026-06-14)

- **Full = Basic's propagation + priority routing.** At a **Priority Splitter / Priority
  Splurger**, the highest-priority outgoing branch is filled completely before the next gets
  any; at a **Priority Merger**, the highest-priority incoming branch is drained first.
  Everywhere else Full behaves exactly like Basic (entered values are limits; unconnected
  inputs surface as "unmade", etc.).
- **Priority = port (connection) order.** The model stores no explicit per-output priority
  numbers, so branch order (the order the connections were made / the reordered ports) is
  the priority. Faithful numeric priorities would need a model field — noted as a follow-up.
- **Opt-in**, selected in Settings → *Calculator* and persisted per document. Default stays
  **Basic**, so nothing changes unless the user picks Full.

### Scope / future work
- v1 layers priority routing on the existing bound-relaxation solver. A true **global LP**
  over the whole graph (the SPEC's "LP over the whole graph" phrasing) — solving all flows
  simultaneously for cases the local relaxation under-determines — is a further step; it would
  reuse the planner's exact-rational simplex (`RevisedSimplexSolver`/`SparseMatrix`).
- Explicit numeric priorities (a per-connection priority field + serialization + canvas UI).

## Implementation

All in `Ficsit.Schematics.Core/Solver/BasicSolver.cs` (the Full solver reuses its tested count
+ flow + result machinery — no separate result builder, so the two calculators can't drift):

- `BasicSolver.HonorPriorities` (`bool` init) — off for Basic/Manual, on for Full.
- `AllocateFlow`: when the producer is a `PrioritySplitter`/`PrioritySplurger` and
  `HonorPriorities`, return `PriorityGrantForSplit` — walks the outgoing branches in order,
  granting each `min(request, remaining)` of the producer's offer. Returns null for an
  unlimited producer (priority doesn't bind → default proportional split).
- `DistributeRequest`: when the consumer is a `PriorityMerger` and `HonorPriorities`, return
  `PriorityMergerShare` — walks the incoming branches in order, taking each `min(offer,
  remaining)` of the total request.
- `SolverFactory`: `"Full" => new BasicSolver(data) { HonorPriorities = true }`.
- `MainPage.xaml.cs`: `SolverIds` gains `"Full"`; the `SolverPicker` items gain
  `_loc.L("FULL_SOLVER")`.

## Tests

`SolverTests`: with a Priority Splitter fed 60 ore and two equal-demand consumers (60 each),
**Basic** splits 30/30 (one machine each) while **Full** fills the first branch fully
(2 machines) and the second to 0. A Priority Merger with two ample suppliers draws the full
demand from the first supplier and nothing from the second under Full.

## Acceptance criteria

1. "Full" appears in the Settings *Calculator* picker and is selectable/persisted.
2. A priority splitter routes by branch order under Full (first branch saturated before the
   next), whereas Basic splits proportionally; a priority merger drains by order.
3. With no priority nodes, Full produces the same results as Basic (it only changes
   allocation at priority nodes); the default calculator stays Basic.
