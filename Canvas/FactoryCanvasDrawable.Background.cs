using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Solver;
using Ficsit.Schematics.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Canvas;

public sealed partial class FactoryCanvasDrawable
{
    private IImage? _mapImage;
    private bool _mapLoadAttempted;

    /// <summary>World map plus the imported resource-node markers.</summary>
    private void DrawMap(ICanvas canvas, RectF dirtyRect)
    {
        if (!_mapLoadAttempted)
        {
            _mapLoadAttempted = true;
            _mapImage = icons.GetAsset("map/world_map.jpg");
        }

        var rect = MapGeometry.MapRect;
        if (_mapImage is not null)
        {
            // Dim the artwork so factory cards and wires stay readable.
            canvas.Alpha = Theme == CanvasTheme.Dark ? 0.55f : 0.8f;
            canvas.DrawImage(_mapImage, rect.X, rect.Y, rect.Width, rect.Height);
            canvas.Alpha = 1f;
        }

        var occupied = state.OccupiedResourceNodes();
        var topLeft = ScreenToWorld(new PointF(dirtyRect.Left, dirtyRect.Top));
        var bottomRight = ScreenToWorld(new PointF(dirtyRect.Right, dirtyRect.Bottom));

        foreach (var node in state.MapNodes)
        {
            var p = MapGeometry.ToCanvas(node.X, node.Y);
            if (p.X < topLeft.X - 40 || p.X > bottomRight.X + 40
                || p.Y < topLeft.Y - 40 || p.Y > bottomRight.Y + 40) continue;
            var isPreview = SnapPreviewMarker is not null && node.Instance == SnapPreviewMarker.Instance;
            DrawMapNode(canvas, node, p, occupied.Contains(node.Instance), isPreview);
        }
    }

    private void DrawMapNode(ICanvas canvas, ResourceNodeInfo node, PointF p, bool occupied, bool isPreview = false)
    {
        var r = node.Kind == ResourceNodeKind.FrackingSatellite
            ? MapGeometry.MarkerRadius * 0.7f
            : MapGeometry.MarkerRadius;

        // Snap-preview pulse: a wide accent ring so the target reads at a glance.
        if (isPreview)
        {
            canvas.StrokeColor = CanvasTheme.Accent;
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(p, r + 5f);
        }

        canvas.FillColor = occupied
            ? CanvasTheme.AccentDeep.WithAlpha(0.85f)
            : Theme.CardBackground.WithAlpha(0.85f);
        canvas.FillCircle(p, r);

        canvas.StrokeSize = 2.5f;
        canvas.StrokeColor = node.Purity switch
        {
            "Pure" => Color.FromArgb("#4CC04C"),
            "Impure" => Color.FromArgb("#E2654E"),
            _ => Color.FromArgb("#E8C547"),
        };
        canvas.DrawCircle(p, r);

        var icon = icons.GetImage(node.Part == "Geyser" ? "Geothermal Generator" : node.Part);
        if (icon is not null)
        {
            var size = r * 1.2f;
            canvas.DrawImage(icon, p.X - size / 2, p.Y - size / 2, size, size);
        }
    }

    /// <summary>The map resource node under a world position, when map mode is on.</summary>
    public ResourceNodeInfo? HitMapNode(PointF world)
    {
        if (!state.Settings.ShowMap || state.Editor.ScopePath.Count > 0) return null;
        ResourceNodeInfo? best = null;
        var bestDistance = MapGeometry.MarkerRadius * 1.4f;
        foreach (var node in state.MapNodes)
        {
            var p = MapGeometry.ToCanvas(node.X, node.Y);
            var d = (float)Math.Sqrt((p.X - world.X) * (p.X - world.X) + (p.Y - world.Y) * (p.Y - world.Y));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = node;
            }
        }
        return best;
    }

    /// <summary>Subtle dot grid in world space; spacing doubles as you zoom out.</summary>
    private void DrawGrid(ICanvas canvas, RectF dirtyRect)
    {
        var spacing = 32f;
        while (spacing * Zoom < 14f) spacing *= 2;
        if (spacing * Zoom > 120f) return;

        var topLeft = ScreenToWorld(new PointF(dirtyRect.Left, dirtyRect.Top));
        var bottomRight = ScreenToWorld(new PointF(dirtyRect.Right, dirtyRect.Bottom));
        var radius = 1.2f / Zoom;

        canvas.FillColor = Theme.GridDot;
        for (var x = MathF.Floor(topLeft.X / spacing) * spacing; x <= bottomRight.X; x += spacing)
            for (var y = MathF.Floor(topLeft.Y / spacing) * spacing; y <= bottomRight.Y; y += spacing)
                canvas.FillCircle(x, y, radius);
    }
}
