namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// How a connection's part physically travels. <see cref="None"/> is a belt or pipe; the vehicle
/// kinds mark save-imported logistics routes (a truck circuit, a paired drone port), drawn in a
/// distinct dashed style and exempt from belt-capacity warnings.
/// </summary>
public enum LogisticsKind
{
    None,
    Truck,
    Drone,
    Train,
}
