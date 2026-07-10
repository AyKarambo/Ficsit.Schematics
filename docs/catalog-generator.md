# Catalog generator

The game-data tables under `Ficsit.Schematics.Core/GameData/Catalog/` are generated from
Satisfactory's official Docs export by the offline console tool
`Ficsit.Schematics.CatalogGenerator`. The tool runs manually; its output is committed —
there is still **no runtime data file and no build-time codegen**.

## Refreshing after a game update

1. Copy the game's export over the committed one:
   `G:\SteamLibrary\steamapps\common\Satisfactory\CommunityResources\Docs\en-US.json`
   → `Ficsit.Schematics.CatalogGenerator/Docs/en-US.json`.
   The game ships it UTF-16LE; the reader accepts that as-is, but re-encode to UTF-8/LF
   before committing so the diff is reviewable (PowerShell:
   `[IO.File]::WriteAllText($dst, ([IO.File]::ReadAllText($src, [Text.Encoding]::Unicode) -replace "`r`n","`n"), [Text.UTF8Encoding]::new($false))`).
2. Inspect what changed without writing anything:
   `dotnet run --project Ficsit.Schematics.CatalogGenerator -- --verify`
   Every line is either a real game change, a rename (record it in
   `Overrides.LegacyNames` so old documents keep loading), or a derivation gap
   (extend `Overrides` with a commented entry).
3. Rewrite the generated files:
   `dotnet run --project Ficsit.Schematics.CatalogGenerator -- --write`
4. Delete `Ficsit.Schematics.Tests/Fixtures/catalog-snapshot.json` — the snapshot oracle
   regenerates on the next test run; deleting it is the sanctioned intentional-change signal.
5. `dotnet test Ficsit.Schematics.Tests/Ficsit.Schematics.Tests.csproj` must be green:
   `CatalogGeneratorTests` re-checks the catalog against the export (oracle), byte-compares
   every generated file (idempotency), and validates the alias table; the icon test fails
   for any newly named part until `Resources/Raw/icons/<Name>.png` exists.
6. Commit the export, the regenerated files and the new snapshot together, and list the
   data changes in the commit message.

Running `--write` twice against the same export produces zero diff (idempotent); the
`Reemitting_generated_files_is_byte_identical_to_disk` test enforces it.

## Generated vs hand-authored

| Generated (headers say so — do not edit) | Hand-authored (never touched by the tool) |
|---|---|
| `Catalog/PartsCatalog.cs` | `Catalog/Machines/<Category>Machines.cs` — grouping, sort keys, families, marks, purity/belt/upload capacities, node defaults |
| `Catalog/Recipes/<Machine>Recipes.cs` | `Catalog/MachineModule.cs` builders, `MachineGroup`, `MarkSpec` |
| `Catalog/Machines/MachineStats.g.cs` — per-machine tier, power, overclock exponent, somersloop slots, build cost, extraction throughput | `Overrides.cs` **inside the generator** — modeling the export cannot express: manual-gather flags, ore gates, event tiers, Space Elevator phases, machine-tier overrides, legacy names |
| `Serialization/NameAliases.g.cs` — legacy → official names, consumed by `SfmdSerializer` on load | `MachineTable.cs` inside the generator — which buildings the app models |

Machine rows pull their numbers from `MachineStats` by name (`Machine(2, "Constructor")`),
so a game update flows through the generated stats table while the reviewed structure
stays byte-identical.

## Derivation notes

- Milestone tiers are keyed by (`mTechTier`, 1-based position by `mMenuPriority`) — the
  `Schematic_3-4_C`-style class names are historical and no longer match in-game numbers.
  HUB upgrades map to `0-n`; `Schematic_StartingRecipes_C` grants `0-0`.
- Recipes no milestone unlocks (MAM research, hard-drive alternates, event content) get
  `max(machine tier, ingredient part tiers)` by fixpoint; a part becomes available at the
  min tier of the recipes that *primarily* produce it (byproducts and unpackaging don't
  count; alternates only count when nothing standard makes the part).
- Quantities convert exactly: export decimals parse straight into `Rational`
  (`1.321929` → `1321929/1000000`), fluid amounts are cm³ ÷ 1000 — nothing passes
  through floating point.
- Generator fuel recipes come from fuel energy ÷ generator power, supplemental water from
  `mSupplementalToPowerRatio`, byproducts (nuclear waste) from the fuel entry.
