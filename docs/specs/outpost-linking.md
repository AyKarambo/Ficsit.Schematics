# Outpost linking: crash fix + side-aware connections (#14)

## Problem

### A. App crash (the urgent part)
`BasicSolver.Solve` builds its `states` map from `document.Root.AllNodes()` **filtered to
exclude** `Outpost`/`Blueprint` (`Ficsit.Schematics.Core/Solver/BasicSolver.cs:40-42`), but
`connections` is `AllConnections()` — **every** connection, including any drawn between a
parent-scope node and an outpost. The flow passes then index the map unconditionally:
`AllocateFlow` does `states[connection.From]` / `states[connection.To]` (:172-173), and so
do `ProducerOffer`/`ConsumerRequest`/`DistributeRequest`. Any connection whose endpoint is
an outpost/blueprint ⇒ **`KeyNotFoundException`**, crashing the solve. This is the crash the
user hits "when I try to connect something new to an outpost."

`NodeProfile.Build` already has an (empty) `Outpost`/`Blueprint` case
(`Solver/NodeProfile.cs:96-99`) — outposts simply were never added to `states`.

### B. Drop side is ignored
`CanvasController.TryResolveEndpoints` (:305-347) decides producer/consumer **only** from
the *pressed* port's side (`pressIsOutput`) and ignores which side of the **target** was
hit. An empty outpost exposes both an input `"AnyPart"` stub (left edge) and an output
`"AnyPart"` stub (right edge) via `AddDynamicPorts` (`Canvas/NodeLayout.cs:189-207`), but the
resolver can't tell which one the user aimed at, so left-vs-right has no effect and there is
no visual hint that both are valid targets.

## Decided behavior (interview 2026-06-13 — outposts & blueprints only)

### 1. Fix the crash by solving outposts as open boundary nodes
Stop excluding `Outpost`/`Blueprint` from `states`, and give them a profile that both
absorbs inputs and provides outputs with unbounded capacity at the parent scope — exactly
the existing "open source + open sink" shape used by default `StorageContainer`
(`NodeProfile.cs:77`). Concretely in `NodeProfile.Build`'s outpost case (:96-99):
`profile.IsOpenSource = profile.IsOpenSink = true; profile.IsPpmDisplay = true;`. Then in
`BasicSolver.Solve` (:40-42) include outpost/blueprint nodes. They flow through the existing
throughput path (`state.IsThroughput` is set from `IsOpenSink || IsOpenSource`,
:52-54; `ComputeCount`/`BuildResult` throughput branches handle it), so exterior connections
resolve and the **outpost ports display real flow**. `UpdateStatus` already skips
outpost/blueprint for the machine count, so the status bar is unaffected.

> Minimal fallback if the above proves risky: make every `states[...]` endpoint access
> null-safe and skip connections whose endpoint isn't in `states`. This stops the crash but
> leaves outpost ports flowless — prefer the open-boundary approach.

### 2. Side-aware linking (left = input, right = output)
For an **outpost/blueprint endpoint**, the side the pointer is over decides the outpost
port's direction, which fixes the connection direction (always producer → consumer):
- **Left half = outpost input:** connection is `otherNode → outpost`; the other endpoint
  must **produce** the part.
- **Right half = outpost output:** connection is `outpost → otherNode`; the other endpoint
  must **consume** the part.
- The part name is adopted from the **non-outpost** endpoint's port (outpost stubs are
  `"AnyPart"`). Reuse the existing `"AnyPart"` adoption in `TryResolveEndpoints` (:320-327).
- A contradictory combination (e.g. dropping a producer's output onto the outpost's *output*
  side) is rejected — no connection is created (the drop-zone hint, below, steers the user
  to the valid side).

Implementation: `HitPort` already returns the left stub for a left-edge hit and the right
stub for a right-edge hit (it checks `Inputs` before `Outputs`, and the stubs sit on
opposite edges), so the hit side is encoded in `targetPort.IsInput`. The change is to make
`TryResolveEndpoints` **use `targetPort.IsInput`** to set direction when the target is an
outpost/blueprint, instead of relying solely on the pressed side. As a robustness guard when
a stub isn't precisely hit, fall back to comparing the pointer X to the node's
`Bounds.Center.X` (left → input, right → output).

### 3. Initiate from an outpost
Pressing the outpost's **left** stub starts an input-seeking wire (the outpost is the
consumer); the **right** stub starts an output-providing wire (the outpost is the producer).
The press path already captures `_pressPort` (`CanvasController.PointerPressed` :56), and the
existing `pressIsOutput` direction logic already yields the right result for these two
cases — once the crash is fixed, completing such a drag onto a compatible recipe port works.
Verify both directions and that the adopted part is the recipe port's part.

### 4. Visual drop-zone affordance
While a pending connection wire hovers an outpost/blueprint, highlight its **left half**
(input drop zone) and **right half** (output drop zone) — e.g. two tinted halves with the
appropriate one emphasized as the pointer crosses the centre line — so it's clear both are
valid and which will be chosen. Draw it in `FactoryCanvasDrawable.DrawAdorners` (where the
pending wire is already drawn), gated on `PendingWire is not null` and the hovered node being
an outpost/blueprint.

## Recommended model & effort

**Opus 4.8, high.** Two subtle pieces: the solver-semantics change (outpost as open
source/sink without disturbing interior solving or existing canvases) and the side-aware
gesture/validation. Add a solver regression test and verify visuals by `/run`.

## Implementation plan

1. **Profile** — fill the `Outpost`/`Blueprint` case in `NodeProfile.Build` (:96-99) with
   `IsOpenSource = IsOpenSink = true; IsPpmDisplay = true;`.
2. **Solver** — in `BasicSolver.Solve` (:40-42) include outpost/blueprint nodes in `nodes`
   (drop the exclusion). Confirm the throughput branches in `ComputeCount` (:108-124) and
   `BuildResult` (:321-361) produce sane flows; outpost ports get `PortResult`s.
3. **Regression test** — new test in `Ficsit.Schematics.Tests`: a recipe connected to an
   outpost (and an outpost connected to a recipe) solves **without throwing** and yields a
   non-zero flow on the connection. Guards the crash forever.
4. **Side-aware resolve** — in `TryResolveEndpoints` (:305-347): when an endpoint is
   outpost/blueprint, derive its role from `targetPort.IsInput` (fallback: pointer-X vs
   `Bounds.Center.X`); set producer/consumer accordingly; adopt the part from the
   non-outpost port; reject contradictory pairings.
5. **Drop-zone visual** — `FactoryCanvasDrawable.DrawAdorners`: when `PendingWire` is over an
   outpost/blueprint, draw left/right half highlights with the active side emphasized.
6. **Verify** — `/run`: connect recipe output → outpost left (input) and recipe input ←
   outpost right (output); initiate drags from both outpost stubs; confirm no crash, correct
   direction, visible drop zones, and that outpost ports show flow; reload an existing
   document and confirm it still solves.

## Update (interview 2026-06-14): implemented as full pass-through

After testing, the user chose the **true sub-factory** model over the independent open
boundary. Implemented:
- New `NodeKind.Import`/`Export` boundary nodes live inside an outpost's `Children` (Name =
  the part). Import provides its part to the interior (output port); Export consumes it.
- `BasicSolver` excludes the outpost/blueprint **container** and reroutes every connection
  that touches it onto the matching boundary node (Import for incoming, Export for outgoing);
  boundary nodes are pass-through, so **flow crosses the boundary and numbers match**.
- Exterior outpost ports derive from the boundary nodes (plus exterior connections).
- Wiring to/from an outpost (from outside) auto-creates the boundary node (`FactoryEditor.
  Connect` → `EnsureBoundaryFor`). Dragging an interior machine port to empty canvas inside
  an outpost creates a boundary (`AddBoundaryNode`) — "add from inside". Deleting a boundary
  node removes its exterior connections. Old saves migrate on load (`EnsureOutpostBoundaries`).
- `Kind` is serialized for boundary nodes. Covered by solver + serializer + editor tests.

## Open questions (resolved)

- ~~Should an outpost's exterior ports be tied to its interior production, or remain
  independent open boundaries?~~ Resolved: **full pass-through** (see Update above).
- Blueprints behave identically to outposts here; confirm no blueprint-specific port rules
  are needed.

## Acceptance criteria

1. Connecting a recipe **to** an outpost and **from** an outpost no longer crashes; the
   regression test passes.
2. Dropping a wire on an outpost's **left** half adds it as an **input**; on the **right**
   half, as an **output**; a contradictory drop is rejected.
3. A visible drop-zone hint appears while a wire hovers an outpost, indicating the two sides
   and the active one.
4. Dragging a wire **out from** an outpost works from both the left (input) and right
   (output) stubs.
5. Outpost ports display flow; existing saved canvases still solve unchanged.
