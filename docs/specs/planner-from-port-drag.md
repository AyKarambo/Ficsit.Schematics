# Auto-Plan upstream from a port drag

> **Status: 📋 Planned — not yet implemented.**

## Problem

Backward planning today: drag a wire **out of a machine's input port** onto empty canvas
and the chooser opens pre-filtered to recipes that *produce* that part
(`CanvasController.CompleteConnection` → `OpenChooserForPort` →
`MainPage.Chooser.ShowChooserForPort`, with `PortDragContext(Node, Part,
FromOutput: false)`). You pick **one** producing recipe and it's placed and wired to the
port (`AddChosenNode`).

But often you don't want one machine — you want *the whole upstream chain* that supplies
this input at the rate the machine needs. There's no way to say "synthesize the supply for
this" from the gesture; you have to abandon the drag, open Auto-Plan, retype the part and
rate, plan, then hand-wire the result back to the machine.

## Proposed behavior (user request 2026-06-14)

When the chooser is open **from an input-port drag** (the filtered/backward case,
`_pendingPortConnect` set with `FromOutput == false`), offer an extra action above the
recipe list:

> **⚡ Auto-Plan upstream** — *build the supply for this part*

Triggering it:
1. **Seeds Auto-Plan** with one target: the dragged part at the rate the source machine
   needs it — the source port's solved input rate (parts/min). If the node is unsolved or
   that rate is 0, seed one machine's worth as a starting value.
2. **Opens the Auto-Plan panel** pre-filled (target part + rate), using the user's existing
   global planner settings (disabled recipes, tier cap, ore conversion, resource
   preference, byproduct mode) exactly as `OnPlanRunClicked` builds them. The user can
   tweak the rate/options, then **Plan factory** as normal (background solve → draft
   review → apply).
3. **Records a "wire into this port" intent** for the drag's source `(Node, Part)`. When
   the resulting plan is applied, after `BuildPlanOnCanvas` materializes it, the plan's
   producer(s) of the dragged part are connected to the source machine's input — as part
   of the same grouped, undoable batch — so the synthesized chain is hooked up, not left
   floating.
4. Places the plan **near/left of** (upstream of) the source node where practical, instead
   of the default "to the right of all existing content," so the wire-in reads naturally.

**Scope.**
- Backward (input-port) drags only. Output-port drags filter to *consumers*
  (`FromOutput == true`); the planner synthesizes producers for a target, not consumers of
  a surplus, so the action is **hidden** there. (Noted, not built: a forward "plan a chain
  that uses this output" has no planner model today.)
- Pre-seeding the existing panel + draft flow is the chosen path (reuses all planner UI,
  options, and the apply/discard safety). A one-click "plan immediately with current
  settings" fast path is an optional later enhancement, not v1.

## Implementation

- **Carry the intent.** Extend the port-drag handoff so the page can stash a pending
  "auto-plan + wire-in" request: the `PortDragContext` (`Node`, `Part`) plus the inferred
  rate. Reuse `PortDragContext`; add a nullable field on `MainPage` (e.g.
  `_planWireBackInto`) set when the action fires and cleared on apply/discard/cancel.
- **Chooser action.** In `MainPage.xaml`, add the button to the chooser's filter row
  region (visible only while `ChooserFilterRow.IsVisible` *and* the pending drag is a
  backward/input case). Handler: read `_pendingPortConnect`, close the chooser, seed the
  Auto-Plan targets list (clear + one `AddPlanRow` with the part and rate), show
  `AutoPlanPanel`, and store the wire-in intent.
- **Rate inference.** Use the source node's solved per-minute demand for `Part` from the
  editor's solve result; fall back to one machine's input rate (from the recipe /
  `NodeProfile`) when unsolved/zero. The seeded rate is editable in the panel, so a rough
  default is acceptable.
- **Wire-in on apply.** `BuildPlanOnCanvas` already wires the plan internally in one
  suspended, grouped batch; when a wire-in intent is present, within that same group also
  `Connect` each planned producer of the target part to the source node's input port, then
  clear the intent. Placement origin biased to the left of the source node.
- No planner or solver concept changes — this is gesture → existing `PlanRequest` →
  existing build, plus one extra connect.

## Tests

- `FactoryEditorTests` / canvas tests: applying a port-seeded plan connects the target
  part's producer to the source node's input, as a single undo step that also unwinds the
  whole plan.
- Rate inference: a source machine consuming 120/min of the part seeds a 120/min target.
- The action is hidden for output-port drags. Panel seeding + UX verified by `/run`.

## Acceptance criteria

1. Dragging out of an **input** port onto empty canvas shows an "Auto-Plan upstream"
   action in the chooser; picking it opens Auto-Plan pre-filled with the part and the
   machine's required rate.
2. Applying the resulting plan builds the upstream chain **and** wires its output into the
   source machine's input, as one undo step.
3. The action does not appear for output-port drags; a plain recipe pick still behaves
   exactly as today.
