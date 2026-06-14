namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of machines (and the multi-machine families they form) authored as
/// <see cref="MachineDefinition"/> / <see cref="MultiMachineDefinition"/> rows.
/// Discovered via reflection by <see cref="GameDataCatalog"/>; each row carries
/// its canonical sort key. The <see cref="C"/> / <see cref="V"/> / <see cref="Cap"/>
/// helpers keep the cost, variant and capacity lists terse.
/// </summary>
public abstract class MachineModule
{
    /// <summary>Standalone machines (and family variants), each with its sort key.</summary>
    public abstract IReadOnlyList<(int Sort, MachineDefinition Definition)> Machines { get; }

    /// <summary>Multi-machine families (Miner Mk.1/2/3, belt-mark sinks, …); empty for most modules.</summary>
    public virtual IReadOnlyList<(int Sort, MultiMachineDefinition Definition)> Families => [];

    /// <summary>A build-cost entry.</summary>
    protected static CostEntry C(string part, int amount) => new() { Part = part, Amount = amount.ToString() };

    /// <summary>A build-cost entry with a fractional amount.</summary>
    protected static CostEntry C(string part, string amount) => new() { Part = part, Amount = amount };

    /// <summary>A family variant (a selectable machine mark).</summary>
    protected static MultiMachineVariant V(string name, string? partsRatio = null, bool isDefault = false)
        => new() { Name = name, PartsRatio = partsRatio, Default = isDefault };

    /// <summary>A family capacity mode (resource purity, belt mark, upload rate, …).</summary>
    protected static MultiMachineCapacity Cap(string name, string? partsRatio = null,
        int? color = null, bool isDefault = false, string? powerRatio = null)
        => new() { Name = name, PartsRatio = partsRatio, Color = color, Default = isDefault, PowerRatio = powerRatio };
}
