using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// One vehicle circuit read from a save: the stations that exchange cargo. For trucks that is
/// every station whose docking path node shares one road network; for drones, a paired pair of
/// ports. <see cref="SaveImport"/> treats the circuit as transport between the stations, so
/// producer→consumer edges across it come out tagged with the <see cref="Kind"/>.
/// </summary>
public sealed record SaveVehicleRoute(LogisticsKind Kind, IReadOnlyList<string> Stations);
