# Canvas pan performance (#11)

## Problem

Panning the whole canvas is laggy. Pan/zoom are applied as a single immediate-mode
transform — `FactoryCanvasDrawable.Draw` does `canvas.Translate(PanX, PanY)` then
`canvas.Scale(Zoom, Zoom)` (`Canvas/FactoryCanvasDrawable.cs:67-69`) — so **world-space
geometry does not change while panning**. Yet every pan tick redoes full work:

- `CanvasController.PointerMoved`, `Mode.Pan` branch (`Canvas/CanvasController.cs:105-110`),
  mutates `PanX/PanY` and calls `Invalidate?.Invoke()`. The handler wired in
  `MainPage.xaml.cs:62-66` runs **both** `Canvas.Invalidate()` **and** `UpdateStatus()`.
  `UpdateStatus()` (`MainPage.xaml.cs:~322-336`) walks
  `_state.Editor.Document.Root.AllNodes()` and does a solver `Result.For(node)` lookup per
  node, formats text, and writes UI labels — **on every pointer move** (100+/sec).
- `Draw` redraws every connection and every node each frame. `DrawConnection` allocates a
  **`new PathF()`** and recomputes bezier control points **per connection per frame**.
- `DrawLabelPill` calls `canvas.GetStringSize(text, font, fontSize)` for **every port label
  and every connection label, every frame**, with no caching — the dominant cost. The font
  is the constant `Font.DefaultBold`; the size is
  `max(LabelFontSize, LabelMinEffectivePx / Zoom)`, which is **constant during a pan**
  (zoom does not change), so the same strings are re-measured identically every frame.
- No viewport culling: nodes/connections fully off-screen are still drawn.

The layout cache itself is fine — `_layouts` is only recomputed when `_layoutsDirty`
(`Canvas/FactoryCanvasDrawable.cs:34-54`), and panning does **not** call
`InvalidateLayouts()`. So the wasted work is purely status updates + per-frame text
measurement + per-frame path allocation.

## Decided behavior

Make panning smooth on large graphs (100+ nodes) without changing what is drawn. Fixes in
impact order; (1) and (2) are the bulk of the win.

### 1. Don't run `UpdateStatus()` during interaction
The status bar (machine count / MW) cannot change from a pan, yet it is recomputed every
move. Decouple it from the per-move invalidate:
- Either gate it — skip `UpdateStatus()` while `_mode` is `Pan`/`DragNodes`/`Connect`/
  `RubberBand`, and run it once on `PointerReleased`; or
- give the controller a lightweight `InvalidateView` (canvas-only) event used by pan/zoom,
  keeping the heavyweight `Invalidate` (canvas + status) for edits.
The zoom-% label (`ZoomResetButton.Text`) only needs updating on wheel/zoom, not pan.

### 2. Memoize text measurement
Cache `GetStringSize` results in `FactoryCanvasDrawable` keyed by `(string text, float
fontSize)` (font is always `DefaultBold`). Look up before measuring in `DrawLabelPill`.
Because pan holds zoom constant, the cache hits on every label every frame after the first.
Bound or clear it on `InvalidateLayouts()` to avoid unbounded growth on documents with many
distinct labels.

### 3. Cache connection path geometry (world space)
Build each connection's `PathF` (and its mid-point for the label) once and store it
alongside the layout cache, rebuilt only when `InvalidateLayouts()` fires. Pan and zoom stay
pure canvas transforms, so cached world-space paths render correctly without rebuilding.
Invalidate when the connection style (`Document.Path`: Curves/Direct/2D) changes.

### 4. Viewport culling (optional, last)
In `Draw`, skip `DrawNode`/`DrawConnection` whose world bounds — transformed by the current
pan/zoom — do not intersect `dirtyRect`. Cheap rejection test per item; only worth doing if
(1)–(3) leave residual lag on very large graphs.

## Recommended model & effort

**Sonnet 4.6, medium.** Well-scoped and measurable. The only subtlety is cache invalidation:
the measurement cache is safe to key by `(text, fontSize)` and bound; the path cache must be
rebuilt on `InvalidateLayouts()` and on connection-style change. No new behavior — verify
existing tests stay green and pan feels smooth via `/run`.

## Implementation plan

1. **Status gating** — in `MainPage.xaml.cs:62-66`, split the invalidate handler: pan/zoom
   paths (`CanvasController` `Mode.Pan`, `ZoomAround`, `PanBy`) raise a canvas-only refresh;
   run `UpdateStatus()` on `PointerReleased` and after edits. Confirm the status text is
   correct after a pan ends.
2. **Measurement cache** — add `Dictionary<(string, float), SizeF>` to
   `FactoryCanvasDrawable`; route `DrawLabelPill`'s measurement through it; clear in
   `InvalidateLayouts()`.
3. **Path cache** — extend the layout cache (or a parallel `Dictionary<NodeConnection,
   (PathF Path, PointF Mid)>`) computed in the `Layouts` getter / a sibling builder;
   `DrawConnection` reads from it. Invalidate with layouts and when `Document.Path` changes.
4. **(Optional) culling** — add an intersection check in `Draw`'s connection/node loops
   (`Canvas/FactoryCanvasDrawable.cs:81-86`).
5. **Verify** — `/run`, open/generate a large factory (Auto-Plan a big target), pan and
   confirm smoothness; sweep connect / drag-node / rubber-band / zoom for regressions; run
   the existing test suite.

## Open questions

- Is a canvas-only invalidate event preferable to mode-gating `UpdateStatus()`? Suggest the
  event — it is explicit and reusable by zoom/`PanBy`.
- Should the path cache live inside `NodeLayout`/the `_layouts` dictionary or a separate
  connection map? Suggest a separate map keyed by `NodeConnection` since connections are not
  per-node.

## Acceptance criteria

1. Panning a 100+ node graph is visibly smooth (no per-frame stutter).
2. While panning, no `GetStringSize` call is issued for an already-measured (text, size) and
   `AllNodes()` is not walked (verify by inspection/profiling).
3. The status bar (machine count, MW) and zoom-% are correct after a pan/zoom completes.
4. Connect, drag-node, rubber-band select, double-click chooser, and zoom-around-cursor all
   behave exactly as before; existing tests pass.
