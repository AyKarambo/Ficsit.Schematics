# Machine editor — copyable overclock value

> **Status: ✅ Implemented.**

## Problem

The reference app shows a machine's clock speed as a static number you eyeball and retype
into the in-game machine. Our machine editor shows the clock in `PopupClockEntry` and, for
Auto-Round nodes, as the read-only `PopupAutoClockValue` ("N × clock%"), plus a
`PopupSloopValue` ("N / max") — but there is **no one-click copy**, and the displayed value
is rounded for display (`ToDecimalString(4, …)`). Retyping a rounded clock drifts the
real throughput away from what the planner/solver computed.

## Proposed behavior (research 2026-06-14) — an improved version of the reference

A copy affordance on the clock value that puts the **exact** clock on the clipboard,
formatted precisely for the game's overclock field — and goes beyond the reference in
four ways:

1. **Exact, not rounded.** Copy the clock derived from the exact `Rational` solve,
   formatted to the precision the game accepts, so pasting reproduces the planned
   throughput with zero drift (the on-screen value stays human-readable; the *copied*
   value is full precision).
2. **Context-aware.** For an Auto-Round node, copy the **effective** clock the solver
   actually computed (`AutoRoundState`: `workload / count`), not the value the user typed —
   that's the number you actually set in-game.
3. **One tap + confirmation.** A small copy glyph on the clock row (and Auto-Round row);
   tap → clipboard + a brief *"Copied 156.25%"* confirmation. No manual text selection.
4. **"Copy machine setup."** A second action copies a compact, shareable line —
   `Constructor · Iron Plate · ×2 @ 156.25% · 1 Somersloop` — for notes, wikis, or chat.

## Implementation

- **Game precision constant.** Define `GameClockDecimals` (the editor already formats at
  4 decimal places of percent; confirm against the live build and keep it as one named
  constant so it's easy to change if the game's field precision differs).
- **Exact clock string** (`MainPage.MachineEditor.cs`):
  - free clock → `node.ClockSpeed * 100` formatted at `GameClockDecimals`;
  - Auto-Round → `workload / count * 100` from the existing `AutoRoundState(node)`.
  Both via `Rational.ToDecimalString` from the exact value (no intermediate rounding).
- **Buttons.** A copy `IconButton` in `PopupClockRow` and `PopupAutoClockRow` in
  `MainPage.xaml`; handlers call `Clipboard.SetTextAsync` and show the confirmation.
- **Confirmation toast.** Reuse a lightweight transient label (the app already floats
  pill/status overlays — e.g. mirror `PlanBusyChip`'s pill) rather than a blocking dialog.
- **Copy setup** builds the line from `node`, `MachineFor(node)`, `AutoRoundState`, and
  `node.Somersloops`.

## Tests

A formatting helper is unit-testable in Core/UI-light: a node at exactly 156.25% emits
`156.25` (trimmed) at `GameClockDecimals`; an Auto-Round node emits its effective clock
(`workload/count`), not the entered clock; the value parsed back equals the source
`Rational` within `GameClockDecimals`. Button wiring + toast verified by `/run`.

## Acceptance criteria

1. A copy button on the clock row places the exact clock % (game precision) on the
   clipboard with a visible confirmation.
2. For an Auto-Round node it copies the **effective** solved clock, not the entered value.
3. "Copy machine setup" yields a one-line summary including machine, recipe, count, clock,
   and Somersloops.
