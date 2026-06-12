namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Base for storage machines: Storage Container, Industrial Storage Container,
/// Fluid Buffer, and Industrial Fluid Buffer.  Family surface is declared here
/// so storage-mark pairs can be merged into a single class with capacity modes.
/// </summary>
public abstract class StorageBase : MachineBase
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
