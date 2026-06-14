# Auto-Planner — clock fit (overclock / underclock budget)

> **Status: ✅ Implemented.** Uniform-clock fit to a machine/power budget, post-solve.

> **Priority: optional / lowest of this batch.** It overlaps the per-node **Auto-Round**
> that already exists ([auto-round.md](auto-round.md)). Listed for completeness because
> over/underclocking is a core game mechanic the *planner* can't currently exploit.

## Problem

The planner emits fractional machine counts at 100% clock. The game lets you trade space
for power by **overclocking** (rate ∝ clock, power ∝ clock^`OverclockPowerExponent`
≈ clock^1.6) or save power by **underclocking**. Auto-Round handles this per node *after*
the fact, but the plan can't be steered toward a **machine-count** or **power budget** up
front, e.g. "fit this into 5 buildings" or "stay under 800 MW."

## Proposed behavior (research 2026-06-14)

- An optional **"Fit to"** constraint in the Auto-Plan panel: *machine count* or *power
  budget* (default off → today's behavior).
- v1 applies a **single uniform clock** that scales the solved plan into the budget:
  - machine budget M, solved total T → `clock = clamp(T / M, 0.01, 2.5)`;
  - the plan reports the resulting clock and the recomputed power (via the overclock
    exponent), or, if the budget is infeasible at the 250% ceiling, the closest achievable
    and by how much it misses.
- Applying the plan sets each node's `ClockSpeed` to the uniform factor.

**Scope.** True per-recipe clock optimization is nonlinear (each recipe has its own power
curve and machine count) and is out of scope. v1 is the explainable uniform-scale case;
per-node refinement is left to Auto-Round.

## Implementation

- Reuse the shared overclock power helper (the one `NodeProfile.PowerPerMachineAt` uses —
  see [planner-somersloop-amplification.md](planner-somersloop-amplification.md) for the
  extraction) so planned and canvas power agree.
- A post-solve pass computes the uniform clock and recomputes `TotalMachines` /
  `TotalPowerMW`; `BuildPlanOnCanvas` sets `node.ClockSpeed`.
- `PlanRequest.FitMode` + `FitBudget`; `AppSettings` default; draft reports the clock.

## Tests

Scaling 10 machine-equivalents into a budget of 5 → 200% clock, per-machine power ×2^1.6;
a budget below the 250% ceiling reports the shortfall; no budget → unchanged plan.

## Acceptance criteria

1. A machine/power budget yields a uniform overclock and updated power; applying sets node
   clocks; the canvas re-solves to the same totals.
2. An infeasible budget reports the closest achievable result rather than failing.
3. With "Fit to" off, plans are unchanged.
