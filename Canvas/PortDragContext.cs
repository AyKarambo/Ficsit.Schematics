using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Canvas;

/// <summary>A port drag that ended on empty canvas; the page turns it into a filtered quick-add.
/// <paramref name="Outside"/> marks an edge-rail drop inside an outpost: the chosen node is
/// created outside the active outpost (next to its box), making the connection a boundary
/// crossing.</summary>
public readonly record struct PortDragContext(FactoryNode Node, string Part, bool FromOutput, bool Outside = false);
