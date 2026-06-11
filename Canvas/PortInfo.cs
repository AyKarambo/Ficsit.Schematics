namespace Ficsit.Schematics.Canvas;

/// <summary>One port on a node card: the part it carries and its icon hit-rect (world space).</summary>
public sealed record PortInfo(string Part, RectF IconRect, bool IsInput);
