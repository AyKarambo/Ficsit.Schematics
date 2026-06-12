namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Base for power-generating machines: Biomass Burner, Coal-Powered Generator,
/// Fuel-Powered Generator, Geothermal Generator, Nuclear Power Plant, and
/// Alien Power Augmenter.
/// </summary>
public abstract class GeneratorBase : MachineBase
{
    /// <summary>
    /// Position in the canonical multi-machine ordering (used to sort
    /// <see cref="MachineBase.ToFamilyDefinition()"/> results).  Only meaningful
    /// when <see cref="MachineBase.ToFamilyDefinition()"/> returns non-null.
    /// </summary>
    public virtual int FamilySortIndex => int.MaxValue;

    /// <summary>True when nodes display parts-per-minute instead of machine count by default.</summary>
    public virtual bool ShowPpm => false;

    public virtual bool AutoRound => true;

    /// <summary>Initial limit when a node is placed ("" = none).</summary>
    public virtual string DefaultMax => "";

    public virtual IReadOnlyList<MultiMachineCapacity> FamilyCapacities => [];
}
