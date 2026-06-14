# Auto-Planner tier cap

> **Status: ✅ Implemented.**

## Problem

The planner draws from every enabled recipe, so it reaches for machines the player
hasn't unlocked yet — e.g. it makes Diluted Fuel on the **Blender** (a late-tier
building) when the user's save has no Blender. There was no quick way to say "only
use what I can actually build."

## Decided behavior (discussion 2026-06-14)

- A single **"Available up to tier"** picker in the Auto-Plan panel: `All tiers`
  (no cap) or `Tier 1`…`Tier 9`. Recipes whose progression tier (phase) exceeds the
  cap are excluded from the plan.
- **Alternates are filtered by their own tier**, like any recipe (chosen over
  "exclude alternates" / "ignore alternates"). A tier cap is therefore a heuristic
  for hard-drive alternates, which are exploration-gated rather than tier-gated; the
  per-recipe enable/disable list ([planner-recipe-control.md](planner-recipe-control.md))
  remains the escape hatch for exceptions.
- Persisted as a **global default** (not per-document), reusing the same settings
  pattern as the other planner toggles.

## Implementation

The planner learns no new concept — the cap maps onto the existing
`PlanRequest.DisabledRecipes`, exactly like the "allow ore conversion" toggle.

- `FactoryPlanner.RecipesAboveTier(GameDatabase, int maxPhase)` — yields recipe names
  with `recipe.Tier.Phase > maxPhase`. Mirrors `OreConversionRecipes`.
- `AppSettings.PlannerMaxTierPhase` (`int`, default **99** = no cap). Persisted via
  `AppState.SaveSettings`; covered in `StoreTests.Planner_settings_roundtrip`.
- `MainPage.xaml` — `PlanMaxTierPicker` next to the byproduct picker.
- `MainPage.AutoPlan.cs` — picker seeded from settings (`TierIndexFromCap` /
  `TierCapFromIndex`, index 0 = "All tiers" = 99, index N = cap N); `OnPlanMaxTierChanged`
  saves the global default (guarded against the seeding pass). In `OnPlanRunClicked`,
  when the cap < 99, `RecipesAboveTier` is folded into `request.DisabledRecipes`.

## Tests

`FactoryPlannerTests.Tier_cap_excludes_higher_tier_recipes`: `RecipesAboveTier`
names exactly the phase > cap recipes; a plan with them disabled uses nothing past
the cap. `StoreTests`: `PlannerMaxTierPhase` round-trips.

## Acceptance criteria

1. With the cap below the Blender's unlock tier, no Blender recipe appears in a plan
   (Diluted Fuel falls back to the Refinery route); raising the cap brings them back.
2. The cap persists across app restarts as a global default.
3. "All tiers" reproduces today's behavior exactly (no recipe excluded).
