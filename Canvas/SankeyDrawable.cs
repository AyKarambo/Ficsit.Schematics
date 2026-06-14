using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Planning;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// Immediate-mode Sankey renderer for a <see cref="PlanFlows"/> graph.
/// Hosted in a <c>GraphicsView</c> inside the draft panel.
/// Left-to-right: raw nodes → recipe columns → target/sink nodes.
/// Band width ∝ parts/min; colors keyed by part via <see cref="PartPalette"/>.
/// </summary>
public sealed class SankeyDrawable : IDrawable
{
    // ---- layout constants -----------------------------------------------
    private const float NodeW   = 18f;
    private const float NodeGap = 12f;   // vertical gap between nodes in a column
    private const float ColGap  = 120f;  // horizontal gap between columns
    private const float Pad     = 20f;   // outer padding
    private const float MinBandW = 2f;  // minimum band width so tiny flows stay visible
    private const float MaxBandW = 36f; // maximum band width
    private const float LabelFontSize = 10f;
    private const float NodeLabelOffset = 5f;

    // ---- state ----------------------------------------------------------
    private PlanFlows? _flows;
    private CanvasTheme _theme = CanvasTheme.Dark;

    // Built each Draw call.
    private Dictionary<string, RectF> _nodeRects = [];

    public void SetFlows(PlanFlows? flows, CanvasTheme theme)
    {
        _flows = flows;
        _theme = theme;
    }

    // ---------------------------------------------------------------- draw

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // Background fill.
        canvas.FillColor = _theme.CardBackground;
        canvas.FillRectangle(dirtyRect);

        var flows = _flows;
        if (flows is null || flows.Nodes.Count == 0)
        {
            canvas.FontColor = _theme.MutedText;
            canvas.FontSize = 12f;
            canvas.DrawString("No flow data.", dirtyRect.Center.X - 50, dirtyRect.Center.Y - 8, 100, 16,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        // ----------------------------------------------------------------
        // Layout phase: assign (x, y, height) to each node.
        // Node height ∝ total flow through it; clamped to MinBandW.
        // ----------------------------------------------------------------
        var nodesByCol = flows.Nodes
            .GroupBy(n => n.Column)
            .OrderBy(g => g.Key)
            .ToList();
        var maxCol = nodesByCol.Count > 0 ? nodesByCol.Max(g => g.Key) : 0;

        // Total flow per node (sum of incoming or outgoing, whichever is larger).
        var nodeFlow = new Dictionary<string, Rational>();
        foreach (var node in flows.Nodes) nodeFlow[node.Id] = Rational.Zero;

        foreach (var link in flows.Links)
        {
            if (link.FromId is not null && nodeFlow.ContainsKey(link.FromId))
                nodeFlow[link.FromId] = nodeFlow[link.FromId] + link.Ppm;
            if (link.ToId is not null && nodeFlow.ContainsKey(link.ToId))
                nodeFlow[link.ToId] = nodeFlow[link.ToId] + link.Ppm;
        }

        // Global max flow to scale band widths.
        var maxFlow = nodeFlow.Count > 0 ? nodeFlow.Values.Max() : Rational.Zero;

        float ScaleBand(Rational ppm)
        {
            if (!maxFlow.IsPositive) return MinBandW;
            // Rational has no float operator; convert via double numerator/denominator.
            var ratio = (float)((double)ppm.Numerator / (double)ppm.Denominator
                                / ((double)maxFlow.Numerator / (double)maxFlow.Denominator));
            return Math.Max(MinBandW, ratio * MaxBandW);
        }

        float NodeHeight(string id)
            => nodeFlow.TryGetValue(id, out var f) ? ScaleBand(f) : MinBandW;

        // Available drawing area (exclude padding).
        var drawW = dirtyRect.Width  - 2 * Pad;
        var drawH = dirtyRect.Height - 2 * Pad;

        // Column x positions (evenly spaced).
        var colCount = maxCol + 1;
        var colXStep = colCount > 1 ? (drawW - NodeW) / (colCount - 1) : 0;

        float ColX(int col) => Pad + col * colXStep;

        // Vertical layout per column.
        _nodeRects = new Dictionary<string, RectF>();

        foreach (var colGroup in nodesByCol)
        {
            var col = colGroup.Key;
            var nodesInCol = colGroup.OrderBy(n => n.Id).ToList();
            var totalH = nodesInCol.Sum(n => NodeHeight(n.Id)) + Math.Max(0, nodesInCol.Count - 1) * NodeGap;
            var startY = Pad + Math.Max(0f, (drawH - totalH) / 2f);

            var y = startY;
            foreach (var node in nodesInCol)
            {
                var h = NodeHeight(node.Id);
                _nodeRects[node.Id] = new RectF(ColX(col), y, NodeW, h);
                y += h + NodeGap;
            }
        }

        // ----------------------------------------------------------------
        // Draw bands (links) first, then nodes on top, then labels.
        // ----------------------------------------------------------------

        // Track how much of each node's vertical space has been used
        // (separately for the outgoing side and the incoming side).
        var usedOutY = new Dictionary<string, float>();
        var usedInY  = new Dictionary<string, float>();
        foreach (var node in flows.Nodes)
        {
            var top = _nodeRects.TryGetValue(node.Id, out var r) ? r.Top : 0f;
            usedOutY[node.Id] = top;
            usedInY[node.Id]  = top;
        }

        // Sort links so same-part flows stay together visually.
        var sortedLinks = flows.Links
            .OrderBy(l => l.FromId ?? "")
            .ThenBy(l => l.Part)
            .ToList();

        foreach (var link in sortedLinks)
        {
            var bandH = ScaleBand(link.Ppm);

            // Source anchor.
            if (!_nodeRects.TryGetValue(link.FromId, out var fromRect)) continue;
            var x0 = fromRect.Right;
            var y0 = usedOutY[link.FromId];
            usedOutY[link.FromId] += bandH;

            // Destination anchor.
            if (!_nodeRects.TryGetValue(link.ToId, out var toRect)) continue;
            var x1 = toRect.Left;
            var y1 = usedInY[link.ToId];
            usedInY[link.ToId] += bandH;

            // Fill color = part palette color, semi-transparent.
            var partColor = PartPalette.ColorFor(link.Part);
            canvas.FillColor = partColor.WithAlpha(0.48f);
            canvas.StrokeColor = partColor.WithAlpha(0.72f);
            canvas.StrokeSize = 0.5f;

            // Cubic-bezier band rendered as a filled path.
            var path = new PathF();
            var cpOffset = Math.Max(20f, (x1 - x0) * 0.45f);

            // Top edge (left→right).
            path.MoveTo(x0, y0);
            path.CurveTo(x0 + cpOffset, y0, x1 - cpOffset, y1, x1, y1);
            // Right side down.
            path.LineTo(x1, y1 + bandH);
            // Bottom edge (right→left).
            path.CurveTo(x1 - cpOffset, y1 + bandH, x0 + cpOffset, y0 + bandH, x0, y0 + bandH);
            path.Close();

            canvas.FillPath(path);
            canvas.DrawPath(path);
        }

        // ----------------------------------------------------------------
        // Draw nodes (filled rectangles) on top of bands.
        // ----------------------------------------------------------------
        foreach (var node in flows.Nodes)
        {
            if (!_nodeRects.TryGetValue(node.Id, out var rect)) continue;

            canvas.FillColor = node.Kind switch
            {
                PlanFlows.NodeKind.Raw    => _theme.UnusedFlag,
                PlanFlows.NodeKind.Target => CanvasTheme.Accent,
                PlanFlows.NodeKind.Sink   => _theme.UnmadeFlag,
                _                         => _theme.CardBorder,
            };
            canvas.FillRectangle(rect);
        }

        // ----------------------------------------------------------------
        // Draw labels alongside nodes.
        // ----------------------------------------------------------------
        canvas.FontSize  = LabelFontSize;
        canvas.FontColor = _theme.Text;

        foreach (var node in flows.Nodes)
        {
            if (!_nodeRects.TryGetValue(node.Id, out var rect)) continue;

            // Truncate label to avoid overflow.
            var label = TruncateLabel(node.Label, 22);

            // Left-column nodes label on the right; all others label on the left.
            if (node.Column == 0)
            {
                var lx = rect.Right + NodeLabelOffset;
                var ly = rect.Center.Y - LabelFontSize / 2f;
                canvas.DrawString(label, lx, ly, ColGap - NodeLabelOffset - 4, LabelFontSize + 4,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
            else
            {
                // Label above node for recipe columns; to the left for terminal columns.
                var lx = rect.Left - NodeLabelOffset - ColGap + 8;
                var ly = rect.Center.Y - LabelFontSize / 2f;
                canvas.DrawString(label, lx, ly, ColGap - NodeLabelOffset - 4, LabelFontSize + 4,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }
        }
    }

    private static string TruncateLabel(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
