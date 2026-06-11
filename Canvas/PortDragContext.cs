using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Canvas;

/// <summary>A port drag that ended on empty canvas; the page turns it into a filtered quick-add.</summary>
public readonly record struct PortDragContext(FactoryNode Node, string Part, bool FromOutput);
