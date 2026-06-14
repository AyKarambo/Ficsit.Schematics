# "From save" unlocked-alternates import — research spike

> **Status: 📚 Research spike — investigation complete; informs [planner-recipe-control.md](planner-recipe-control.md). Not a standalone feature to implement.**

Spike for [planner-recipe-control.md](planner-recipe-control.md) Phase 5. Goal:
can we read the set of **unlocked alternate recipes** from a Satisfactory `.sav`
so the planner's "From save" button enables exactly those alternates?

**Verdict: YES — clearly feasible, low risk on the parsing side.** The purchased-
schematics list is present, well-structured, and extractable with a small extension
to `SatisfactorySaveReader`'s existing primitives. The only real work is a
hand-maintained schematic→recipe name table, because the game's internal schematic
names do **not** reliably match our catalog class names (only ~42% match by stem).

Investigated against a real save (save version 60 / Satisfactory 1.1):
`%LocalAppData%\FactoryGame\Saved\SaveGames\76561198007592448\dune_desert_autosave_2.sav`
(6.1 MB compressed → 107 MB decompressed, 816 zlib chunks). No `.sav` fixture
exists in the repo; `SaveReaderTests` reads the newest local save off disk, and
the only files under `_reference/save_backup` are the modeler's own `.sfmd` JSON
metadata, not Satisfactory saves.

---

## 1. Is `mPurchasedSchematics` present and parseable? — YES

It is present, exactly once, and parses with the reader's existing primitives
(`DecompressBody`, `FindAll`, `TryReadFString`). It needs **no new save-format
work** — just a new scan + a small array walk.

### Where it lives (spec's guess was slightly off)

The spec guessed `Persistent_Level:PersistentLevel.schematicManager`
(`BP_SchematicManager_C`). In the real save the unlocked list is **not** on the
schematic manager — `mPurchasedSchematics` is an array property on the
**game state** object. The bytes immediately before the property name:

```
...evel.BP_GameState_C_2147477082 . . . . mPurchasedSchematics . . . ArrayProperty ...
```

`BP_SchematicManager_C` does appear in the save (5 hits) but the purchased list is
not adjacent to it. So: **scan for the property name `mPurchasedSchematics`
directly** rather than navigating from an owner object. There is exactly one
occurrence, which removes ambiguity.

### Exact on-disk layout (quoted from real bytes)

After the property name `mPurchasedSchematics\0` the block is a standard UE
ArrayProperty of ObjectProperty:

```
6D...mPurchasedSchematics\0
0D 00 00 00  "ArrayProperty\0"        ← FString: element-container type
<int64 size>                          ← payload byte size
01 00 00 00                           ← index/0
0F 00 00 00  "ObjectProperty\0"       ← FString: array element type (len 0x0F = 15)
00 00 00 00 00                        ← flags/guid byte padding
3B 01 00 00                           ← int32 element COUNT  (0x13B = 315 entries)
53 00 00 00  "/Game/FactoryGame/Sc…"  ← entry 0: FString object ref (len 0x53 = 83)
… repeated COUNT times …
```

So each element is a plain FString (UE serializes object references inside save
ArrayProperty as path strings here). The captured hex confirming this:

```
01 00 00 00 0F 00 00 00 4F 62 6A 65 63 74 50 72 6F 70 65 72 74 79 00   ("ObjectProperty")
00 00 00 00 00 FB 89 00 00 00 3B 01 00 00 00 00 00 00                  (… count 3B 01 = 315)
53 00 00 00 2F 47 61 6D 65 2F 46 61 63 74 6F 72 79 47 61 6D 65 2F ...  ("S…/Game/FactoryGame/")
```

### Entry format (object reference path strings)

Each entry is a class path; the alternates we care about look like:

```
/Game/FactoryGame/Schematics/Alternate/New_Update3/Schematic_Alternate_BoltedFrame.Schematic_Alternate_BoltedFrame_C
/Game/FactoryGame/Schematics/Alternate/Schematic_Alternate_PureIronIngot.Schematic_Alternate_PureIronIngot_C
```

Notable: alternates live under **varying subfolders** — e.g.
`Schematics/Alternate/New_Update3/…`, `Schematics/Alternate/…` — so the folder
path is NOT stable. The **stem** `Schematic_Alternate_<X>` after the last `/` (and
before `.`) IS the stable key. The array also contains non-alternate schematics
(`Schematic_StartingRecipes`, `Schematic_1-1`, tutorial, milestone, MAM research,
customizer/build-gun unlocks). In the test save: **315 total purchased
schematics, 92 of them `Schematic_Alternate_*`.**

---

## 2. Concrete extraction approach

Add one method to `SatisfactorySaveReader`, reusing the existing helpers verbatim.
No change to chunk handling, no new format decoding.

```
public static IReadOnlyList<string> ReadUnlockedAlternateSchematics(byte[] saveFile)
{
    var body = DecompressBody(saveFile);                 // existing
    // 1. find the single "mPurchasedSchematics" name (FindAll + length-guard
    //    like ScanResourceOverrides does: int32 before == len+1, trailing \0).
    // 2. skip name\0, read FString "ArrayProperty", int64 size, int32, FString
    //    "ObjectProperty", padding, int32 count.
    // 3. read `count` FStrings via TryReadFString; for each, take the substring
    //    after the last '/' up to '.', strip a trailing "_C", keep those that
    //    start with "Schematic_Alternate_", strip that prefix → the stem.
}
```

Reused primitives: `DecompressBody`, `FindAll`, `TryReadFString` already exist and
are sufficient. The only mild fragility is the fixed padding between
`"ObjectProperty\0"` and the count (UE version-dependent flag/guid bytes). Two
robustness options:

- **Robust (recommended):** skip the structural header entirely and regex/scan the
  array region for `/Game/FactoryGame/Schematics/…_C` paths between the property
  name and the next property name. This sidesteps the version-sensitive padding
  and matches the reader's existing "scan for what we need" philosophy. It is how
  this spike extracted the 92 stems reliably.
- **Strict:** parse the header exactly as above and read precisely `count`
  FStrings. Cleaner but more brittle across save versions.

Either is small. No fallback to "report unrecognized entries instead of parsing"
is needed — the format is not opaque.

---

## 3. Schematic stem → recipe display-name mapping — NEEDS A HAND TABLE

This is the real cost. Our catalog class name is `Sanitize(displayName)` (PascalCase
of the recipe's display name; see `tools/generate-catalog.ps1` `Sanitize`). The
save's stem is **Coffee Stain's internal schematic name**, which is independent of
our display name. They coincide often but not reliably.

Measured against the catalog (110 alternate recipes) vs the save's 92 alternate
stems: **only 39 stems match a catalog class name exactly (~42%). 53 do not.**

The catalog stores **no** schematic/class identifier per recipe — only
`Name`, `Machine`, `BatchTime`, `Alternate`, `Tier`, `Parts` in
`tools/game_data.json`. So there is nothing to join on today; the mapping must be
added.

### Recommended strategy

Add `"UnlockSchematic"` (or `"GameClass"`) per alternate recipe in
`tools/game_data.json`, generator-emitted onto `RecipeBase`, holding the game's
schematic stem (e.g. `"IronIngot_Leached"` for "Leached Iron ingot"). "From save"
then maps stem → recipe by exact lookup. Storing it as catalog data (vs a separate
table) keeps it versioned with the recipes and lets a data update fix it.

Seed the table with the 39 exact matches automatically; hand-fill the rest. Many
unmatched stems are mechanical convention swaps that can speed up the hand pass:

| save stem (game)          | catalog class (our display name)        |
|---------------------------|-----------------------------------------|
| `IronIngot_Leached`       | `LeachedIronIngot` ("Leached Iron ingot")|
| `IronIngot_Basic`         | `BasicIronIngot`                         |
| `CopperIngot_Leached`     | `LeachedCopperIngot`                     |
| `CopperIngot_Tempered`    | `TemperedCopperIngot`                    |
| `CateriumIngot_Leached`   | `LeachedCateriumIngot`                   |
| `CateriumIngot_Tempered`  | `TemperedCateriumIngot`                  |
| `Quartz_Fused`            | `FusedQuartzCrystal`                     |
| `Quartz_Purified`         | `QuartzPurification`                     |
| `Silica_Distilled`        | `DistilledSilica`                        |
| `SteelBeam_Aluminum`      | `AluminumBeam`                           |
| `SteelBeam_Molded`        | `MoldedBeam`                             |
| `SteelPipe_Iron`          | `IronPipe`                               |
| `SteelPipe_Molded`        | `MoldedSteelPipe`                        |
| `SteelCastedPlate`        | `SteelCastPlate`                         |
| `AILimiter_Plastic`       | `PlasticAILimiter`                       |
| `ElectroAluminumScrap`    | `ElectrodeAluminumScrap`                 |
| `Cable1` / `Cable2`       | `InsulatedCable` / `QuickwireCable`      |
| `Wire1` / `Wire2`         | `IronWire` / `CateriumWire` (verify order)|
| `Coal1` / `Coal2`         | `Charcoal` / `Biocoal` (verify order)    |
| `Screw` / `Screw2`        | `CastScrew` / `SteelScrew` (verify order)|
| `Computer1` / `Computer2` | `CrystalComputer` / `CateriumComputer`   |
| `CircuitBoard1`/`2`       | `SiliconCircuitBoard`/`CateriumCircuitBoard` |
| `Motor1`                  | `RigorMotor`                             |
| `HighSpeedConnector`      | `SiliconHighSpeedConnector`              |
| `HighSpeedWiring`         | `AutomatedSpeedWiring`                   |
| `EnrichedCoal`            | `CompactedCoal`                          |
| `Gunpowder1`              | `FineBlackPowder`                        |
| `HeatSink1`               | `HeatExchanger`                          |
| `Plastic1`                | `RecycledPlastic`                        |
| `IngotSteel1` / `IngotSteel2` | `SolidSteelIngot` / `CompactedSteelIngot` |
| `Quickwire`               | `FusedQuickwire`                         |
| `Stator`                  | `QuickwireStator`                        |
| `Rotor`                   | `SteelRotor`                             |
| `TurboFuel`               | `TurboHeavyFuel` (verify — Turbofuel family) |
| `RadioControlUnit1`       | `RadioConnectionUnit`                    |
| `IngotIron`               | (standard? appears as alternate stem — verify) |

> The numbered/`_Variant` stems (`Cable1`, `Coal1`, `IngotSteel2`, …) pair to two
> different display names; the digit's mapping must be **confirmed against the
> game**, not guessed. Do not auto-derive these — get them right once by hand.

### Caveats that make pure stem-matching unsafe

- **`ReinforcedIronPlate1/2`, `ModularFrame`, `HeavyModularFrame`, `Concrete`,
  `Silica`, etc.** appear as alternate stems whose names collide with *standard*
  recipe concepts — a careless contains-match would mis-map. Use exact stem keys.
- **One schematic can unlock multiple recipes** in principle; the table should be
  stem → recipe(s). Most alternates are 1:1, but design the lookup as 1→N to be safe.
- **The 71 "catalog-only" alternates** (present in our catalog, absent from this
  save) are the union of *locked-in-this-save* and *naming-mismatch partners*; you
  cannot tell which from one save. Build the table from a **fully-unlocked** save
  (or the game's recipe registry / community data dumps), not this partial one.

---

## 4. Effort & risk

**Save parsing: S.** ~30–40 lines added to `SatisfactorySaveReader` reusing
existing helpers; one new test asserting the alternate set is non-empty and all
stems start with the expected prefix. Format is confirmed and stable-enough.

**Name mapping: M.** Building and verifying the ~110-row stem↔recipe table is the
bulk of the work — it is rote but must be correct, and the numbered-variant rows
(`Coal1/2`, `Cable1/2`, `Wire1/2`, `Screw/Screw2`, `Computer1/2`, …) each need a
game cross-check. Best sourced from a fully-unlocked save plus a community data
dump rather than reasoning from display names.

**Combined: M.** Risk is concentrated entirely in mapping correctness, not in the
binary parse:

- *Low risk:* locating and reading `mPurchasedSchematics` (done in this spike).
- *Medium risk:* the structural-header padding between `ObjectProperty` and the
  count is version-sensitive — mitigated by the regex-scan extraction variant.
- *Medium risk:* mapping errors silently enable/disable the wrong alternate. Make
  "From save" **report unrecognized stems** (stems with no table entry) in the UI
  rather than dropping them silently, so a future game update that adds/renames an
  alternate surfaces as a visible "N unrecognized" notice instead of wrong output.
  This is the spec's fallback used as a safety net, not as the primary path.

### Bottom line

Proceed with the real implementation. Extract via a new
`ReadUnlockedAlternateSchematics` (prefer the path-scan variant), add
`UnlockSchematic` to `game_data.json` per alternate recipe (seed 39 exact matches,
hand-fill 71 from a fully-unlocked save / data dump), and have "From save" enable
standard recipes + matched alternates while reporting any unrecognized stems.
