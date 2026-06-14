# Auto-Planner recipe control (#5, #6)

> **Status: ✅ Implemented.**

## Problem

1. **#5** — The planner's recipe pool is all-or-nothing: `PlanRequest.UseAlternateRecipes`
   (`Ficsit.Schematics.Core/Planning/PlanRequest.cs`) toggles *every* alternate at once.
   Users want to enable/disable individual recipes, and to initialize the alternate-recipe
   set from what is actually unlocked in their save.
2. **#6** — The LP planner happily sources biomass: with `ScarcityWeights` giving leaf
   parts a flat weight (`DefaultLeafWeight = 50`), chains like Biomass → Solid Biofuel →
   Coal or SAM ore conversion can look optimal. But biomass-family parts must be gathered
   by hand in-game, so plans built on them are unwanted by default.

## Decided behavior (interview 2026-06-12)

### Part metadata: "manually gathered" vs "infinite"

Rather than a biomass blocklist, parts get a new boolean in the catalog:

- **Infinite** (default): auto-extractable at a rate — ores, Water, Crude Oil,
  Nitrogen Gas, SAM, geyser steam. Limited per minute, never depleted.
- **Manually gathered**: must be collected by hand — Leaves, Wood, Mycelia, all
  alien remains/protein parts, power slugs, hard-drive style pickups, FICSMAS drops.

Source of truth: `tools/game_data.json` gains the flag per part;
`tools/generate-catalog.ps1` emits it on `PartBase` (e.g.
`public virtual bool IsManuallyGathered => false;`), flowing into `PartDefinition`.
(See [catalog-restructure.md](catalog-restructure.md) — do that work first or
together.)

### Planner toggles (#6)

Two switches in the Auto-Plan panel (`MainPage.AutoPlan.cs`), persisted in settings:

1. **"Exclude manually gathered parts"** — default **ON**. When ON, the planner
   must not consume any `IsManuallyGathered` part as a raw input. Implementation:
   add those parts to `PlanRequest.BannedResources` (mechanism already exists).
   Exceptions, in both cases deliberate user action:
   - a manually gathered part listed as a **Provision** is allowed up to its cap;
   - if a **Target** is itself biomass-based (e.g. "plan Solid Biofuel"), only the
     parts on its ingredient chain that the user provisions are allowed — the
     planner still won't invent free leaves.
2. **"Allow ore conversion"** — default **OFF**. Excludes Converter ore-from-SAM
   conversion recipes (recipes on the Converter machine whose output is a raw ore
   part) from the plan pool. SAM itself remains an infinite resource; this toggle
   only governs the conversion recipes, which otherwise dominate "efficient" plans.

### Recipe toggle list (#5)

A per-recipe enable/disable list controlling which recipes the planner may use:

- **Primary home: the Auto-Plan panel.** A "Recipes…" button opens the list
  in-place (same panel, swapped view or expander) — searchable, grouped by output
  part, alternates visually distinct (this pairs with the output icons from
  [ui-readability-ux.md](ui-readability-ux.md)). Checkbox per recipe.
- **Secondary: Settings page.** The same list under Settings as the global
  default set; the planner panel edits the same persisted state. (One shared
  state, two entry points — per interview: "one in the general settings is fine,
  but I need it in the planner view more.")
- Bulk actions: *All on*, *All standard / alternates off*, and **"From save"** —
  reads the selected save (existing `Ficsit.Schematics.Core/Saves` import) and
  enables exactly the alternate recipes unlocked in that save; standard recipes
  stay on.
- Default state: everything enabled (matches today's `UseAlternateRecipes = true`).
- `FactoryPlanner` consumes the list as a recipe filter on the LP column set;
  `PlanRequest` gains `HashSet<string> DisabledRecipes` (or an allowed-set —
  implementer's choice, but serialize the *disabled* set so new recipes added by
  data updates default to enabled).
- The legacy all-or-nothing `UseAlternateRecipes` switch is replaced by the list
  (a "alternates off" bulk action covers its use case).

## Recommended model & effort

Split by sub-task — they differ sharply in nature:

- **Toggles, part metadata, recipe filter, list UI: Opus 4.8, medium effort.**
  Mostly wiring through well-defined layers (JSON → generator → definitions →
  `PlanRequest` → panel UI) with existing patterns to copy; medium reasoning,
  decent volume.
- **"From save" unlocked-alternates import: Fable 5, high effort, time-boxed
  spike first.** Reverse-engineering the save's schematic-manager state and
  mapping schematic assets to recipe names is genuinely uncertain work — survey
  confirmed `SatisfactorySaveReader` parses resource nodes only.

## Implementation plan

Ordering note: Phase 1 rides on the catalog generator — do
[catalog-restructure.md](catalog-restructure.md) first or land Phase 1 as a
minimal generator patch.

### Phase 1 — Part metadata
1. `tools/game_data.json`: add `"IsManuallyGathered": true` to the hand-collected
   pickups only — Leaves, Wood, Mycelia, the four alien remains, the three power
   slugs, Mercer Sphere, Somersloop, FICSMAS drops. Crafted descendants (Biomass,
   protein, DNA capsules) need no flag: with their gathered inputs banned, the
   planner can't source them anyway.
2. `tools/generate-catalog.ps1` emits the flag on `PartBase`
   (`public virtual bool IsManuallyGathered => false;`) → `PartDefinition` →
   available via `GameDatabase.PartsByName`.

### Phase 2 — Planner core
3. `PlanRequest` gains `HashSet<string> DisabledRecipes`. Keep the request
   primitive: the two UI toggles are *mapped* onto `BannedResources` /
   `DisabledRecipes` when building the request in `MainPage.AutoPlan.cs` — the
   planner itself learns no new concepts.
4. `FactoryPlanner.CollectCandidateRecipes` (~line 294): add
   `if (request.DisabledRecipes.Contains(recipe.Name)) continue;` and delete the
   `UseAlternateRecipes` all-or-nothing check (property removed; "alternates
   off" becomes a bulk action on the list).
5. **Bug-order fix found in survey:** in the external-supply loop
   (`FactoryPlanner` ~line 114) `BannedResources` skips a part *before* checking
   `provided` — a banned-but-provisioned part gets no supply column. Change to
   `if (banned && !provided) continue;` so provisions override the ban (spec
   exception 1).
6. Toggle mapping when building the request:
   - *Exclude manually gathered* (default on) → every `IsManuallyGathered` part
     into `BannedResources`.
   - *Allow ore conversion* (default off) → when off, every recipe with
     `Machine == "Converter"` whose output is an extractable raw (a part some
     extraction recipe outputs — compute once from `GameDatabase`) into
     `DisabledRecipes`.

### Phase 3 — Settings & persistence
7. `AppSettings`: `bool PlannerExcludeManualParts = true`,
   `bool PlannerAllowOreConversion = false`,
   `List<string> PlannerDisabledRecipes = []` (store the *disabled* set so new
   recipes default enabled). Persisted via the existing `SettingsStore`
   (`AppState.SaveSettings`); cover in `StoreTests`.

### Phase 4 — UI
8. Auto-Plan panel (`MainPage.AutoPlan.cs` / `MainPage.xaml` `AutoPlanPanel`):
   two `Switch` rows for the toggles + a "Recipes…" button swapping the panel
   content to the recipe list view (back button returns). List rows reuse the
   chooser row template + checkbox, grouped by primary output part, search box
   on top, bulk buttons: *All on*, *Alternates off*, *From save*.
9. Settings page: same list embedded in a new section (one shared persisted
   state, two entry points). Recipe rows show output icons per
   [ui-readability-ux.md](ui-readability-ux.md) #2 once that lands.

### Phase 5 — "From save" (spike, then implement)
10. Time-boxed spike on `SatisfactorySaveReader`: locate the schematic manager
    object (`Persistent_Level:PersistentLevel.schematicManager`,
    `mPurchasedSchematics` array of `/Game/FactoryGame/Schematics/Alternate/...`
    object refs) in a real save from `_reference/save_backup`.
11. Mapping schematic asset name → recipe display name: derive from the asset
    path stem (`Schematic_Alternate_X` ↔ recipe class) plus a hand-table for
    irregulars, stored in `tools/game_data.json` per recipe
    (`"UnlockSchematic": "..."`, generator-emitted). If the spike shows the
    mapping is unreliable, fallback scope: parse nothing — let "From save" read
    the already-imported save selected in the Saves panel and report
    unrecognized entries instead of guessing.
12. Wire the *From save* bulk action: enabled set = standard recipes + unlocked
    alternates; everything else disabled.

### Phase 6 — Tests
13. `FactoryPlannerTests`: default plan for a coal-consuming target uses mined
    Coal (no Leaves/Wood/Mycelia in any supply column); disabled recipe absent
    from `PlannedRecipe` list; banned-but-provisioned part supplies up to cap;
    ore-conversion toggle adds/removes Converter columns. `SaveReaderTests`:
    purchased-schematics extraction against a fixture save.

## Open questions

- ~~Does the save parser already expose unlocked alternate recipes?~~ Answered
  during survey: **no** — `SatisfactorySaveReader` parses resource nodes only.
  "From save" is scoped as its own spiked phase in the implementation plan.
- Should disabling a recipe also hide it from the manual recipe chooser? Decision
  for now: **no** — the list governs the planner only.

## Acceptance criteria

1. Default plan for Coal (target: any coal-consuming chain) uses mined Coal, not
   Biomass → Coal chains; no plan consumes Leaves/Wood/Mycelia/remains unless the
   user provisions them or turns the exclusion toggle off.
2. With "Allow ore conversion" OFF (default), no Converter ore-conversion recipe
   appears in any plan; toggling it ON makes them available again.
3. Unchecking a recipe in the planner list removes it from subsequent plans;
   state survives app restart and is the same state shown in Settings.
4. "From save" enables exactly the save's unlocked alternates (verified against a
   test save), leaving standard recipes untouched.
5. Toggles and list state serialize with app settings, not per-document.
