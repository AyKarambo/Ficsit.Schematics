using Microsoft.Maui.Graphics;

namespace Ficsit.Schematics.Canvas;

/// <summary>
/// A deterministic, distinct colour per part name, used to colour-code wires so a
/// belt can be traced across the canvas. The hue is a stable hash of the part name;
/// saturation/lightness are tuned to read on the dark canvas. Cached per name.
/// </summary>
public static class PartPalette
{
    private static readonly Dictionary<string, Color> Cache = [];

    public static Color ColorFor(string part)
    {
        if (Cache.TryGetValue(part, out var color)) return color;
        var hue = StableHash(part) % 360u / 360f;
        color = Color.FromHsla(hue, 0.58f, 0.62f);
        Cache[part] = color;
        return color;
    }

    private static uint StableHash(string text)
    {
        // FNV-1a — small, stable, well-spread across hues.
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }
}
