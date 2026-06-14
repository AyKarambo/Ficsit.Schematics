# Auto-Planner — power generation

> **Status: ✅ Implemented.** Virtual `#Power` part produced by generators; a "Power (MW)" target plans the generator + fuel + water chain.

## Problem

Generators (Coal, Fuel, Nuclear, Geothermal, Biomass) exist in the catalog as recipes
that consume fuel/water and carry a positive `AveragePower`, but the planner **excludes
them**: `CollectCandidateRecipes` skips any recipe with no part output
(`!recipe.Outputs.Any()`), and power is not a balanceable quantity in the LP — it is only
*reported* (`PowerPerMachine`). So the planner can synthesize a factory but cannot answer
*"build me 5 GW of fuel power"* — fuel chains, water, and nuclear-waste loops included.

This is one of the most-requested things a Satisfactory planner can do, and the LP is
already shaped for it: byproduct cycles (nuclear waste → reprocessing) fall out naturally,
exactly as the recycled-plastic/rubber loops do today.

## Proposed behavior (research 2026-06-14)

- **Power becomes a plannable quantity.** The user can add a **Power (MW)** target (a
  dedicated quick-add in the targets list, or "Power" surfaced as a pickable item in the
  part picker). The planner builds the generators + fuel + water chain to meet it.
- Generator family is steered by the existing **recipe enable/disable list** and the
  **bias** (e.g. ban Crude Oil to force coal/nuclear; tier cap gates nuclear).
- Nuclear waste is handled by the existing **byproduct policy** — `Eliminate` closes the
  reprocessing loop (Plutonium / Ficsonium), `AllowSink` sinks it.
- The draft reports fuel and water consumption alongside the usual machine/power totals
  (here power is the *target*, not a cost).

## Implementation

The planner learns one new concept: a **virtual "Power" part** (a reserved key, e.g.
`"#Power"`, that can never collide with a real part name).

- In `FactoryPlanner`, generator recipes (machine in the generator set, or
  `AveragePower` positive) contribute `+AveragePowerValue` per machine to the `#Power`
  balance row, and the `Outputs.Any()` eligibility filter is relaxed so a recipe that
  produces only `#Power` still enters the candidate set when `#Power` is demanded.
- `#Power` is seeded into `parts` and the backward BFS so fuel/water producers are pulled
  into the closure like any input.
- A Power target sets `b[partRow["#Power"]]` (target mode) or joins the bundle
  (maximize mode) through the existing machinery — no new solve path.
- Power is dimensionless to the LP (1 unit = 1 MW); it is **not** sinkable (no sink column
  for `#Power`) and **not** an external supply (no scarcity weight), so it can only be
  met by building generators.
- UI: a "Power (MW)" affordance in `MainPage.AutoPlan.cs` targets; `PlanTarget` already
  carries an arbitrary part name + rate, so the row plumbing is unchanged.
- The existing `result.TotalPowerMW` accounting must not double-count: generators that
  *produce* `#Power` are the target, while consumers' draw is still summed as today —
  keep the reported "net power" coherent (generation target vs. factory draw shown
  separately).

## Tests

`FactoryPlannerTests`: targeting 1000 MW of coal power (oil/nuclear banned) yields
Coal + Water + Coal-Powered Generator in the correct ratio and ~1000 MW; a nuclear power
target under `Eliminate` closes the uranium-waste loop (no sink) or sinks waste under
`AllowSink`; a 0 MW / no-power-target plan is identical to today.

## Acceptance criteria

1. A Power (MW) target produces a working generator + fuel + water chain at the requested
   output; banning a fuel forces an alternate generator.
2. Nuclear power respects the byproduct policy for waste (loop vs. sink).
3. Plans with no power target are unchanged.
