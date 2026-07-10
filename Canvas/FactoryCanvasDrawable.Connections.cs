using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Solver;
using Ficsit.Schematics.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Canvas;

public sealed partial class FactoryCanvasDrawable
{
    // ------------------------------------------------------------ connections

    // Red tint for over-capacity connections.
    private static readonly Color OverCapacityColor = Color.FromArgb("#CC3300");

    // Auto-derived outpost boundary chips (no stored handles): one per crossing part, pinned to
    // the viewport sides in fixed screen slots — incoming parts on the left rail, outgoing on
    // the right — so they never move with the members. Wires run chip ↔ member port.
    private const float BoundaryMarkerSize = 22f;
    private const float RailMarginPx = 14f;
    private const float RailTopPx = 96f;
    private const float RailGapPx = 10f;

    // Vehicle logistics links (truck/drone/train) draw dashed so they read as "shipped, not belted".
    private static readonly float[] LogisticsDash = [8f, 5f];

    private void DrawConnection(
        ICanvas canvas,
        NodeConnection connection,
        IReadOnlyDictionary<FactoryNode, NodeLayout> layouts,
        SolveResult result)
    {
        var fromNode = VisibleRep(connection.From);
        var toNode = VisibleRep(connection.To);
        if (fromNode == toNode) return; // wholly inside one box, or both outside this scope
        if (fromNode is null || toNode is null) return; // crossings draw as boundary rails
        if (!layouts.TryGetValue(fromNode, out var fromLayout)
            || !layouts.TryGetValue(toNode, out var toLayout))
            return;

        var fromPort = fromLayout.Outputs.FirstOrDefault(p => p.Part == connection.Part)
            ?? fromLayout.Outputs.FirstOrDefault();
        var toPort = toLayout.Inputs.FirstOrDefault(p => p.Part == connection.Part)
            ?? toLayout.Inputs.FirstOrDefault();
        var start = fromPort is not null
            ? fromLayout.PortAnchor(fromPort)
            : new PointF(fromLayout.Bounds.Right, fromLayout.Bounds.Center.Y);
        var end = toPort is not null
            ? toLayout.PortAnchor(toPort)
            : new PointF(toLayout.Bounds.Left, toLayout.Bounds.Center.Y);

        // Determine if this connection is over capacity — vehicles aren't belts, so a
        // logistics link never warns.
        var isVehicle = connection.Logistics != LogisticsKind.None;
        var flow = result.FlowOf(connection);
        ConnectionOverflow? overflow = null;
        if (state.Settings.ShowBeltCapacityWarnings && !isVehicle && flow > Rational.Zero)
        {
            var isFluid = state.Data.PartsByName.TryGetValue(connection.Part, out var partDef) && partDef.Fluid;
            var threshold = isFluid ? state.Data.MaxPipeThroughput : state.Data.MaxBeltThroughput;
            overflow = ConnectionOverflowHelper.Check(flow, threshold);
        }

        canvas.StrokeColor = overflow is not null
            ? OverCapacityColor
            : state.Settings.WireColorByPart ? PartPalette.ColorFor(connection.Part) : Theme.Wire;
        canvas.StrokeSize = overflow is not null ? 3f : 2f;

        if (isVehicle) canvas.StrokeDashPattern = LogisticsDash;
        var mid = DrawWirePath(canvas, start, end);
        if (isVehicle) canvas.StrokeDashPattern = null;

        // Mid label: part icon + flow ppm — pill treatment (Slice B #3).
        var icon = icons.GetImage(connection.Part);
        const float iconSize = 16f;
        if (icon is not null)
            canvas.DrawImage(icon, mid.X - iconSize - 2, mid.Y - iconSize / 2, iconSize, iconSize);
        // The label sits to the right of the icon, centred on mid.Y.
        DrawLabelPill(canvas, numbers.Connection(flow), mid.X, mid.Y, anchorRight: false);

        // The vehicle kind under the flow pill, so a truck hop reads differently from a belt.
        if (isVehicle)
            DrawLabelPill(canvas, connection.Logistics.ToString(), mid.X, mid.Y + 18, anchorRight: false);

        // Over-capacity warning: a small warning glyph (⚠) above the mid-point.
        if (overflow is not null)
            DrawOverCapacityWarning(canvas, mid, overflow);
    }

    /// <summary>
    /// Draw the active outpost's boundary as pinned side rails: one chip per crossing part —
    /// incoming on the left rail, outgoing on the right, stacked top-down at fixed screen
    /// positions (they hold still while the interior pans/zooms and never move with the
    /// members) — with a wire from the chip to every member port it feeds or drains.
    /// </summary>
    private void DrawBoundaryRails(
        ICanvas canvas,
        List<(NodeConnection Connection, bool Incoming)> crossings,
        IReadOnlyDictionary<FactoryNode, NodeLayout> layouts,
        SolveResult result)
    {
        if (crossings.Count == 0) return;

        // Assign each (side, part) its rail slot in first-seen order; positions are derived
        // from the screen rail each frame, so pan/zoom can't carry them away.
        var chips = new Dictionary<(bool Incoming, string Part), RectF>();
        var slotCount = new int[2];
        foreach (var (connection, incoming) in crossings)
        {
            var key = (incoming, connection.Part);
            if (chips.ContainsKey(key)) continue;
            var slot = slotCount[incoming ? 0 : 1]++;
            var sizePx = BoundaryMarkerSize * Zoom;
            var screenX = incoming
                ? RailMarginPx
                : MathF.Max(RailMarginPx, _viewport.Width - RailMarginPx - sizePx);
            var screenY = RailTopPx + slot * (sizePx + RailGapPx);
            var topLeft = ScreenToWorld(new PointF(screenX, screenY));
            chips[key] = new RectF(topLeft.X, topLeft.Y, BoundaryMarkerSize, BoundaryMarkerSize);
        }

        foreach (var (connection, incoming) in crossings)
        {
            var member = incoming ? VisibleRep(connection.To) : VisibleRep(connection.From);
            if (member is null || !layouts.TryGetValue(member, out var layout)) continue;

            var ports = incoming ? layout.Inputs : layout.Outputs;
            var port = ports.FirstOrDefault(p => p.Part == connection.Part);
            var anchor = port is not null
                ? layout.PortAnchor(port)
                : new PointF(incoming ? layout.Bounds.Left : layout.Bounds.Right, layout.Bounds.Center.Y);

            var chip = chips[(incoming, connection.Part)];
            var chipAnchor = new PointF(incoming ? chip.Right : chip.Left, chip.Center.Y);
            var start = incoming ? chipAnchor : anchor;
            var end = incoming ? anchor : chipAnchor;

            var flow = result.FlowOf(connection);
            canvas.StrokeColor = state.Settings.WireColorByPart ? PartPalette.ColorFor(connection.Part) : Theme.Wire;
            canvas.StrokeSize = 2f;
            if (connection.Logistics != LogisticsKind.None) canvas.StrokeDashPattern = LogisticsDash;
            var mid = DrawWirePath(canvas, start, end);
            canvas.StrokeDashPattern = null;
            if (flow > Rational.Zero)
                DrawLabelPill(canvas, numbers.Connection(flow), mid.X, mid.Y, anchorRight: false);
        }

        // Chips last, so they sit on top of their wires.
        foreach (var ((_, part), rect) in chips)
        {
            canvas.FillColor = Theme.PortChip;
            canvas.FillRoundedRectangle(rect, 5f);
            var icon = icons.GetImage(part);
            if (icon is not null)
                canvas.DrawImage(icon, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
        }
    }

    /// <summary>Stroke the wire from <paramref name="start"/> to <paramref name="end"/> in the
    /// document's path style (Direct / 2D / Curves) and return its mid-point for labelling.</summary>
    private PointF DrawWirePath(ICanvas canvas, PointF start, PointF end)
    {
        var path = new PathF();
        path.MoveTo(start);
        PointF mid;
        if (state.Editor.Document.Path == "Direct")
        {
            path.LineTo(end);
            mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        }
        else if (state.Editor.Document.Path == "2D")
        {
            var midX = (start.X + end.X) / 2;
            path.LineTo(midX, start.Y);
            path.LineTo(midX, end.Y);
            path.LineTo(end.X, end.Y);
            mid = new PointF(midX, (start.Y + end.Y) / 2);
        }
        else
        {
            var dx = Math.Max(30f, Math.Abs(end.X - start.X) / 2);
            path.CurveTo(start.X + dx, start.Y, end.X - dx, end.Y, end.X, end.Y);
            mid = BezierPoint(start, new PointF(start.X + dx, start.Y), new PointF(end.X - dx, end.Y), end, 0.5f);
        }
        canvas.DrawPath(path);
        return mid;
    }

    private void DrawOverCapacityWarning(ICanvas canvas, PointF mid, ConnectionOverflow overflow)
    {
        // Draw a small warning indicator above the connection mid-label.
        // The glyph is a "!" in a red circle, positioned above the flow label.
        const float glyphSize = 14f;
        const float glyphOffset = 20f; // above mid

        var gx = mid.X - glyphSize / 2;
        var gy = mid.Y - glyphOffset - glyphSize;

        canvas.FillColor = OverCapacityColor;
        canvas.FillEllipse(gx, gy, glyphSize, glyphSize);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 9f;
        canvas.DrawString("!", gx, gy, glyphSize, glyphSize,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // Tooltip-style label: "N× needed" hint, drawn as a small pill below the glyph.
        // The actual tooltip text is set on hover by CanvasController; here we show a
        // compact "×N" suffix on the flow pill so it's visible without hovering.
        // (The canonical tooltip is handled in CanvasController.Queries.cs.)
    }

    /// <summary>
    /// Hit / exclusion rect for the connection mid-label pill. The pill's exact size
    /// depends on the measured text, but for hit-testing a fixed conservative estimate
    /// is sufficient — slightly wider than a 5-digit ppm value at label font size.
    /// Kept in sync with <see cref="DrawConnection"/>: same mid-point, same icon offset.
    /// </summary>
    public RectF ConnectionLabelRect(NodeConnection connection)
    {
        var layouts = Layouts;
        var fromNode = VisibleRep(connection.From);
        var toNode = VisibleRep(connection.To);
        if (fromNode is null || toNode is null || fromNode == toNode
            || !layouts.TryGetValue(fromNode, out var fromLayout)
            || !layouts.TryGetValue(toNode, out var toLayout))
            return RectF.Zero;
        var start = new PointF(fromLayout.Bounds.Right, fromLayout.Bounds.Center.Y);
        var end = new PointF(toLayout.Bounds.Left, toLayout.Bounds.Center.Y);
        var mid = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        // Icon sits at [mid.X - 18, mid.Y - 8]; pill starts at mid.X (anchorRight:false).
        // Estimate: icon + pill together span ~70 wide, ~18 tall, centred on mid.Y.
        const float iconSize = 16f;
        return new RectF(mid.X - iconSize - 4, mid.Y - 9, iconSize + 70, 18);
    }

    private static PointF BezierPoint(PointF p0, PointF p1, PointF p2, PointF p3, float t)
    {
        var u = 1 - t;
        return new PointF(
            u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
            u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
    }
}
