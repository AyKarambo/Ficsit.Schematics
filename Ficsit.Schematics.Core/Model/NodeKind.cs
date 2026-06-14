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

    /// <summary>Outpost boundary: brings a part INTO the outpost (a source for the interior).
    /// <see cref="FactoryNode.Name"/> holds the part. Lives in an outpost's Children.</summary>
    Import,

    /// <summary>Outpost boundary: sends a part OUT of the outpost (a sink for the interior).</summary>
    Export,
}
