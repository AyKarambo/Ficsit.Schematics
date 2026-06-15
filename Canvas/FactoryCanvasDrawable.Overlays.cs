using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Solver;
using Ficsit.Schematics.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Canvas;

public sealed partial class FactoryCanvasDrawable
{
    // ------------------------------------------------------------- label pills

    /// <summary>
    /// Measures <paramref name="text"/> at the clamped label font size, draws a
    /// rounded-rectangle pill sized to the text, then the text on top — replacing
    /// the legacy fixed-width boxes (Slice B — #3).
    ///
    /// The pill is anchored either with its right edge at <paramref name="x"/> (when
    /// <paramref name="anchorRight"/> is true, i.e. input-side labels) or with its
    /// left edge at <paramref name="x"/> (output-side and connection labels).
    /// </summary>
    /// <summary>Memoized <see cref="ICanvas.GetStringSize"/> (font is always bold). The
    /// measurement for a (text, size) pair is invariant, so this is safe to cache forever;
    /// a hard cap guards against unbounded growth across many zoom levels.</summary>
    private SizeF MeasureText(ICanvas canvas, string text, Microsoft.Maui.Graphics.Font font, float fontSize)
    {
        var key = (text, fontSize);
        if (_textSizeCache.TryGetValue(key, out var size)) return size;
        if (_textSizeCache.Count > 4096) _textSizeCache.Clear();
        return _textSizeCache[key] = canvas.GetStringSize(text, font, fontSize);
    }

    private void DrawLabelPill(ICanvas canvas, string text, float x, float centerY, bool anchorRight)
    {
        // Clamp the font upward so effective on-screen size never drops below the
        // minimum legible pixel height (LabelMinEffectivePx / Zoom restores world units).
        var fontSize = MathF.Max(NodeLayout.LabelFontSize, NodeLayout.LabelMinEffectivePx / Zoom);
        var font = Microsoft.Maui.Graphics.Font.DefaultBold;

        var textSize = MeasureText(canvas, text, font, fontSize);
        var pillW = textSize.Width + NodeLayout.LabelPillPadX * 2;
        var pillH = textSize.Height + NodeLayout.LabelPillPadY * 2;
        var pillX = anchorRight ? x - pillW : x;
        var pillY = centerY - pillH / 2;
        var pillRect = new RectF(pillX, pillY, pillW, pillH);

        canvas.FillColor = Theme.LabelPillBackground;
        canvas.FillRoundedRectangle(pillRect, NodeLayout.LabelPillCorner);

        canvas.FontColor = Theme.LabelPillText;
        canvas.FontSize = fontSize;
        canvas.Font = font;
        canvas.DrawString(text, pillRect, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // --------------------------------------------------------------- adorners

    private void DrawAdorners(ICanvas canvas)
    {
        if (PendingWire is { } wire)
        {
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 1.5f;
            canvas.StrokeDashPattern = [4, 3];
            canvas.DrawLine(wire.From, wire.To);
            canvas.StrokeDashPattern = null;
        }

        if (RubberBand is { } band)
        {
            canvas.FillColor = Theme.RubberBand;
            canvas.FillRectangle(band);
            canvas.StrokeColor = Theme.SelectedBorder;
            canvas.StrokeSize = 1f;
            canvas.DrawRectangle(band);
        }

        if (Tooltip is { } tooltip)
        {
            canvas.FontSize = 11f;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            var size = canvas.GetStringSize(tooltip.Text, Microsoft.Maui.Graphics.Font.Default, 11f);
            var rect = new RectF(tooltip.Screen.X + 14, tooltip.Screen.Y + 18, size.Width + 16, size.Height + 10);
            canvas.FillColor = Theme.TooltipBackground;
            canvas.FillRoundedRectangle(rect, 6);
            canvas.StrokeColor = Theme.TooltipBorder;
            canvas.StrokeSize = 1f;
            canvas.DrawRoundedRectangle(rect, 6);
            canvas.FontColor = Theme.TooltipText;
            canvas.DrawString(tooltip.Text, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
