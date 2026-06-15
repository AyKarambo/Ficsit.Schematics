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

    /// <summary>A power generator that burns any one of its machine's fuels. <see cref="FactoryNode.Name"/>
    /// is the machine ("Fuel-Powered Generator", …); the active recipe follows the connected fuel.</summary>
    Generator,
}
