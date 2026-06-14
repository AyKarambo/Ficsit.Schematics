# Catalog restructure: category base classes & subfolders (#7, #8)

> **Status: ✅ Implemented.**

## Problem

`Ficsit.Schematics.Core/GameData/Catalog/` is flat and lightly structured:

- `Machines/` — ~30 classes, every one inheriting the catch-all `MachineBase`,
  which carries the union of all machine concerns (generation power, consumption
  power, somersloop slots, …) regardless of category.
- `MultiMachines/` — separate family classes (`MinerFamily`, `OilExtractorFamily`,
  `AWESOMESinkFamily`, …) inheriting `MultiMachineBase`, duplicating identity that
  conceptually belongs to the machine (Miner Mk.1/2/3 exist three times in
  `Machines/` *plus* once as a family).
- `Recipes/` — hundreds of files in one flat folder.

All of it is **generated** by `tools/generate-catalog.ps1` from
`tools/game_data.json` (the app never reads the JSON at runtime; discovery is via
reflection in `GameDataCatalog`). So this restructure is primarily a **generator
change** plus a handful of hand-written base classes. Generated files must never
be hand-edited.

> Working-tree note (2026-06-12): the 8 "modified" catalog files in git status are
> line-ending churn only (`autocrlf=true`, LF in index / CRLF on disk) — no real
> edits to reconcile. Consider adding `*.cs text eol=lf` to `.gitattributes` while
> touching the generator, so regeneration stops producing phantom diffs.

## Decided behavior (interview 2026-06-12)

Real base classes per category, **plus** merging the MultiMachine families into
the machine classes, **plus** subfolders (#8). Runtime stays untouched: the
catalog still produces the same `MachineDefinition` / `MultiMachineDefinition` /
`RecipeDefinition` objects, so solver, planner, UI and serialization see no
difference.

### Category base classes (hand-written, in `Catalog/`)

All inherit `MachineBase` so `GameDataCatalog`'s reflection discovery keeps
working unchanged. Each carries only the surface its category needs; shared
power/cost plumbing stays on `MachineBase`.

| Base class | Machines | Category-specific surface |
|---|---|---|
| `ExtractorBase` | Miner (Mk.1–3), Oil Extractor, Water Extractor, Resource Well Pressurizer + Extractor | variants (marks), capacities (purity / belt), map-snappable resource kind |
| `GeneratorBase` | Biomass Burner, Coal-Powered, Fuel-Powered, Geothermal, Nuclear Power Plant, Alien Power Augmenter | power production, fuel handling |
| `SmelterBase` | Smelter, Foundry | (organizational; production surface) |
| `ProductionBase` | Constructor, Assembler, Manufacturer, Refinery, Packager, Blender, Particle Accelerator, Converter, Quantum Encoder, Biochemical Sculptor | somersloop slots, recipe production |
| `StorageBase` | Storage Container, Industrial Storage Container, Fluid Buffer, Industrial Fluid Buffer | capacity, fullness modes |
| `SpecialBase` | Space Elevator, AWESOME Sink, Dimensional Depot Uploader, FICSMAS Gift Tree | one-offs |

Category assignment lives as a `Category` field per machine in
`tools/game_data.json` (explicit data beats name heuristics in the script).

*Proposed placements to confirm during implementation:* Alien Power Augmenter →
Generators (it boosts grid power); AWESOME Sink & Dimensional Depot → Special
(they already have special `NodeKind` handling in `NodeLayout`). Move freely if
it reads better — placement is data, not code.

### Family merge (kills `MultiMachines/` + redundant marks)

`MinerMk1/Mk2/Mk3` + `MinerFamily` collapse into **one generated `Miner` class**
(an `ExtractorBase`) declaring its variants (Mk.1/2/3 with per-mark stats) and
capacities (purity/belt) — same for Oil Extractor, AWESOME Sink belt marks,
storage mark pairs, and the other families. `MultiMachineBase` and the
`MultiMachines/` folder disappear; the per-family defaults it carried
(`ShowPpm`, `AutoRound`, `DefaultMax`) move onto the merged classes.
`ToDefinition()` on the merged class emits both the `MachineDefinition`s (one per
variant) and the `MultiMachineDefinition`, so `GameDatabase` and everything
downstream (Machine Defaults settings, `FactoryEditor.AddNode` family lookup)
keep working against unchanged runtime types.

### Folder layout (#8)

```
Catalog/
  MachineBase.cs, ExtractorBase.cs, GeneratorBase.cs, ...   (hand-written)
  Machines/
    Extractors/   Generators/   Smelting/   Production/   Storage/   Special/
  Recipes/
    Miner/  WaterExtractor/  Smelter/  Foundry/  Constructor/  Assembler/
    Manufacturer/  Refinery/  Packager/  Blender/  ParticleAccelerator/
    Converter/  QuantumEncoder/  BiomassBurner/  ...  (one folder per machine)
  Parts/          (unchanged)
```

Each recipe lands in exactly the subfolder of the machine that runs it — the
data already knows this (`RecipeBase.Machine`); the generator buckets by the
sanitized machine name. Multi-output machine families bucket under the family
name (e.g. ore extraction recipes → `Recipes/Miner/`). Namespaces follow folders
(`...Catalog.Machines.Extractors`, `...Catalog.Recipes.Constructor`); reflection
discovery is assembly-wide and unaffected. The MAUI csproj globs `**/*.cs`, so no
project-file changes.

### Generator script changes

- `New-CatalogDir` cleanup must remove the old flat files *and* recurse subfolders
  (it currently wipes `$dir\*.cs` non-recursively) — one-time migration deletes
  `MultiMachines/` entirely.
- Emit categories/subfolders from the new `Category` field; fail loudly on a
  machine without a category or a recipe whose machine has no folder.
- Also emit the `IsManuallyGathered` part flag required by
  [planner-recipe-control.md](planner-recipe-control.md) — same JSON+generator
  touch, do them in one pass.

## Recommended model & effort

**Sonnet 4.6, medium effort.** High-volume but mechanical: PowerShell generator
edits, JSON data tagging, and file churn, with a snapshot-equivalence test as the
safety net (written *first* — it converts the whole restructure from "careful"
to "checkable"). The only design-sensitive piece is the family-merge expansion in
`GameDataCatalog`; if that step fights back, escalate just that step to Opus 4.8.
Not Haiku: the family/variant semantic mapping needs more care than pure
boilerplate.

## Implementation plan

### Phase 0 — Safety net (before touching anything)
1. Snapshot test in `GameDataTests`: serialize every
   `MachineDefinition`/`MultiMachineDefinition`/`RecipeDefinition`/
   `PartDefinition` from `GameDataCatalog` to canonical ordered JSON; commit the
   fixture. This is the equivalence oracle for the whole restructure (criterion
   2). Include definition *order* (SortIndex) in the snapshot.
2. Add `*.cs text eol=lf` (or repo-wide `* text=auto eol=lf`) to
   `.gitattributes`; renormalize so regeneration stops producing phantom diffs.

### Phase 1 — Data
3. `tools/game_data.json`: add `"Category"` to each of the 32 machines
   (Extractors / Generators / Smelting / Production / Storage / Special, per the
   table above). Add `"IsManuallyGathered"` part flags while in the file (shared
   with [planner-recipe-control.md](planner-recipe-control.md) Phase 1).
4. The 7 `MultiMachines` entries already reference their member machines
   (`Machines` variant list + `Capacities`) — no JSON shape change needed for
   the merge; the generator joins families to machines by variant name.

### Phase 2 — Hand-written base classes
5. `Catalog/ExtractorBase.cs`, `GeneratorBase.cs`, `SmelterBase.cs`,
   `ProductionBase.cs`, `StorageBase.cs`, `SpecialBase.cs` — all
   `: MachineBase`, so reflection discovery is untouched. Family surface
   (`ShowPpm`, `AutoRound`, `DefaultMax`, `Variants`, `Capacities`) lives on the
   bases that need it (`ExtractorBase`, plus the storage/sink cases) as virtual
   members with empty defaults.
6. Merged family classes must yield *both* definition kinds: give the bases
   `IEnumerable<MachineDefinition> ToMachineDefinitions()` (default: the single
   `ToDefinition()`) and `MultiMachineDefinition? ToFamilyDefinition()` (default
   null). `GameDataCatalog` switches to these and must preserve SortIndex
   ordering — variant entries carry their original machine SortIndex.

### Phase 3 — Generator rewrite (`tools/generate-catalog.ps1`)
7. Machines section: emit into `Machines/<Category>/`, base class from category;
   namespace follows folder. A machine belonging to a family is **not** emitted
   standalone; instead one class per family (e.g. `Miner`) under its category,
   declaring per-variant stats (name, SortIndex, power, shards, cost) and the
   family block (ShowPpm/AutoRound/DefaultMax/Capacities). Drop the
   `MultiMachines` section entirely.
8. Recipes section: bucket by sanitized `recipe.Machine` into
   `Recipes/<Machine>/`; family-machine recipes (ore extraction) bucket under
   the family name (`Recipes/Miner/`). Fail loudly: machine without `Category`,
   or recipe whose machine resolves to no folder → generator error, not a guess.
9. `New-CatalogDir`: recursive cleanup (`Remove-Item -Recurse` of generated
   subtrees), and one-time deletion of the old flat files + `MultiMachines/`.

### Phase 4 — Catalog plumbing
10. `GameDataCatalog`: consume the new expansion methods; delete
    `MultiMachineBase` once nothing inherits it. Runtime types
    (`MachineDefinition`, `MultiMachineDefinition`, `GameDatabase`,
    `MultiMachinesByName`, `MultiMachineFor`) stay byte-compatible — solver,
    editor, Machine Defaults settings untouched.

### Phase 5 — Verify
11. Regenerate → build → **snapshot test must pass unchanged** → full suite →
    regenerate again → `git status` clean (idempotence, criterion 1).
12. `/run` smoke: place a Miner via chooser and via map, check Machine Defaults
    settings page lists the same families as before.

## Acceptance criteria

1. `pwsh tools/generate-catalog.ps1` is idempotent: run twice → zero diff; the
   build compiles; the full test suite passes untouched.
2. `GameDatabase` content (machines, multi-machines, recipes, parts and all their
   definition values) is **identical** before/after the restructure — verified by
   a test that snapshots definitions from the old catalog commit.
3. `MultiMachines/` folder and `MultiMachineBase` are gone; Machine Defaults
   settings and node placement defaults behave exactly as before.
4. Every machine file lives in a category subfolder; every recipe file lives in
   its machine's folder; no flat leftovers.
5. No generated file is hand-edited (header comment preserved); category changes
   require only a JSON edit + regeneration.
