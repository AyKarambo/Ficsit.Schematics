# Ficsit.Schematics — Project Specification (Improved Prompt)

> This document is the refined version of the original request, grounded in a reverse-engineering
> pass of the reference application (extracted from `D:\Downloads\satisfactory-modeler.zip`):
> its game data, save format, settings schema, translation files, official documentation, and
> live UI screenshots (see `_reference/screenshots/`).

## 1. Goal

Build **Ficsit.Schematics**, a Windows desktop app using **.NET MAUI**: a visual factory
calculator for the game Satisfactory in the spirit of **Satisfactory Modeler** (a Java/Swing
app) — a node-graph canvas where the user places recipe "machines", connects outputs to
inputs, optionally sets limits, and the app calculates exact machine counts, item flows
(parts per minute), and power, using **exact rational arithmetic** (display `0.89`, tooltip
the exact `8/9 Manufacturer`).

It is a **visual calculator**, not a layout planner — and a **similar app, not a clone**.

### 1.1 Design philosophy (user directive, 2026-06-11)

The reference app defines **what the app does**, not how it looks or how it is built.
Do **not** chase 1:1 parity; improving on the reference is expected.

- **Stay faithful to**: game data and calculation semantics (rates, clock speed, somersloops,
  power — results must match the game numerically), and `.sfmd` import/export interop so saves
  can still be exchanged with Satisfactory Modeler.
- **Free — and encouraged — to improve**: visual design (modern Fluent-style Windows UI instead
  of the reference's Swing look), interaction design (safer, more discoverable patterns),
  information architecture (menus, settings, panels), and every technical/architectural choice.
- When the reference does something awkwardly, build the better version instead of copying the
  quirk. Reference screenshots are a feature inventory and inspiration, never a pixel target.
  Wherever this spec describes reference behavior, read it as "this capability must exist",
  not "it must look/work exactly like this".

## 2. Reference ground truth (in `_reference/`)

The reference materials are authoritative for **data, math, and file formats**; the
screenshots only document features.

| Artifact | What it defines |
|---|---|
| `satisfactory-modeler/game_data/game_data.json` | 32 Machines, 7 MultiMachines, 170 Parts, 332 Recipes. Numbers are fraction strings (`"1321929/1000000"`). Recipe `Parts` use negative amounts for inputs, positive for outputs, per `BatchTime` seconds. |
| `satisfactory-modeler/images/icons/*.png` (204) | Item + machine artwork used on nodes and lists. |
| `satisfactory-modeler/images/ui_icons/*.png` (42) | Toolbar/menu glyphs (undo, settings, somersloop, clockspeed, …). |
| `satisfactory-modeler/images/custom_icons/` | `Outpost.png`, `Blueprint.png`, `anypart.png`. |
| `satisfactory-modeler/languages/translations/*.json` (40+) | Full i18n string tables (key → text), including help texts that document features. |
| `save_backup/save.sfmd` | Save format: plain JSON document — top-level settings (`Version, Language, Solver, Zoom, PanX/Y, grids, Path, SpaceElevatorMultiplier, InputMultiplier, PowerMultiplier`) + `Data[]` of nodes; node = `{Name, X, Y, Max?, Title?, ClockSpeed?, …, Inputs: {part: [sourceNodeIndex,…]}}`; outposts carry their own `Zoom/PanX/PanY`. |
| `save_backup/settings.json` | App settings schema: window geometry, `uiScale`, `darkMode`, per-location number formats (Decimal/Fraction, places, rounding Nearest/Up/Down), grid snap, path style `Curves`, `dragSensitivity`, autosave (interval, max backups), language, machine defaults per MultiMachine. |
| `screenshots/03..11_*.png` | Live UI: canvas, recipe chooser, hamburger menu, settings, machine popup, summary panel, fraction tooltip. |

## 3. Functional requirements (v1 = the core loop, feature-complete — not pixel parity)

### 3.1 Canvas (the heart of the app)
- Dark (default) / light theme; near-black canvas; smooth pan (drag background) and zoom (mouse wheel, around cursor).
- **Machine nodes**: card with machine artwork in the center, calculated value below it
  (machine count, or parts-per-minute for miners/extractors/sinks), an editable **limit** field
  at the bottom, input part icons stacked on the left edge, output part icons on the right edge.
  - Input ports are visually flagged when the part is not (fully) supplied ("unmade");
    output ports flagged when surplus leaves the node ("unused"). Labels show ppm at each
    connection point. (Reference uses pink/green backgrounds; styling is ours to design,
    it just has to be instantly readable.)
  - Calculated value rendering distinguishes three states: ppm display mode, invalid, and
    non-matching (configurable flags). Exact styling free.
- **Connections**: cubic-bezier curves from output port to input port with a part icon + ppm
  label at the midpoint/endpoints. Connect by dragging from a port to a compatible port (either
  direction). Re-drag an endpoint to disconnect. Connection styles: Curves (default) / Direct /
  2D (orthogonal); optional waypoints (drag label to anchor; double-left-click waypoint adds
  another; double-right-click deletes; double-right-click on label deletes the connection).
  - **Drag-to-add (our addition)**: dropping a port drag on empty canvas opens the recipe
    chooser pre-filtered to compatible recipes — consumers of the part when dragging from an
    output, producers when dragging from an input — and the chosen machine is placed at the
    drop point and auto-connected.
- **Auto-Plan (our addition)**: the user picks any target parts via a searchable part picker
  with rates (or provides input caps and maximizes a bundle — Space Elevator phases ship as
  presets). Provided inputs cover intermediates too ("I already make Heavy Modular Frames")
  with a per-row lock: locked = the supply is all there is — its producers are excluded and
  an undersupply scales the whole output down proportionally (two-stage LP: maximize the
  achievable fraction, then optimize the bias at that fraction; bottlenecks are reported);
  unlocked = the planner builds extra production for any shortfall. Users pick a bias
  (resource- / power- / machine-efficient), bans raw resources, and chooses byproduct
  handling (eliminate = recycle to zero waste, or allow AWESOME-Sink disposal). The planner
  is an exact-rational two-phase simplex (`Core/Planning/`): one variable per candidate
  recipe / external supply / sink, a balance row per part; raw supplies are priced by
  scarcity (classic-map totals, or rebuilt from imported map nodes). Recipe cycles work,
  which is what makes zero-waste oil loops plannable — the canonical 300-oil → 900-plastic
  build is a regression test. The result materializes on the canvas: nodes per recipe with
  exact machine-count limits, wired part-wise, laid out in dependency layers.
- **Map mode (our addition)**: toggleable world-map background (1 canvas unit = 1 m; map
  bounds X −324 699…425 302 cm, Y ±375 000 cm). Resource nodes (ore nodes, geysers, resource
  wells incl. randomized-node game modes) are imported from a Satisfactory `.sav`
  (`SatisfactorySaveReader`: zlib chunk decompression + surgical actor-header/property scans,
  validated against save version 60). Dropped miners/extractors/pressurizers/geothermals snap
  to the nearest free compatible node, adopt its purity as capacity, and mark it occupied
  (accent marker); hovering a marker shows part · purity · occupant extraction rate, so spare
  vs. saturated nodes are visible at a glance. Node assignments persist via an optional
  `ResourceNode` key on .sfmd nodes (ignored by the reference app).
- **Add machine**: double-click (or right-click) empty canvas → **Recipe Chooser** popup at the
  cursor: search field with three match toggles (**Recipe Name / Inputs / Outputs**), left
  column of specialty machines, right column of recipes (icon + localized name), scrollable,
  ordered by game tier. Click background to dismiss.
- **Machine popup** (right-click a machine): Title text field, **Clock Speed %** (with − / +
  stepper that snaps count to next whole machine, capped 250%), **Auto Round** toggle,
  **Somersloop n/max** stepper, **Parts Per Minute** display toggle, and Cut / Copy / Paste /
  Delete buttons.
- **Delete machine**: Delete key on selection and a context-menu action — deliberately *not*
  the reference's double-click-to-delete, which is accident-prone. Multi-select: rubber band
  drag + click to select one. Selected machines move together. Copy/Cut/Paste/Select-All with
  the usual Ctrl shortcuts; paste at cursor.
- **Undo/redo**: unlimited, Ctrl+Z / Ctrl+Y + menu buttons; covers moves, edits, adds, deletes,
  paste, even imports.
- Optional **snap to grid** for machines and waypoints (separate X/Y sizes, apply-to-existing).
- Hover **tooltips** show exact values as fractions (e.g. `8/9 Manufacturer`) — formats are
  configurable per location (Number Settings).

### 3.2 Calculation
- All math in **exact rationals** (BigInteger numerator/denominator); inputs accept whole
  numbers, decimals, fractions and mixed numbers (`1.2`, `1 1/5`, `6/5`, `4.32/3.6`).
- Recipe rate = `Amount / BatchTime * 60` per machine at 100%; scaled by clock speed; somersloops
  multiply output (×(1 + slots·multiplier)) and power (^exponent); machine power uses
  `AveragePower · clock^OverclockPowerExponent`, shard-boosted power uses
  `ProductionShardPowerExponent`.
- **MultiMachines**: Miner Mk.1/2/3 with Impure/Normal/Pure purity (×½/×1/×2), Oil Extractor,
  Resource Well Extractor, Dimensional Depot upload rates, AWESOME Sink belt marks (60…1200),
  Geothermal purity power, Space Elevator. Defaults configurable in settings (Machine Defaults).
- **Specialty nodes**: Outpost (nested sub-canvas with its own pan/zoom, plus-button I/O),
  Blueprint (like outpost but result = number of copies; interior needs a limit), Splurger,
  Priority Splitter / Merger / Splurger (ordered ports), Storage Container modes (Partially
  Full / Full / Empty / Input = Output), AWESOME Sink (sink-points/min), Dimensional Depot.
- **Solvers** (selectable in settings, persisted per save): **None** (no calc), **Manual**
  (entered values are desired outcomes, spreadsheet-like), **Basic** (entered values are limits;
  propagation; ignores splitter/merger preferences) — and **Full** (LP over the whole graph,
  honors priorities) as the post-v1 stretch goal. v1 ships None/Manual/Basic with Basic default.
- Global multipliers: Space Elevator deliverable cost ×, recipe parts cost ×, power consumption ×.
- **Summary panel** (toggle bottom-left): scope dropdown (Everything / Current Outpost (& Below) /
  Selected (& Below / + Connected)), collapsible sections — Power (avg net/min-max, used, made,
  boost), Overclock (power shards, somersloops), Output/Input part tables (Unused/Used/Made/
  Mined-Extracted/Sunk filters, count per part with icon), Sink Points, Cost To Build; sections
  addable via ⊕, panel content copyable.

### 3.3 Persistence (document store — NOT SQL)
- **LiteDB** (embedded NoSQL document database, single data file, zero external services) in the
  app-data folder. Factories are stored as documents in the natural save-document shape; no
  relational mapping. Collections: `factories` (the working document + named saves), `backups`
  (rolling autosaves, max N), `settings` (one document).
- **Autosave**: periodic (default 5 min) + on app exit; backups browsable/restorable.
- **Import/Export**: read & write **`.sfmd` files byte-compatible with Satisfactory Modeler** so
  users can exchange saves with the original app. (Steam Cloud sync is explicitly out of scope.)

### 3.4 App chrome
- Chrome layout is free-form — use whatever fits a modern Windows app (toolbar, command bar,
  flyout menus) rather than the reference's hamburger button stack. It must expose: Undo/Redo,
  Import (File/Backup), Export, Settings (General / Style / Saves / Machine Defaults /
  Numbers), Help, About.
- Settings General: Language, Dark/Light mode, UI Size, Drag Sensitivity, Calculator selector,
  the three global multipliers. Style: connection path style, grids. Numbers: per-location
  format table (location / Fraction-Decimal / digits / rounding). Saves: autosave config + saves
  list. Machine Defaults: per-MultiMachine defaults (ppm display, initial limit, auto-round,
  default mark/purity/belt, fullness), each with "apply to existing" actions.
- Localization: load the reference translation JSONs (40+ languages, en-US default; ship de, etc.).
- Item/machine artwork from the reference `images/` set (it is the game's art). UI glyphs may
  come from `ui_icons` or any consistent modern icon set — whichever looks better.

## 4. Architecture & patterns

- **Projects**:
  - `Ficsit.Schematics.Core` — pure domain, no UI deps: `Rational`, game-data models + loader,
    factory graph, solver(s), undo/redo command stack, `.sfmd` (de)serialization. Deterministic
    and fully unit-testable.
  - `Ficsit.Schematics.Data` — LiteDB repositories (factory/saves/backups/settings) behind
    interfaces defined in Core (repository pattern, dependency inversion).
  - `Ficsit.Schematics` — MAUI app (WinUI), **MVVM** with `CommunityToolkit.Mvvm`
    (source-generated observable properties/commands), DI via `MauiProgram`. The canvas is a
    `GraphicsView` `IDrawable` (immediate-mode rendering of nodes/wires from the view model)
    with an interaction state machine (idle → panning / dragging / connecting / rubber-band).
  - `Ficsit.Schematics.Tests` — xunit suite for Core (+ Data via temp DB files).
- **Patterns**: MVVM, repository, command (undo/redo), strategy (solver selection), facade
  (`FactoryEditor` orchestrating graph + commands + solver invalidation), pub/sub via
  `IMessenger` for cross-panel updates.
- **NuGet discipline** (per request, only best-practice essentials): `CommunityToolkit.Mvvm`,
  `LiteDB`, `xunit` (+ runner). Everything else (solver, rational math, canvas, .sfmd codec)
  is hand-written.
- Windows-only TFM for v1 (`net10.0-windows10.0.19041.0`); Core/Data stay platform-neutral
  (`net10.0`) so other targets remain possible.

## 5. Explicit non-goals for v1

Steam Cloud sync; the Full LP solver (stretch goal; UI shows it disabled); blueprint duplication
math beyond count display; popout windows; in-app help/documentation content (link out instead);
non-Windows targets.

## 6. Acceptance checks

1. `dotnet build` and `dotnet test` green; data integrity test proves all 332 recipes / 170
   parts / 32 machines load, every referenced part & machine resolves, every node artwork file
   exists.
2. Importing `_reference/save_backup/save.sfmd` reproduces the user's factory: Limestone miner
   (60) → Fine Concrete → Encased Industrial Pipe → Heavy Flexible Frame with manufacturer count
   **8/9 ≈ 0.89**, inputs 16.67 / 66.67 / 346.67 ppm, output 3.33/min, and flags the same
   undersupplied inputs / surplus outputs the reference flags (our own styling).
3. The core loop works end-to-end in the running app: add Iron Ore miner + Iron Ingot smelter +
   Iron Plate constructor, connect, set a limit, see counts/flows/power; undo/redo; restart the
   app and the factory is restored from LiteDB; export to `.sfmd` and the original Modeler can
   open it.
