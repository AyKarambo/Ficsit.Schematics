# Auto-Planner draft review (background planning)

> **Status: ✅ Implemented.**

## Problem

Planning a deep chain could take up to a minute, and the result was built straight
onto the canvas. Two pains: the app *looked* frozen with no progress and no way to
abort, and a plan you didn't like had to be deleted off the canvas before retrying —
you couldn't judge it before committing.

## Decided behavior (discussion 2026-06-14)

Planning **always** runs as a non-blocking background job; the canvas stays usable.
On completion the plan becomes a **draft** that is reviewed before it touches the
canvas:

```
Configure → Planning (background, cancelable) → Draft ready → Review → Apply | Discard
```

- **Progress / cancel:** an indeterminate spinner with live phase text and a working
  Cancel button (chosen over a fake percentage — an LP has no meaningful progress).
- **Draft review = summary panel** (chosen over a ghosted canvas preview): totals
  (machines · power · recipes), the base resources going in, and the recipes used.
- **Notification = a floating "Plan ready — review" chip** (chosen over auto-opening
  the panel), so a finished plan doesn't steal focus mid-edit. The job and chip
  survive the Auto-Plan panel being closed.
- **Apply is explicit**, with an opt-in **"Apply plans without review"** setting for
  users who trust the planner.

## Implementation

- Core: `FactoryPlanner.Plan(…, IProgress<PlanProgress>?, CancellationToken)`;
  `PlanProgress(string Phase)` reports `Collecting recipes` → `Maximizing achievable
  output` → `Solving`. The token is threaded into
  `RevisedSimplexSolver.Minimize`/`MinimizeWarm` and polled every 256 pivots
  (`Cancel.ThrowIfCancellationRequested()`), so a runaway exact-rational solve is
  genuinely abortable. Pooled arrays are still released on cancel (the `finally`).
- `AppSettings.PlannerAutoApply` (`bool`, default off); round-tripped in `StoreTests`.
- `MainPage.AutoPlan.cs` — `StartPlanning` owns a `CancellationTokenSource`, runs the
  solve in `Task.Run`, and on success either auto-applies or `HoldDraft`s the result
  (`PlanResult` + request + label). The draft is rendered by `RenderDraft` from data
  the `PlanResult` already carries (`Supplies`, `Recipes`, `Sinks`, totals); Apply
  calls the existing `BuildPlanOnCanvas`. A superseding plan cancels the prior one and
  the stale job bows out (`if (_planCts == cts)`).
- `MainPage.xaml` — `DraftPanel` (summary + Apply/Discard), `PlanBusyChip`
  (spinner + phase + Cancel), `PlanReadyChip` (review entry point). The busy/ready
  chips are status, not overlays, so `CloseOverlays` leaves them alone (it refreshes
  the ready chip instead).

## Acceptance criteria

1. Starting a plan never freezes the UI; the canvas remains pannable/editable while
   it solves.
2. Cancel aborts even a long solve promptly (within a pivot batch).
3. With auto-apply off (default), a completed plan does not touch the canvas until
   Apply; Discard leaves the document untouched.
4. With auto-apply on, the plan builds straight onto the canvas as before.
5. Closing the Auto-Plan panel mid-solve still surfaces the "Plan ready" chip when it
   finishes.
