# Ficsit.Schematics

A desktop factory planner for *Satisfactory*: lay machines on an infinite canvas,
wire them up, and see live throughput / power; or let the auto-planner synthesize a
factory for a target output. .NET 10 MAUI app (Windows), with all the domain logic
in a UI-free core library.

## Projects

| Project | TFM | Role |
|---|---|---|
| `Ficsit.Schematics` | `net10.0-windows10.0.19041.0` | MAUI app — canvas, pages, view models, services |
| `Ficsit.Schematics.Core` | `net10.0` | Domain: game data, model, solver, planner, save reader, serialization. **No UI.** |
| `Ficsit.Schematics.Data` | `net10.0` | Persistence (`FicsitStore`: settings, current doc, backups, map nodes) |
| `Ficsit.Schematics.Tests` | `net10.0` | xUnit tests over Core + Data |

## Build / test / run

```pwsh
# Test (fast; Core + Data, no MAUI workload needed)
dotnet test Ficsit.Schematics.Tests/Ficsit.Schematics.Tests.csproj

# Build the app
dotnet build Ficsit.Schematics.csproj -f net10.0-windows10.0.19041.0

# Run the app
dotnet build Ficsit.Schematics.csproj -t:Run -f net10.0-windows10.0.19041.0
```

## Release

Push a `v*` tag → `.github/workflows/release.yml` tests, builds the WiX MSI
(`installer/build-installer.ps1`) and uploads it **unsigned** to a **draft**
GitHub Release; the maintainer then signs + publishes locally with
`installer/sign-release.ps1` (Certum SimplySign only works on the maintainer's
machine — never try to sign in CI). Full runbook: `installer/README.md`.

## Architecture

```
App (MAUI)                         Core (no UI)
  App.xaml.cs ─ MainPage           GameData/ ─ Catalog (grouped C# tables) → GameDatabase
    MainPage.*.cs (partials:         Model/ ─ FactoryDocument, FactoryGraph, FactoryNode
      Chooser, MachineEditor,        Solver/ ─ ISolver, BasicSolver (live throughput/power)
      SavesPanel, SettingsPanel,     Planning/ ─ FactoryPlanner + exact-rational simplex
      RecipeList, AutoPlan, Input)   Editing/ ─ FactoryEditor + undo/redo CommandStack
    Canvas/ (partials per concern)   Numerics/ ─ Rational (exact fractions, no FP drift)
      CanvasController ─ pointers    Saves/ ─ Satisfactory save reader, map snapping
      FactoryCanvasDrawable ─ draw   Serialization/ ─ SFMD document format
    ViewModels/, Services/         Data: FicsitStore (LiteDB document store)
```

- **`Services/AppState`** is the app-wide hub: the open `FactoryEditor`, settings,
  selection, autosave. Everything is wired through **DI** (`MauiProgram.cs`) —
  singletons resolved by constructor injection, including the canvas collaborators.
- **`Canvas/`** is one renderer (`FactoryCanvasDrawable`, immediate-mode `IDrawable`)
  plus one pointer state machine (`CanvasController`). Both are split into partial
  files by concern (`.Connections`, `.Nodes`, `.MapMode`, …) — same idiom as
  `MainPage.*.cs`.
- The **solver** computes flows for the current graph; the **planner** is a separate
  LP-based factory synthesizer. They don't depend on each other.

## Game data (important)

There is **no runtime data file and no code generation.** Machines, parts and recipes
are authored as grouped, strongly-typed C# tables under
`Core/GameData/Catalog/`, discovered via reflection by `GameDataCatalog`:

- `Recipes/<Machine>Recipes.cs` — one `RecipeModule` per machine; rows use `In(...)` /
  `Out(...)` helpers. Adding a recipe = adding one line.
- `PartsCatalog.cs` — every part as a one-line `Part(...)` row.
- `Machines/<Category>Machines.cs` — machines grouped by category; each `MachineGroup`
  keeps a multi-machine **family** (marks / purity / belt capacities) next to its machine.
- `RecipeModule` / `PartModule` / `MachineModule` define the row types and helpers.

A **multi-machine family** (`MultiMachineDefinition`) is one building with selectable
options: marks (Miner Mk.1/2/3), resource purity, belt marks or upload rates, plus node
defaults. Recipes target the family name; `GameDatabase.MultiMachineFor(name)` resolves it.

Each row carries a sort key that restores canonical game order.
`GameDataCatalog.BuildDatabase()` assembles the immutable `GameDatabase` used everywhere.

**Editing game data:** change the relevant table file directly. `GameDataTests`
includes a snapshot oracle (`Fixtures/catalog-snapshot.json`) that pins the assembled
data; if you intentionally change game data, delete the fixture to regenerate it on the
next test run, otherwise the snapshot test fails by design.

## Conventions

- File-scoped namespaces; `Rational` (not `double`) for all game quantities.
- `*.cs` are `eol=lf` (`.gitattributes`); keep it that way to avoid phantom diffs.
- Specs and design notes live in `docs/` and `docs/specs/`.
