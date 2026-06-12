namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Base for resource-extraction machines: Miners, Oil Extractor, Water Extractor,
/// Resource Well Pressurizer and Extractor.  Family surface (<see cref="ShowPpm"/>,
/// <see cref="AutoRound"/>, <see cref="DefaultMax"/>, variants and capacities) is
/// declared here so merged family classes can override it.
/// </summary>
public abstract class ExtractorBase : MachineBase
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

    public virtual IReadOnlyList<MultiMachineVariant> FamilyMachines => [];

    public virtual IReadOnlyList<MultiMachineCapacity> FamilyCapacities => [];
}
