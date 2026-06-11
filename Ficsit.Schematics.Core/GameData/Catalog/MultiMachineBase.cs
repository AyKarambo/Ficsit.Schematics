namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A machine family (Miner Mk.1/2/3, AWESOME Sink belt marks, …) contributed to
/// the game catalog; discovered via reflection by <see cref="GameDataCatalog"/>.
/// </summary>
public abstract class MultiMachineBase
{
    /// <summary>Position in the canonical game-data ordering.</summary>
    public abstract int SortIndex { get; }

    public abstract string FamilyName { get; }

    /// <summary>True when nodes display parts-per-minute instead of machine count by default.</summary>
    public virtual bool ShowPpm => false;

    public virtual bool AutoRound => true;

    /// <summary>Initial limit when a node is placed ("" = none).</summary>
    public virtual string DefaultMax => "";

    public virtual IReadOnlyList<MultiMachineVariant> Machines => [];

    public virtual IReadOnlyList<MultiMachineCapacity> Capacities => [];

    public MultiMachineDefinition ToDefinition() => new()
    {
        Name = FamilyName,
        ShowPpm = ShowPpm,
        AutoRound = AutoRound,
        DefaultMax = DefaultMax,
        Machines = Machines.ToList(),
        Capacities = Capacities.ToList(),
    };
}
