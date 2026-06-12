namespace Ficsit.Schematics.Canvas;

/// <summary>
/// World-map coordinate mapping. Satisfactory world coordinates are in
/// centimeters; the canvas uses 1 unit = 1 meter, so the ~7.5 km map becomes a
/// 7500-unit square the camera handles comfortably at existing zoom limits.
/// </summary>
public static class MapGeometry
{
    public const float CmPerUnit = 100f;

    // In-game map extents in cm (the bounds the wiki map image covers).
    public const float WestCm = -324698.832f;
    public const float EastCm = 425301.832f;
    public const float NorthCm = -375000f;
    public const float SouthCm = 375000f;

    /// <summary>Marker radius in canvas units (= meters).</summary>
    public const float MarkerRadius = 18f;

    /// <summary>How close a dropped extractor must be to snap, in canvas units.</summary>
    public const float SnapRadius = 150f;

    public static RectF MapRect => new(
        WestCm / CmPerUnit,
        NorthCm / CmPerUnit,
        (EastCm - WestCm) / CmPerUnit,
        (SouthCm - NorthCm) / CmPerUnit);

    public static PointF ToCanvas(double xCm, double yCm)
        => new((float)(xCm / CmPerUnit), (float)(yCm / CmPerUnit));
}
