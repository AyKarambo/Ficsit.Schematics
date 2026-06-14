# Auto-Round fixes: limit-pinned stepper + clock stepper when off (#15)

Follow-up to [auto-round.md](auto-round.md) (#1), which shipped the feature. Two defects
remain.

## Problem

### Bug A — Auto-Round stepping changes throughput on limited nodes (the "longer chains" bug)
The Auto-Round invariant is: rounding/stepping changes only the **machine count** (and the
rebalanced per-machine clock), **never** the node's input/output throughput. This holds for
**passive display** — `BasicSolver.BuildResult` keeps every port `Target`/`Connected` on the
**exact fractional `count`** while only the displayed `Count`/`DisplayValue`/`EffectiveClock`
become whole (`Ficsit.Schematics.Core/Solver/BasicSolver.cs:362-409`). So short, hand-built
chains look right.

It breaks for **interactive stepping on a node whose count is pinned by a `Max` limit.** The
stepper assumes "count follows clock": it writes `ClockSpeed = W / N'`
(`MainPage.MachineEditor.cs:StepMachineCount` :257-265, and the off-state
`FactoryEditor.StepClockToWholeMachines` :235-246) expecting the re-solve to land at `N'`
machines. But for a count-display node, `LimitCount` is **clock-independent** — it is the raw
machine-count `Max` (`NodeProfile.cs:169-170`). When that limit binds, the re-solve keeps
`count` pinned at the limit while the clock dropped, so **throughput = count × rate@newClock
changes** and the machine count does not. Result: I/O moves instead of the machine count —
exactly the reported symptom.

Why it correlates with **longer chains**: Auto-Plan writes a machine-count limit onto *every*
generated node (`MainPage.AutoPlan.cs:394`, `editor.SetLimit(node, machines.ToString())`), so
planned/long factories have a binding limit on essentially every node; hand-built short
chains usually have limits only (if at all) on the final node.

### Bug B — Can't change clock with +/- when Auto-Round is OFF
The off-state clock row's `−`/`+` buttons (`MainPage.xaml:372-373`) call
`OnClockStepDown/Up` → `StepClock` → `FactoryEditor.StepClockToWholeMachines`
(`MainPage.MachineEditor.cs:239-248`). That method rounds the **count** to the next whole
machine, so it is a **no-op whenever the solved count is already whole or zero**:
- a freshly placed / unconnected machine has `count == 0` → `if (!count.IsPositive) return;`
  (`FactoryEditor.cs:239`) → nothing happens;
- a node already at a whole count → `target == count` → `newClock == ClockSpeed` → no change.

So for most nodes the off-state `+`/`−` appear dead. The user expects them to **step the
clock speed by a fixed increment** (the "optional polish" left unimplemented in #1).

## Decided behavior

### Fix A — stepping moves the machine count, never the throughput
When Auto-Round is ON, the `−`/`+` stepper must change the **physical machine count** while
holding throughput `W` constant, *including* when a `Max` limit binds:
- Compute `W` and current whole `N` as today (`AutoRoundState`, :272-278).
- On step to `N' = N ± 1` (clamped by `CanStepTo`): set `ClockSpeed = W / N'` **and**, when
  the node has a `Max` set (`node.Max`/`LimitValue` present), update that limit to `N'` so
  the solver lands on `N'` machines instead of staying pinned at the old limit. Do both in
  **one undo step** (group the two `SetProperty`/`SetLimit` calls).
- When the node has no limit, keep today's behavior (clock alone; count follows clock).
- Net effect: `N'` machines at clock `W/N'` ⇒ throughput `N' × rate@(W/N') = W × rate@1` =
  unchanged. Verify the limit unit matches `LimitCount` (machine count for count-display
  nodes; if the node is ppm-display, set the limit in ppm = `W × rate@1`, i.e. the throughput
  the limit must encode so the count resolves to `N'`).

This keeps the passive path untouched (still correct) and fixes the interactive path on
limited/auto-planned chains.

### Fix B — off-state +/- is a fixed-increment clock stepper
When Auto-Round is OFF, `OnClockStepDown/Up` should step `node.ClockSpeed` by a fixed
increment (not round to whole machines), clamped to the game range `(1%, 250%]`
(`FactoryNode.MinClockSpeed`/`MaxClockSpeed`), and work regardless of the solved count
(including count 0 / unconnected nodes). Use `FactoryEditor.SetClockSpeed` (already clamps
the upper bound; add the lower clamp). Keep the free-text entry for exact values.

## Touch points

- `MainPage.MachineEditor.cs` — `StepMachineCount` (:257-265): add the limit update when a
  limit is set, grouped with the clock write. `StepClock`/`OnClockStep*` (:239-248): replace
  the whole-machine rounding with a fixed-increment clock step for the OFF state.
- `Ficsit.Schematics.Core/Editing/FactoryEditor.cs` — `SetClockSpeed` (:224-229): add the
  `MinClockSpeed` lower clamp. Possibly a small grouped `StepAutoRound(node, delta)` helper
  that writes clock + limit atomically (undoable), mirroring the existing command grouping.
- `Ficsit.Schematics.Core/Solver/NodeProfile.cs:169-170` — confirm the limit/clock unit
  relationship used by the fix (count-display vs ppm-display).
- No serializer change (`ClockSpeed`, `Max`, `AutoRound` already round-trip).

## Recommended model & effort

**Fable 5, high.** Same numeric-invariant territory as #1 — exact `Rational` reasoning about
the throughput invariant and the limit/clock/count relationship; a wrong fix silently
corrupts rates. Bug B alone is a small UI change that could be split to **Sonnet 4.6, low**,
but Bug A is the careful part. Reproduce first via Auto-Plan (which sets limits everywhere),
then verify with `/run` and a solver regression test.

## Implementation plan

### Phase 1 — Reproduce & lock with a test
1. `SolverTests`/editor test: build a node with a binding `Max` limit and Auto-Round ON;
   record output throughput; simulate a step (`StepMachineCount(+1)`); assert the **machine
   count changed to N+1** and the **output throughput is unchanged**. This currently fails —
   it pins the bug.

### Phase 2 — Fix A (limited-node stepping)
2. In `StepMachineCount` (and any shared editor helper), when `node` has a limit set, write
   the new limit `N'` alongside `ClockSpeed = W/N'` in one undo group; when no limit, write
   only the clock (today's path). Get the limit unit right per `NodeProfile` (count vs ppm).
3. Confirm `CanStepTo` bounds still gate correctly (clock stays in `(1%, 250%]`).

### Phase 3 — Fix B (off-state clock stepper)
4. Repoint `OnClockStepDown/Up` to a fixed-increment clock step (e.g. ±10 percentage points,
   clamped), independent of solved count; add the lower clamp to `SetClockSpeed`.
5. Confirm the off-state entry still accepts exact typed values.

### Phase 4 — Verify
6. `/run`: Auto-Plan a multi-stage factory (e.g. the zero-waste plastic build), turn on
   Auto-Round, step several nodes — machine counts change, all port rates stay put; with
   Auto-Round off, `+`/`−` visibly change the clock on any node including a freshly placed
   one. Sweep undo/redo and save/reload.

## Open questions

- **Off-state step increment**: ±10 percentage points (100→90→80…) is the suggested default;
  could instead snap to nice values or expose a setting. Confirm the magnitude during `/run`.
- For a **ppm-display** limited node, should the step adjust the ppm limit (throughput) or
  convert to a machine-count limit? Suggest: keep the limit in its existing unit and set the
  value that makes the solved count land on `N'`.

## Acceptance criteria

1. Auto-Round ON, node with a binding `Max` limit (as Auto-Plan creates): pressing `−`/`+`
   changes the **machine count** by one and rebalances the clock; **every input/output rate
   on the node is unchanged**.
2. The same holds across a long/auto-planned chain — no port rate shifts when stepping any
   node; only counts and clocks move.
3. Auto-Round OFF: `−`/`+` change the clock speed by a fixed increment on any node, including
   an unconnected one (count 0); values clamp to `(1%, 250%]`; typed exact values still work.
4. One Ctrl+Z reverts a step (clock + limit together); save/reload reproduces the stepped
   state.
5. Passive Auto-Round display is unchanged from #1 (fractional throughput preserved on all
   ports).
