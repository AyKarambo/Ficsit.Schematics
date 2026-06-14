# Import built factories from a save — onto the world map

> **Status: 📋 Planned — not yet implemented. Large, phased initiative (see the phasing below); Phases 0–1 are the first shippable slice.**

> **The vision (user, 2026-06-14):** load an actual game save and have *my real
> factories* already on the canvas, at the exact world locations they occupy in-game,
> snapped to the resource nodes they extract from — grouped into outposts, with the
> train / truck / drone networks drawn as the links between them.

This is the most ambitious feature in the backlog. It is **explicitly phased**: each phase
is independently shippable and delivers visible value on its own, so we never bet
everything on the hardest part landing. Phases 0–1 already produce the "wow" (my machines,
on the map, at their real spots); 2–4 are progressive enrichment.

## Where we are today

`SatisfactorySaveReader` is **deliberately surgical**: it decompresses the zlib chunk body
and scans for a handful of *actor headers* (resource nodes, geysers, fracking) plus two
override properties (`mResourceClassOverride`, `mPurityOverride`), correlating them by
serialization order. It does **not** parse the full save schema, and that's by design —
it's robust across save versions precisely because it reads almost nothing.

Reconstructing built factories needs the opposite: the **object data / property tags** of
every building actor (its class, transform, recipe, clock, sloops) and the
**connection graph** (belts/pipes, logistics). So Phase 0 is a real parser upgrade, not an
extension of the scanner. We should lean on the documented format and an existing
battle-tested parser rather than reverse-engineer from zero:

- Format reference: [satisfactory-3d-map `SATISFACTORY_SAVE.md`](https://github.com/moritz-h/satisfactory-3d-map/blob/master/docs/SATISFACTORY_SAVE.md)
- Reference parsers: [etothepii4/satisfactory-file-parser](https://github.com/etothepii4/satisfactory-file-parser) (TS),
  [GreyHak/sat_sav_parse](https://github.com/GreyHak/sat_sav_parse) (Py). C# prior art exists too
  (Goz3rr-style save editors).

**Recommended parser strategy (decide at Phase 0):** port the object-parsing logic into the
C# reader (no runtime dependency, fits "no codegen / no runtime data file"), validating our
output against a reference parser's JSON for the same save. Alternatives — bundle a parser,
or shell out — are heavier on dependencies; note and reject unless porting proves too
costly. Pin and assert the save version (today's reader is verified against v60 / 1.1).

## Phased delivery

### Phase 0 — Object parser foundation
Extend the reader from header-scanning to parsing building actors and their key properties.
Reuse the existing chunk decompression. For each `Build_*` actor extract:
- **class** → machine (e.g. `Build_ConstructorMk1_C` → Constructor; belts `Build_ConveyorBeltMk1_C`…),
- **transform** → world X/Y/Z (cm),
- **`mCurrentRecipe`** → our recipe name,
- **`mCurrentPotential` / `mPendingPotential`** → clock speed,
- **production-shard slots** → Somersloops,
- machine **variant / belt mark** from the class (Mk tiers).

Output a UI-free `SaveWorld` model (`Buildings`, plus the `ResourceNodeInfo` we already
read). No canvas work yet; this phase is "the data is parseable and correct."

### Phase 1 — Place factories at world locations  *(first shippable "wow")*
Map each parsed building → a `FactoryNode`:
- recipe resolved from `mCurrentRecipe`; machine/variant/belt from class; `ClockSpeed`,
  `Somersloops` from properties;
- **position** = world X/Y mapped to canvas coords via the same transform `MapSnap` /
  map-mode already uses for resource nodes (top-down XY; Z ignored on the 2D canvas);
- **extractors snap to their node**: set `FactoryNode.ResourceNodeId` to the nearest
  matching `ResourceNodeInfo` (reuse `MapSnap`), the same anchor the `.sfmd` extension
  already serializes.

After Phase 1 you can import a save and see every machine sitting on the map where it is
in-game, each already running its real recipe/clock, miners snapped to their nodes — even
with no connections yet.

### Phase 2 — Conveyor / pipe connections
Recover producer→consumer edges from the belt/pipe **connection graph** (connection
components + `mConnectedComponent` references through belt/pipe actors) and materialize them
with `FactoryEditor.Connect`. This is the hardest parsing.
- **Fallback when the graph can't be recovered** (version drift, partial data): infer
  connections from spatial adjacency + recipe compatibility (the same producer/consumer
  matching `BuildPlanOnCanvas` already does), clearly **marked as inferred** so the user
  knows what's reconstructed vs. read.

### Phase 3 — Outpost clustering  *(the user's "how do I split factories?" concern)*
Group buildings into outposts so the import isn't one flat sea of machines:
- **Spatial density clustering** of building world positions (grid / DBSCAN-style) into
  outposts; cluster radius is the one tunable knob, adjustable after import.
- **Logistics endpoints are natural boundaries**: a train/truck/drone station marks the
  edge of an outpost, so seed clusters at stations and let belt-connected neighbors join
  the same outpost (a building belt-fed across a long gap stays with its station's cluster).
- Name each outpost by its dominant output or nearest resource node.
- Reuse the existing flat-parent model: `FactoryEditor.GroupIntoOutpost` +
  `FactoryAutoGroup`; the user can merge/split clusters afterward like any outpost.

### Phase 4 — Logistics networks (train / truck / drone)  *(the user's "not everything is belt-connected" concern)*
The later the game, the more is shipped by vehicles, not belts. Parse logistics actors and
draw the inter-outpost links:
- **Trains** — stations + attached freight platforms + their load/unload **item filters** →
  the commodity each station ships; pair source/destination stations on a line.
- **Trucks** — truck stations + their recorded **routes** (station pairs) and cargo filter.
- **Drones** — drone ports + their paired home/destination + filtered item.

Represent each logistics link as a **cross-outpost connection** carrying the commodity,
rendered in a distinct **logistics edge style** (train/truck/drone glyph) so vehicle hops
read differently from belts. Build it on **whatever outpost-boundary mechanism is current** —
note that the boundary model is being reworked by [outpost-flat-model.md](outpost-flat-model.md),
which auto-derives boundary markers from crossing connections rather than using
`NodeKind.Import`/`Export` nodes; this spec should target that flat model, not the
superseded Import/Export handles. Either way it's an existing mechanism applied to stations,
not a new model concept.

## Implementation notes

- **Core, UI-free:** `SaveWorld` model + parser in `Core/Saves`; clustering + logistics
  inference as pure functions (testable without MAUI). The canvas just materializes the
  result through `FactoryEditor`.
- **Reuse, don't reinvent:** `MapSnap` (coords + node snap), `ResourceNodeId` (already in
  `.sfmd`), `GroupIntoOutpost` / `FactoryAutoGroup` (outposts), the current outpost
  cross-boundary mechanism (logistics links — see the
  [outpost-flat-model.md](outpost-flat-model.md) note above), `BuildPlanOnCanvas`'s
  suspended/grouped batch pattern
  (import as one undoable step, one solve).
- **Entry point:** extend the existing Settings → *Import save* flow
  (`OnImportSaveClicked`) — today it loads only map nodes; add "import built factories"
  with a phase-appropriate scope and a clear "inferred connections" notice.
- **Mapping table:** building class → machine/variant needs a class→name map (mirrors the
  reader's `DescToPart`); derive from the catalog where possible so it tracks game data.

## Risks & honest constraints

- **Schema drift.** Full object parsing is far more version-sensitive than header scanning.
  Pin/assert the save version, validate against a reference parser's JSON, and degrade
  gracefully (fall back to Phase-1 placement + inferred connections) rather than failing the
  whole import.
- **Scale.** Late saves have tens of thousands of actors; parse off the UI thread (the
  planner already uses `Task.Run` + progress + cancel — reuse that pattern) and consider
  filtering to factory-relevant classes.
- **Fidelity is not guaranteed.** Belt graph recovery and logistics inference are
  best-effort; the UI must distinguish *read* from *inferred*, and the import is a starting
  point the user can edit, not a perfect mirror.
- **2D vs 3D.** The canvas is top-down XY; stacked/vertical builds will overlap and may need
  the auto-format/declutter pass after import.

## Tests

- Phase 0/1: a small fixture save parses to the expected building set (class, recipe, clock,
  sloops, position) and miners snap to the correct `ResourceNodeInfo`. Validate counts/values
  against a reference-parser JSON for the same save.
- Phase 2: known belt graph recovers the expected edges; the adjacency fallback produces a
  connected, recipe-valid graph and is flagged inferred.
- Phase 3: clustering splits a two-site fixture into two outposts at the expected radius;
  a station-served building lands in its station's outpost.
- Phase 4: a train fixture with an iron-ore station pair yields one logistics link carrying
  Iron Ore between the two outposts via Import/Export handles.

## Acceptance criteria (per phase)

1. **P0** — a save parses to a `SaveWorld` of buildings with class, recipe, clock, sloops,
   and world transform; output matches a reference parser for a fixture save; version is
   asserted.
2. **P1** — importing places every factory machine on the canvas at its real map location
   with its real recipe/clock/sloops, miners snapped to their nodes, as one undo step.
3. **P2** — belt/pipe connections are reconstructed where the graph is recoverable; otherwise
   adjacency-inferred connections are shown and clearly labeled as inferred.
4. **P3** — buildings are grouped into spatially sensible outposts (logistics stations as
   boundaries), named and user-adjustable.
5. **P4** — train/truck/drone routes appear as distinct inter-outpost logistics links
   carrying the right commodity, built on Import/Export handles.
