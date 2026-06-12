namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Base for special-purpose machines: Space Elevator, AWESOME Sink,
/// Dimensional Depot Uploader, and FICSMAS Gift Tree.  Family surface
/// (capacities for AWESOME Sink belt marks, Dimensional Depot upload rates, etc.)
/// is declared here.
/// </summary>
public abstract class SpecialBase : MachineBase
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
