# Auto-Plan panel — centered layout & categorized options

> **Status: ✅ Implemented.** Centered/enlarged panel, option cards, category-grouped part picker, machine-grouped recipe list. Deferred follow-ups: two-column responsive layout and grouped banned-resource chips.

## Problem

The Auto-Plan panel is a single narrow right-docked column (`AutoPlanPanel`:
`HorizontalOptions="End"`, `WidthRequest="380"`, `ScrollView MaximumHeightRequest="640"`)
holding ~12 stacked controls and sections. It reads as one long flat scroll with poor
overview, the options aren't visually grouped, and the lists inside it are flat:

- the **part picker** is one alphabetical `CollectionView` of every part;
- the **excluded-resources** chips are an ungrouped `FlexLayout`;
- the shared **recipe enable/disable list** is a flat list.

## Proposed behavior (research 2026-06-14)

### Centered & bigger, for overview
- Open the Auto-Plan view **centered** and **wider** (target ~720–840 px) instead of the
  narrow right dock, so the whole request is visible at once. Dismissible the same way as
  the other overlays (close button / click-away), consistent with `CloseOverlays`.
- On a wide window, lay the sections out in **two columns** (e.g. *build request* on the
  left, *optimization & output* on the right); fall back to one column when narrow.

### Categorized options (grouped cards)
Group the controls under titled cards (reuse `SectionHeader`, add a light `Card` border
style mirroring `Panel`):
- **What to build** — targets list, phase preset, "maximize from inputs".
- **Inputs & limits** — provided inputs, excluded resources, "available up to tier".
- **Optimization** — bias, resource preference, byproducts.
- **Output** — auto-apply, auto-collapse, recipe enable/disable, *Plan factory*.

### Categorized lists (within the lists themselves)
- **Part picker** → a **grouped** `CollectionView` (`IsGrouped="true"`). `PartDefinition`
  has no category field, so derive the group from data already present:
  *Fluids* (`Fluid`), *Raw resources* (in `ScarcityWeights` / not producible),
  *Intermediates*, and optionally by **tier/phase** (`Tier.Phase`) within. Group headers
  reuse `SectionHeader`.
- **Excluded-resources chips** → sub-grouped by raw category (ores / fluids / special)
  with a small label per group instead of one flat wrap.
- **Recipe enable/disable list** → grouped **by machine** (`RecipeDefinition.Machine`,
  via `Data.RecipesByName`), since `RecipeListItem` carries no machine itself — either add
  the machine to the view model or group at build time in `MainPage.RecipeList.cs`.

## Implementation

- `MainPage.xaml`: re-anchor `AutoPlanPanel` to `Center`, widen it, and reorganize its
  `VerticalStackLayout` into grouped cards inside a responsive 1-/2-column `Grid`. The
  responsive switch can be width-driven from the existing size handling in
  `MainPage.Input.cs` (or a `VisualStateManager`).
- **Re-anchor the part picker.** `PartPickerPanel` is currently positioned relative to the
  380 px right dock (`Margin="12,60,404,12"`); recompute its position relative to the
  centered panel so it still opens beside it.
- `MainPage.AutoPlan.cs`: build the **grouped** part-picker source (a helper that buckets
  `_allPartItems` by derived category) and the grouped banned-chips layout.
- `MainPage.RecipeList.cs`: group the recipe list by machine.
- A reusable `PartCategory(part)` classifier in Core (UI-free) so the grouping is testable
  and shared.

This is presentation/layout only — no planner or model behavior changes; the same
`PlanRequest` is built as today.

## Tests

Core: `PartCategory` buckets a known set correctly (Water→Fluid, Iron Ore→Raw,
Iron Plate→Intermediate). Layout, centering, responsiveness, and grouped lists verified by
`/run` + screenshots at a wide and a narrow window size.

## Acceptance criteria

1. Auto-Plan opens centered and noticeably larger, with options organized into titled
   groups; on a wide window it uses two columns and collapses to one when narrow.
2. The part picker is grouped by category (with headers), the excluded-resource chips are
   grouped, and the recipe list is grouped by machine.
3. The part picker re-anchors correctly beside the centered panel; planning still builds
   the same request and produces the same plans.
