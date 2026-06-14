# Auto-Planner — Somersloop amplification

> **Status: 📋 Planned — not yet implemented.**

## Problem

The canvas already models **production amplification** correctly: a sloopable
machine's output scales `1 + filled/total · multiplier` and its power scales by the
slot-ratio raised to `ProductionShardPowerExponent` (≈ quadratic) —
`NodeProfile.BuildRecipeProfile` / `PowerPerMachineAt`, off `MachineDefinition`
(`MaxProductionShards`, `ProductionShardMultiplier`, `ProductionShardPowerExponent`).

The **auto-planner ignores all of this** — every planned machine runs at 100% clock
with zero Somersloops. So the planner can't answer the game's most interesting
late-game question: *Somersloops are finite (106 in the whole world) and buy 2× output
for 4× power — where do I spend the ones I have for the biggest win?*

## Proposed behavior (research 2026-06-14)

- A **"Somersloops available"** input in the Auto-Plan panel (default 0 = off, today's
  behavior exactly). Persisted as a global default, like the other planner toggles.
- After a normal solve, the planner spends the budget on **whole machines of sloopable
  recipes** (those whose machine has `MaxProductionShards > 0`), greedily, by marginal
  benefit to the active objective:
  - **Resources bias** — slooping a step halves its required machines *and* its input
    demand for the same output, so prefer the step whose halved input most reduces the
    scarcity-weighted raw draw (the cascade upstream is the real prize).
  - **Machines bias** — prefer the step with the most machines to remove.
  - **Power bias** — slooping *costs* power (quadratic), so the budget defaults to 0
    here and only spends if the user opts in.
- The draft review reports: **Somersloops used**, machines saved, the resulting raw-input
  reduction, and the **extra MW** the amplification costs (honest about the 4× ceiling).
- Applying the plan sets `FactoryNode.Somersloops` on the affected nodes so the canvas
  shows and re-solves the slooped configuration.

**Scope (v1 is a heuristic, stated plainly):** the budget is allocated as a
**post-solve greedy pass** — the LP itself is unchanged. Discrete, finite Somersloops
across continuous machine counts is a mixed-integer problem; a true MILP optimum is out
of scope. The greedy pass is explainable and never overstates its result ("spent N of M
for −X raw / +Y MW").

## Implementation

- **Shared amplification math.** Extract the output/power factors now inline in
  `NodeProfile` into a small UI-free helper (e.g. `Amplification` in `Core/Planning` or
  static methods on `MachineDefinition`): `OutputFactor(machine, sloops)` and
  `PowerFactor(machine, sloops)`. `NodeProfile` and the planner then share one source of
  truth — no drift between canvas and plan.
- `PlanRequest.SomersloopBudget` (`int`, default 0).
- `SomersloopAllocator.Allocate(GameDatabase, PlanResult, PlanRequest)` — ranks sloopable
  `PlannedRecipe`s, assigns sloops machine-by-machine within each machine's slot cap and
  the global budget, and returns updated machine counts + per-recipe sloop counts.
- `PlannedRecipe` gains `Somersloops`; `PlanResult` gains `SomersloopsUsed` and updated
  `TotalMachines` / `TotalPowerMW` (recomputed via `PowerFactor`).
- `BuildPlanOnCanvas` sets `node.Somersloops` from the plan (inside the existing grouped,
  suspended batch).
- `AppSettings.PlannerSomersloopBudget` (global default; round-tripped in `StoreTests`).
- UI: an entry next to the tier cap in `MainPage.xaml`; `MainPage.AutoPlan.cs` seeds it
  from settings, folds it into the request, and renders the sloop line in `RenderDraft` /
  `Summarize`.

## Tests

`FactoryPlannerTests` (or a new `SomersloopAllocatorTests`): the allocator spends the
budget on the highest-value recipe first; never exceeds a machine's slot cap; output
factor and power factor match the `NodeProfile` formula for the same inputs; a budget of
0 reproduces today's plan byte-for-byte. `StoreTests`: `PlannerSomersloopBudget`
round-trips.

## Acceptance criteria

1. With a budget > 0, a plan reports Somersloops used, fewer machines / less raw draw,
   and the extra MW; applying it sets `Somersloops` on the right nodes and the canvas
   re-solves to the same numbers.
2. A budget of 0 reproduces today's behavior exactly.
3. The amplification math is shared with the canvas (one helper) — the planned slooped
   power equals what the canvas shows for the same configuration.
