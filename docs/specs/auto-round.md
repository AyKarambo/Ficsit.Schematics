# Auto-Round: whole machines with rebalanced clock (#1)

> **Status: ✅ Implemented.**

## Problem

The Auto-Round flag exists end-to-end as data — `FactoryNode.AutoRound`
(`Ficsit.Schematics.Core/Model/FactoryNode.cs`), defaulted from
`MultiMachineBase.AutoRound` on placement (`FactoryEditor.AddNode`), toggled in the
machine editor popup (`MainPage.MachineEditor.cs`), serialized by `SfmdSerializer` —
but **nothing ever reads it**. Neither `BasicSolver` nor the canvas renderer consults
the flag, so toggling it changes nothing. Machine counts display as fractions
(e.g. `5.5`), and the user cannot tell what clock speed to set each machine to
in-game.

## Decided behavior (interview 2026-06-12)

Auto-Round ON means: **the machine count is always a whole number (rounded up), and
the clock speed is rebalanced so total output exactly matches the requirement.**

Worked example: solved requirement is 5.5 machine-equivalents at 100%.

- Auto-Round OFF: shows `5.5` machines @ 100% (today's behavior).
- Auto-Round ON: shows `6` machines @ `91.6667%`. The displayed clock is exactly
  the value the user sets on each machine in-game to get the expected output.

### Clock stepper replaces free clock entry

When Auto-Round is ON, the user no longer types explicit clock values. The clock
control in the editor popup becomes a **machine-count stepper**:

- **`−` (decrease clock)** → one **more** machine, clock rebalances down.
  From 6 machines: press `−` twice → 8 machines (7, then 8).
- **`+` (increase clock)** → one **fewer** machine, clock rebalances up.
  From 6 machines: press `+` three times → 3 machines (5, 4, then 3).
- The clock entry displays the computed effective clock read-only while
  Auto-Round is ON (the popup also shows the whole machine count).

When Auto-Round is OFF the popup keeps the current free-text clock entry.
A `+`/`−` stepper may also be added for the OFF state (stepping clock by a fixed
increment), but that is optional polish, not part of this spec's core.

## Mechanics

Let `W` = solved workload in machine-equivalents at 100% clock (what `BasicSolver`
computes today as `Count × ClockSpeed`, i.e. count at clock 1).

With Auto-Round ON:

- `N = ceil(W / clockBound)` where `clockBound` is the node's stored
  `ClockSpeed` (acts as the per-machine upper bound).
- Effective per-machine clock `c = W / N` (always `≤ clockBound`).
- Stepping to `N' = N ± 1` stores `ClockSpeed = W / N'` exactly (Rational), which
  is stable: re-solving gives `ceil(W / (W/N')) = N'`.
- When the graph changes and `W` becomes `W'`, the count recomputes as
  `N = ceil(W' / ClockSpeed)` and the effective clock rebalances — count stays
  whole automatically without user action.

Bounds and edge cases:

- Clock is clamped to the game's range `(1%, 250%]` (`FactoryNode.ClockSpeed`
  documents `(0, 2.5]`). Minimum reachable count: `ceil(W / 2.5)`; `+` disables
  there. `−` disables when the rebalanced clock would drop below 1%.
- `W = 0` (no demand/supply yet): show `0` machines, stepper disabled.
- All math stays in `Rational` — no floating-point drift; display formatting goes
  through the existing Numbers settings (`NumberFormatService`).
- Somersloops and the power formula use the **effective clock** `c`, not the bound.

## Touch points

- `Ficsit.Schematics.Core/Solver/BasicSolver.cs` — after counts converge, apply a
  rounding pass for nodes with `AutoRound`: populate `NodeResult` with whole
  `Count` and a new `EffectiveClock` (used for power and per-port flows must stay
  based on `W`, which is unchanged by rounding — only the count/clock split moves).
- `Ficsit.Schematics.Core/Solver/NodeResult.cs` — add `EffectiveClock`.
- `Canvas/FactoryCanvasDrawable.cs` — value row renders the whole count.
- `MainPage.MachineEditor.cs` / `MainPage.xaml` — popup: read-only clock +
  stepper when AutoRound is ON; live update of count and clock on step.
- Serialization unchanged (`ClockSpeed` and `AutoRound` already round-trip).

## Recommended model & effort

**Fable 5, high effort** (plan mode first, extended thinking on the solver pass).
Small surface but high subtlety: exact `Rational` math, a power formula that must
switch to the effective clock, and a hard invariant (port flows unchanged by
rounding). A wrong rounding pass corrupts numbers silently — this is the one spec
where reasoning quality dominates typing volume. UI phase (stepper) could drop to
Sonnet 4.6 / medium if split off.

## Implementation plan

Useful simplification found during code survey: `NodeProfile` bakes the clock into
per-machine rates (`throughput = variantRatio * capacityRatio * clock`), so the
solved `Count` is already "machines at the entered clock". Therefore
`N = ceil(Count)` and `effectiveClock = Count × ClockSpeed / N` — no separate
workload bookkeeping needed.

### Phase 1 — Rational ceiling
1. Add `Rational.Ceiling()` (and `Floor()` if absent) to
   `Ficsit.Schematics.Core/Numerics/Rational.cs` with unit tests in
   `RationalTests.cs` (negative values, exact integers, huge numerators).

### Phase 2 — Solver rounding pass
2. `NodeResult` gains `Rational? EffectiveClock` and `bool IsRounded` (display
   count already fits in `DisplayValue`).
3. In `BasicSolver.BuildResult` (the non-throughput branch, ~line 362): for nodes
   with `node.AutoRound`, `node.Kind == Recipe`, `count > 0`:
   - `N = count.Ceiling()`; `effClock = count * node.ClockSpeed / N`.
   - `nodeResult.Count = N`, `EffectiveClock = effClock`; **port
     `Target`/`Connected` keep using the exact fractional `count`** — extract the
     exact count into a local before overwriting so flows are untouched.
   - ppm-display nodes (`IsPpmDisplay`): `DisplayValue` stays throughput-based;
     only the popup shows N + clock.
4. Power correctness: `PowerPerMachine` was computed at the entered clock with
   exponent `clock^OverclockPowerExponent`. Extract the power computation from
   `NodeProfile.BuildRecipeProfile` (lines ~167–184) into a static helper taking
   the clock as a parameter; the rounding pass recomputes
   `Power = N × PowerAt(effClock)`.
5. Clamps: skip rounding (and flag `nodeResult.IsInvalid = false`, just fall back
   to exact display) when `effClock` would leave `(1%, 250%]` — can only happen
   via stepping, see Phase 3 guards.

### Phase 3 — Stepper semantics
6. Stepping lives in `MainPage.MachineEditor.cs` (popup already reads solve
   results): `StepMachineCount(int delta)` reads the node's current exact
   `Count`/`ClockSpeed` from the last `SolveResult`, computes
   `N' = N + delta` (note: `−` button → `delta = +1` machine), clamps to
   `ceil(W / 2.5) ≤ N'` and `W / N' ≥ 1%` where `W = Count × ClockSpeed`, then
   writes `ClockSpeed = W / N'` through `Editor.SetProperty` (undoable, same
   pattern as `StepSloop`).
7. Guard: `W = 0` (no demand) → stepper disabled.

### Phase 4 — UI
8. `MainPage.xaml` popup clock row: when `AutoRound` is on, swap the free
   `PopupClockEntry` for a read-only clock label + machine-count display +
   `+`/`−` buttons (mirror the existing Somersloop stepper row). Heed the known
   WinUI disabled-button styling gotcha when disabling at clamp bounds.
9. Toggling Auto-Round in the popup refreshes the row in place; canvas value row
   (`FactoryCanvasDrawable` ~line 320) needs no change — it renders
   `DisplayValue`, which the solver now rounds.

### Phase 5 — Tests & verification
10. `SolverTests`: (a) 5.5-machines fixture → `Count == 6`,
    `EffectiveClock == 11/12` exactly; (b) port flows identical with flag on/off;
    (c) power equals `6 × PowerAt(11/12)`; (d) ppm-display node unaffected in
    `DisplayValue`; (e) serializer round-trip of stepped clock reproduces N.
11. Rollout note: `MultiMachineBase.AutoRound` defaults **true**, so implementing
    the behavior flips most existing nodes from fractional to whole display.
    That matches the feature's intent, but verify with `/run` on an existing
    document and confirm the popup makes the change discoverable.

## Acceptance criteria

1. Node requiring 5.5 machines @ 100%, Auto-Round ON → displays `6` machines;
   popup shows clock `91.6667%` (formatted per Numbers settings).
2. From that state, pressing `−` twice → `8` machines @ `68.75%`; pressing `+`
   three times from 6 → `3` machines @ `183.3333%`.
3. `+` is disabled when stepping would push clock above 250%; `−` disabled below 1%.
4. Changing an upstream rate re-solves to a whole count without user input.
5. Auto-Round OFF behaves exactly as today (fractional counts, editable clock).
6. Output/input port flows are identical with Auto-Round ON vs OFF (rounding
   never changes throughput, only the count/clock presentation).
7. Save/load round-trips the stepped state (count reproduces from stored clock).
