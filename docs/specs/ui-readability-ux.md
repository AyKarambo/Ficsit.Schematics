# UI readability & UX (#2, #3, #9, #10)

## #2 — Output icons in the recipe chooser

**Problem:** recipe rows are hard to tell apart, especially alternates.

**Current:** `RecipeChooserViewModel.Refresh()` already picks the *first* output's
icon as the row icon. That's one small icon, and recipes with byproducts or
non-obvious names still read ambiguously.

**Decided (interview):** icons go in the **recipe chooser list** (not canvas
nodes, popup, or summary).

**Spec:**

- Each recipe row shows the icon of **every output part** (most recipes 1, some 2
  with byproduct), ordered as in the recipe, primary output first and slightly
  larger or leading. Quantities are not required on the row; the existing
  tooltip/detail affordance can carry rates.
- Alternates producing the same part inevitably share output icons — keep the
  existing name text as the differentiator, and (optional, behind the same row
  layout) trailing smaller **input** icons, which is what actually distinguishes
  e.g. Pure vs. Alloy ingot recipes at a glance. Implementer may include input
  icons if the row stays clean at default UI size.
- `RecipeListItem` grows from one `Icon` to an icon list; `IconStore` already
  resolves part images.
- Applies everywhere the chooser appears (add-node flow, port-drag filtered
  chooser) and to the planner's recipe toggle list
  ([planner-recipe-control.md](planner-recipe-control.md)).

**Acceptance:** scrolling the chooser, every recipe row shows its output icon(s);
"Iron Plate" vs "Coated Iron Plate" vs "Steel Cast Plate" are distinguishable
without reading full names; search/filter behavior unchanged.

## #3 — Port PPM labels: overlap, size, contrast

**Problem (interview):** "the parts-per-minute values that go in and out of every
machine" overlap each other and are too small / low-contrast.

**Current:** `FactoryCanvasDrawable` (≈ lines 396–408) draws port ppm labels at
**9.5 px font** into a **fixed 60-unit box** beside each port, with no background.
Long values overflow the box toward neighboring nodes; labels of adjacent nodes
collide; thin text sits directly on the canvas/grid with poor contrast. Connection
mid-labels (10 px, fixed 60×16 box) share the problem.

**Spec:**

- **Contrast:** draw port and connection labels on a rounded background pill using
  theme colors (`CanvasTheme`) with proper contrast in both light and dark mode.
- **Fit:** measure the string and size the pill to the text instead of fixed
  60-unit boxes; never clip a value.
- **Size:** raise the minimum *on-screen* legibility — labels scale with zoom but
  clamp to a minimum effective pixel size; below the zoom level where even the
  clamped size would overlap its neighbors, hide port labels entirely and show
  them on hover/selection instead.
- **Overlap:** within one node, stack labels with their ports (existing layout);
  between neighbors, the pill + measured width + zoom threshold rules above must
  eliminate the common collisions. (Full collision-avoidance layout is out of
  scope; the threshold rule is the backstop.)
- Number formatting itself stays with `NumberFormatService` / Numbers settings —
  this spec changes presentation, not values.

**Acceptance:** at default zoom, two nodes placed side by side with 4-digit ppm
values show no overlapping or clipped labels in light and dark themes; zooming
far out hides port labels rather than rendering unreadable smears; hovering a
port at any zoom reveals its value.

## #9 — Settings panels & machine editor placement

Two concrete pains from the interview:

### Scrollbar merged into settings content

The settings panels' scrollbar overlays/merges with the content edge. Fix: give
every settings `ScrollView` a right content inset so the bar has its own gutter,
and style it per theme (WinUI default overlay scrollbar + zero padding is the
culprit on Windows). Apply uniformly across all settings sections.

### Machine editor must not cover the edited node

The machine settings popup currently floats over the canvas and can sit on top of
the very node being edited. Decided direction (user, open to better ideas — "I
just wanna see the machine node as well as the settings in one view"):

- Replace the floating popup with a **docked side panel** (right edge, like the
  existing Auto-Plan panel pattern) hosting the same controls
  (`MainPage.MachineEditor.cs` content).
- When the panel opens (or the selection changes), if the selected node would be
  hidden under the panel, **auto-pan the canvas** the minimum distance to bring
  the node fully into the visible region beside the panel; highlight the node so
  the association is obvious.
- The panel is live-bound to the selected node — clicking another machine retargets
  the panel without closing it.
- The same rule applies to the recipe chooser when it opens from a port drag: it
  must not cover the drag's origin node.

**Acceptance:** with a node selected at any canvas position, opening the editor
always leaves the node fully visible and highlighted next to the panel; settings
pages show a clean scrollbar gutter with no content underneath the bar.

## #10 — Further UI/UX improvements (discussion backlog)

All four directions were selected as interesting. These are **proposals for
discussion**, not commitments — pull items into their own spec when picked up.

**Keyboard & quick actions**
- Quick recipe search palette (Ctrl+K): type to place a recipe at viewport center.
- Del / Ctrl+D (duplicate) / arrow-key nudge on selection; Ctrl+A select-all in scope.
- While dragging from a port: Esc cancels; modifier to drop a Splitter/Merger inline.

**Canvas organization**
- Align / distribute selected nodes (left/center/right, equal spacing).
- Auto-layout of a selection (the LP planner already produces layered placements —
  reuse for manual graphs).
- Labeled group frames to mark factory sections; collapse a frame into a single
  proxy node (pairs naturally with existing Outpost/Blueprint nesting).

**Navigation**
- Zoom-to-fit (whole graph / selection) and a small minimap toggle.
- Jump-to-node: search box listing nodes by recipe/part, centers on pick.
- Breadcrumb bar when inside nested Outposts/Blueprints.

**Guidance & polish**
- First-run hint overlay (place node, drag ports, open map).
- Empty-state text on a blank canvas pointing to the chooser and Auto-Plan.
- Consistent icon set + spacing audit of toolbar/popups; hover tooltips on every
  icon-only button (some exist already via `ToolTipProperties`).

Suggested first picks: zoom-to-fit and align/distribute (high value, low risk),
then the Ctrl+K palette.

## Recommended model & effort

Ship as three independent slices — they share no code:

| Slice | Model | Effort | Why |
|---|---|---|---|
| #2 chooser output icons | Haiku 4.5 (Sonnet 4.6 if row layout fights) | low | Tiny: extend `RecipeListItem`, one ViewModel loop, two XAML templates. |
| #3 port/connection labels | Sonnet 4.6 | medium | Self-contained `FactoryCanvasDrawable` graphics work; iterate visually with `/run` + screenshots. |
| #9 settings scrollbar + editor side panel | Opus 4.8 | high (panel), low (scrollbar) | The side panel touches popup lifecycle, selection retargeting, and canvas auto-pan, plus WinUI platform quirks — the fiddliest UI piece in the batch. |
| #10 backlog | — | — | Discussion only; each picked item gets its own mini-spec. |

## Implementation plan

### Slice A — #2 output icons (smallest, do first)
1. `RecipeListItem`: `Icon` → `IReadOnlyList<ImageSource> OutputIcons` (keep
   `Icon` as first-output convenience for other call sites) + optional
   `InputIcons`.
2. `RecipeChooserViewModel.Refresh()`: build icon lists from `recipe.Outputs`
   (all of them, recipe order) and `recipe.Inputs` via `IconStore.GetSource`.
3. `MainPage.xaml` chooser templates (~lines 304–334): replace the single
   24-unit `Image` cell with a horizontal icon stack (primary output ~20 px,
   byproduct ~16 px, optional right-aligned input icons ~14 px). Check row
   height stays compact at default UI size; drop input icons if it crowds.
4. Reuse the same row template for the planner recipe toggle list
   ([planner-recipe-control.md](planner-recipe-control.md) Phase 4).

### Slice B — #3 label pills
5. `CanvasTheme`: add label-pill background/text color tokens for both themes
   (contrast-checked against canvas + grid).
6. `FactoryCanvasDrawable` port labels (~lines 396–408): measure with
   `canvas.GetStringSize`, draw rounded pill sized to text, then the string —
   replacing the fixed 60-unit boxes. Same treatment for connection mid-labels
   (~lines 234–243; `ConnectionLabelRect` must stay in sync for hit-testing).
7. Zoom rules: compute effective on-screen px (`fontSize × Zoom`); clamp label
   font upward to a minimum legible screen size; below the zoom where clamped
   pills would collide with a neighboring node's pills (simple heuristic:
   clamped pill width > available gap), skip port labels and rely on hover —
   the tooltip path (~line 475) already exists; extend hover hit-testing to
   port rects if it only covers nodes.
8. Verify per acceptance: side-by-side nodes with 4-digit ppm, light + dark,
   three zoom levels; screenshot comparison.

### Slice C — #9 settings scrollbar (quick win)
9. Add right padding/margin so content clears the overlay scrollbar on every
   settings `ScrollView` (`MainPage.xaml` settings sections, ~lines 414–494) —
   via the shared `Panel`/section style, not per-page one-offs. If the WinUI
   overlay bar still overlaps, a small Windows handler mapping to
   `ScrollViewer` padding fixes it centrally.

### Slice D — #9 machine editor side panel
10. Re-anchor `MachinePopup` (`MainPage.xaml` ~line 341) from float-near-node to
    a right-docked panel (same pattern as `AutoPlanPanel`); remove the
    position-by-node logic in `MainPage.MachineEditor.cs`.
11. Auto-pan: on open/retarget, compute the node's screen rect
    (`drawable.WorldToScreen` + `NodeLayout.Bounds`); if it intersects the panel
    rect, animate `PanX/PanY` by the minimal delta to clear it (reuse
    `SyncPanToDocument`). Highlight = existing selection ring.
12. Live retarget: while the panel is open, selection change rebinds it to the
    new node (the popup-load path already exists; guard `_popupLoading`).
    Clicking empty canvas closes it (existing `CloseOverlays` event).
13. Apply the same no-cover rule to `ChooserPanel` when opened from a port drag:
    if the chooser would cover the drag-origin node, place it on the opposite
    side or auto-pan likewise.
14. Regression sweep with `/run`: popup workflows (clock, sloop, auto-round
    toggle, limit edit), chooser from double-click and from port drag, panel +
    map mode together.
