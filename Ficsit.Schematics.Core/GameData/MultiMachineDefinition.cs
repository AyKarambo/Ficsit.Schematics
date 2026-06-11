namespace Ficsit.Schematics.Core.GameData;

/// <summary>
/// A machine family with selectable variants (Miner Mk.1/2/3) and/or capacity
/// modes (Impure/Normal/Pure purity, belt marks, upload rates).
/// </summary>
public sealed class MultiMachineDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>True when the node displays parts-per-minute instead of machine count by default.</summary>
    public bool ShowPpm { get; set; }

    public bool AutoRound { get; set; } = true;

    /// <summary>Initial limit when the node is placed ("" = none).</summary>
    public string DefaultMax { get; set; } = string.Empty;

    public List<MultiMachineVariant> Machines { get; set; } = [];
    public List<MultiMachineCapacity> Capacities { get; set; } = [];
}
