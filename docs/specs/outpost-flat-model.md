# Outposts as flat groups ("bracket"), matching the reference (#16)

> **Status: 🚧 In progress — active rework on branch `outpost-flat-model`. The flat `Parent` model has landed; removal of the `NodeKind.Import`/`Export` + `EnsureBoundary` mechanism is still pending.**

Supersedes the nested/boundary-node approach in [outpost-linking.md](outpost-linking.md). Goal
(user, 2026-06-14): an outpost is **a completely normal factory, just with a bracket for
overview** — exactly like the reference app *Satisfactory Modeler*.

## Reference model (reverse-engineered from the running app + its save format)

Confirmed by launching the reference, entering an outpost, and round-tripping a crafted save:

- **One flat node list.** The document's `Data` array holds *all* nodes; an **outpost is just a
  node** (`Name:"Outpost"`) in that list — there is **no nested sub-graph**.
- **Membership via `Parent`.** A node carries a `Parent` = the outpost it belongs to (index in
  the flat list); root-level nodes have none. Verified: a node with `Parent:1` is hidden at
  root → membership is honored.
- **Active scope is a view filter.** A document-level field **`"Outpost":<index>`** records which
  outpost you're currently *viewing* (absent = root). Entering sets it; the back-arrow clears it.
- Each outpost node has its own **`Zoom`/`PanX`/`PanY`** (interior view).
- **Connections are normal** node→node refs in the flat list and **cross the boundary freely**.
  The reference does *not* auto-draw a raw cross-ref to a hidden member; it shows the boundary
  via the outpost box's ports (it stores an `InteriorInputs` helper on the node — exact JSON not
  needed for us, see below).
- **Enter** via the node context menu **"Open"** (menu also has Title, **Blueprint** toggle,
  Cut/Copy/Paste/Delete). Inside: a **back-arrow** (top-left) exits; **"+"** affordances
  top-left / top-right add an input / output boundary. A **Blueprint** is an outpost flagged for
  duplication. Solver scope options: "Current Outpost" / "Current Outpost & Below".

## Target behavior for our app

An outpost groups normal nodes for overview. Root view shows the outpost as a **box with the
parts that cross its boundary as ports**; entering shows its members as a normal factory with
the crossing connections drawn to **edge boundary markers**. Numbers cross naturally (the solver
already solves the whole flat graph). No separate Import/Export machine nodes.

## Implementation plan (full rework)

### Remove (revert the divergent approach)
- `NodeKind.Import`/`Export`; `FactoryEditor.EnsureBoundaryFor`/`AddBoundaryNode`/
  `EnsureOutpostBoundaries`; the `BasicSolver` outpost reroute/pass-through; the boundary-badge
  rendering and drop-zone code added for the nested model.

### Model — flatten
- `Ficsit.Schematics.Core/Model/FactoryNode.cs`: add `FactoryNode? Parent` (the containing
  outpost; null = root). Remove `Children` usage.
- `FactoryGraph`: a single flat graph (`Document.Root`) holds **all** nodes and connections.
  `AllNodes`/`AllConnections` become the lists themselves.

### Editor — scope as filter
- `FactoryEditor`: replace `CurrentScope`/`ScopePath`/`EnterOutpost`/`LeaveOutpost` with
  `ActiveOutpost` (FactoryNode? — null = root) + `EnterOutpost(o)`/`LeaveOutpost()` that just set
  it. "Current scope" = nodes with `Parent == ActiveOutpost`. `AddNode` sets
  `Parent = ActiveOutpost`. Connect/Disconnect operate on the one flat connection list. Deleting
  an outpost deletes (or re-parents) its members.

### Serialization — flat + Parent + active scope
- `SfmdSerializer`: write a flat `Data` with `Parent` (index) per node and the document-level
  `Outpost` (active index). Read both. **Migration:** existing nested-`Children` saves (our old
  format) flatten into the list with `Parent` set; reference saves load directly.

### Rendering — scope filter + boundary
- `FactoryCanvasDrawable`: draw only nodes with `Parent == ActiveOutpost`. At root, an outpost
  renders as a box; its ports = the distinct parts on connections crossing its boundary (a
  connection from a root node to a node whose `Parent` is that outpost). A crossing connection is
  drawn root-node → outpost-box. Inside an outpost, a connection to a node outside the active
  scope is drawn member → **edge boundary marker** (left = incoming, right = outgoing). Boundary
  ports/markers are **auto-derived from the crossing connections** — no stored `InteriorInputs`
  needed (this sidesteps the one reference detail we didn't byte-capture while matching the UX).
- `CanvasController`: hit-testing/connect/gestures operate on the visible (active-scope) nodes;
  dropping a wire on an outpost box (root) or an edge marker (inside) creates the crossing
  connection. Keep enter-on-Open and a back affordance.

### Solver — already flat
- `BasicSolver` solves the whole flat graph (it already does). Scope is view-only. (The earlier
  reroute/pass-through is removed.)

### UI
- Outpost context menu: **Open** + **Blueprint** toggle (mirror the reference). Keep the
  breadcrumb/back navigation that already exists.

## Recommended model & effort

**Opus 4.8, high.** Large rearchitecture touching model, serialization, editor scope, rendering,
gestures, and solver, plus migration of old saves. Do it as one coherent change (the model swap
breaks compilation until finished). Cover model + serialization + scope-filter + migration with
unit tests; verify the canvas UX with `/run`.

## Acceptance criteria
1. Outposts are normal nodes in one flat list; members carry `Parent`; entering filters the view;
   the back-arrow returns to root.
2. Root shows an outpost as a box whose ports are the parts crossing its boundary; crossing
   connections draw to the box.
3. Inside an outpost, members render as a normal factory; connections to outside draw to left
   (in) / right (out) edge markers; you can add/remove them from inside.
4. Numbers cross the boundary (whole-graph solve); save/reload round-trips; old nested-format
   saves migrate.
5. No Import/Export machine nodes; the nested-`Children` model is gone.
