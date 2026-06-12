# Map mode: extractor size & node interaction (#4)

## Problem

On the world map (7500-unit canvas, `Canvas/MapGeometry.cs`), resource node markers
have radius 18 (36 units across) while a placed extractor renders as the standard
node card — `NodeLayout.CardWidth = 110` plus value/limit rows. A snapped miner is
roughly **3× wider than the marker it sits on**, so in clusters (classic map has
many node pairs/triples well under 110 units apart) one placed miner visually
buries its neighbors, and dropping a second miner near the right marker is a
guessing game. Snapping (`CanvasController` ≈ line 405) picks the nearest matching
free marker within `SnapRadius = 150` measured from the **node center**, which is
ambiguous when the card covers several markers.

## Decided behavior (interview 2026-06-12)

The user wants the extractor to "match the node size in a clever way" — but it
cannot become a bare marker icon because clock speed and miner mark must stay
editable, and the output port must remain draggable. Plus one new gesture:
dragging out of a resource node directly to create/connect machinery.

### 1. Compact map-mode extractor card

When an extractor node is snapped to a resource node (`ResourceNodeId != null`)
**and the map is shown**, render a compact badge instead of the full card:

- Footprint close to the marker size (target ≈ 40–48 units square, vs 110 today),
  centered on the marker.
- Contents: machine icon, and one condensed value line (machine count or ppm —
  whatever the node's `ShowPpm` resolves to). Clock/mark/purity details live in
  the editor popup, not on the badge.
- One output port on the right edge, same `PortInfo` hit-testing as today
  (`PortSize` may shrink in this layout; keep a comfortable touch target by
  hit-testing a padded rect).
- Tap/click opens the same machine editor popup as the full card — nothing about
  editing changes.
- Unsnapped extractors (no `ResourceNodeId`) keep the full card so they're easy
  to grab and place.
- Implementation: a map-compact branch in `NodeLayout.Compute` (like the existing
  `SpecialtySize` branch for outposts/blueprints) + matching draw path in
  `FactoryCanvasDrawable`.

### 2. Snap by pointer, free markers only

While dragging an extractor in map mode, the snap candidate is the matching,
unoccupied marker nearest the **pointer position** (not the card center). The
occupied-marker exclusion already exists (`state.OccupiedResourceNodes()`); the
change is the reference point plus a snap-preview highlight on the candidate
marker so the user sees which node will receive the miner before releasing.

### 3. Drag out of a resource node

New gesture: starting a drag **on an unoccupied resource marker**:

- Creates the appropriate extractor for that resource (variant/purity defaults
  from Machine Defaults settings), snapped to the marker, and
- continues as an output-port connection drag from the new extractor — identical
  to today's port-drag flow: release on a machine's input connects; release on
  empty canvas opens the recipe chooser with the producer/consumer port filter
  (`RecipeChooserViewModel.SetPortFilter`) pre-set to the extracted part.
- Undo removes both the connection and the created extractor (single undo group
  via the existing `EditCommand` coalescing).
- Drag on an **occupied** marker does nothing special (the existing node on top
  owns the gesture).

## Recommended model & effort

**Opus 4.8, medium effort** — high for Phase 3 (the gesture state machine).
Canvas geometry and rendering are moderate-complexity, visually verifiable work;
the one subtle piece is threading the new "drag from marker" branch through
`CanvasController`'s pointer modes without breaking pan/move/connect/rubber-band.
Verify with `/run` and the acceptance scenarios rather than relying on tests
alone — most of this is interaction feel.

## Implementation plan

### Phase 1 — Compact map card
1. `NodeLayout`: add a compact branch in `Compute` (pattern: the existing
   `SpecialtySize` branch for outposts). Trigger: `node.Kind == Recipe`,
   `node.ResourceNodeId != null`, and a new `bool mapCompact` parameter the
   caller passes (the drawable knows `Settings.ShowMap` and scope). Geometry:
   ~44–48 unit square + one value chip row; single output port on the right
   edge centered; `PortInfo` hit rect padded to ≥ 20 units for touch.
2. `FactoryCanvasDrawable`: draw path for compact layouts — machine icon,
   condensed value chip (count or ppm via `NodeResult.DisplayValue`), selection
   ring. Existing hit-testing works unchanged since it goes through
   `NodeLayout.Bounds`/`HitPort`.
3. Snap centering: `SnapToResourceNode` offsets by `CardWidth/2` /
   `ImageAreaHeight/2` (lines ~433–437) — switch to half the *actual* layout
   size so compact cards center on the marker.

### Phase 2 — Pointer-based snap + preview
4. `CanvasController.SnapToResourceNode` currently measures from the node
   center; pass the current pointer world position (tracked in `PointerMoved`)
   and use it as the distance reference during drags. Extract the
   candidate-selection loop (matching + free + nearest) into a pure static
   helper so it's unit-testable.
5. Live preview: while in `Mode.DragNodes` over the map with a single extractor
   selected, compute the candidate each move and set
   `drawable.SnapPreviewMarker`; draw a highlight ring on that marker. Clear on
   release/cancel.

### Phase 3 — Drag out of a resource node
6. `PointerPressed`: when no node/port is hit, map is shown, root scope, left
   button — hit-test markers (radius `MarkerRadius` + padding) for a *free*
   marker; remember it as `_pressMarker` (do not disturb pan-on-drag for misses).
7. On exceeding the drag threshold with `_pressMarker` set: create the matching
   extractor — marker kind/part → recipe (inverse of `MatchesMapNode`: ore part →
   that part's Miner recipe, oil → Oil Extractor recipe, geyser → Geothermal,
   fracking core → Pressurizer), variant from Machine Defaults
   (`DefaultMachine`), snap + purity adoption via the existing snap routine —
   then enter `Mode.Connect` with the new node's output port as `_pressPort`.
8. Release: on an input port → connect (existing path); on empty canvas →
   `OpenChooserForPort` with the producer filter (existing path). Cancel (Esc /
   zero-length) removes the created node.
9. Undo grouping: the node-add, snap, purity, and connection commands must undo
   as one step — verify `FactoryEditor.Commands` coalescing supports this;
   otherwise add a composite/grouping push to `CommandStack` (small, contained
   change).

### Phase 4 — Pressurizer & polish
10. Resource Well Pressurizer: compact card centers on the fracking core;
    satellite markers remain plain markers (extractor recipes for satellites
    keep the standard flow).
11. Zoom behavior: below the label-legibility threshold (shared rule with
    [ui-readability-ux.md](ui-readability-ux.md) #3), the compact card drops its
    value chip; hover shows the tooltip (drawable already has a tooltip path).

### Phase 5 — Verification
12. Unit tests: compact `NodeLayout` geometry; snap-candidate helper (cluster of
    3 markers, occupied exclusion, pointer-nearest wins).
13. `/run` checklist = the five acceptance scenarios below, in both themes, plus
    regression sweep of normal canvas gestures off-map (pan, move, connect,
    rubber-band, double-click chooser).

## Open questions

- Should the compact badge hide its value line below a zoom threshold (map zoomed
  far out) and show it as hover tooltip instead? Suggest yes, threshold shared
  with the port-label zoom rules from [ui-readability-ux.md](ui-readability-ux.md).
- Resource Well Pressurizer covers multiple satellite nodes — compact rendering
  for the pressurizer should center on the well core; satellites stay markers.

## Acceptance criteria

1. Two pure iron markers 60 units apart: place miners on both; each badge centers
   on its marker, both markers' badges are individually clickable, neither
   obscures the other's marker.
2. While dragging a miner across a cluster, the highlighted snap target is always
   the free matching marker nearest the pointer.
3. Snapped miners open the editor popup; clock speed and miner mark remain fully
   editable; output port drag works from the compact badge.
4. Dragging from an empty marker to a Smelter input creates a snapped miner wired
   to the Smelter in one gesture; one Ctrl+Z removes both.
5. Dragging from an empty marker to empty canvas opens the recipe chooser filtered
   to consumers of that resource.
