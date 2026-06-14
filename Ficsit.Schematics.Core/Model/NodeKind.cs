namespace Ficsit.Schematics.Core.Model;

public enum NodeKind
{
    Recipe,
    Outpost,
    Blueprint,
    Splurger,
    PrioritySplitter,
    PriorityMerger,
    PrioritySplurger,
    AwesomeSink,
    StorageContainer,
    DimensionalDepot,

    /// <summary>An outpost boundary handle that brings a part IN (a per-part pass-through; a
    /// member of its outpost, <see cref="FactoryNode.Parent"/>). <see cref="FactoryNode.Name"/>
    /// is the part. Rendered as a left-edge handle inside the outpost.</summary>
    Import,

    /// <summary>An outpost boundary handle that sends a part OUT. Right-edge handle inside.</summary>
    Export,
}
