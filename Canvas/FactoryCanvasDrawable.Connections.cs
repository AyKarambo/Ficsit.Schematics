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

    private void DrawConnection(
        ICanvas canvas,
        NodeConnection connection,
        IReadOnlyDictionary<FactoryNode, NodeLayout> layouts,
        SolveResult result)
    {
        var fromNode = VisibleRep(connection.From);
        var toNode = VisibleRep(connection.To);
        if (fromNode is null || toNode is null || fromNode == toNode) return;
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

        canvas.StrokeColor = state.Settings.WireColorByPart ? PartPalette.ColorFor(connection.Part) : Theme.Wire;
        canvas.StrokeSize = 2f;

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

        // Mid label: part icon + flow ppm — pill treatment (Slice B #3).
        var flow = result.FlowOf(connection);
        var icon = icons.GetImage(connection.Part);
        const float iconSize = 16f;
        if (icon is not null)
            canvas.DrawImage(icon, mid.X - iconSize - 2, mid.Y - iconSize / 2, iconSize, iconSize);
        // The label sits to the right of the icon, centred on mid.Y.
        DrawLabelPill(canvas, numbers.Connection(flow), mid.X, mid.Y, anchorRight: false);
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
