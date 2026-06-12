namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A machine contributed to the game catalog; discovered via reflection by
/// <see cref="GameDataCatalog"/>. Rational quantities stay fraction strings
/// (e.g. "1321929/1000000") so no precision is lost.
/// </summary>
public abstract class MachineBase
{
    /// <summary>Position in the canonical game-data ordering.</summary>
    public abstract int SortIndex { get; }

    public abstract string MachineName { get; }

    /// <summary>Unlock tier, e.g. "0-2".</summary>
    public abstract string Tier { get; }

    /// <summary>MW at 100% clock; negative = consumes, positive = generates. Null = no power.</summary>
    public virtual string? AveragePower => null;

    /// <summary>Lower bound for variable-power machines.</summary>
    public virtual string? MinPower => null;

    /// <summary>Constant draw independent of the recipe.</summary>
    public virtual string? BasePower => null;

    public virtual string? BasePowerBoost => null;

    public virtual string? FueledBasePowerBoost => null;

    /// <summary>Exponent x in power = base · clock^x.</summary>
    public virtual string? OverclockPowerExponent => null;

    /// <summary>Somersloop slots.</summary>
    public virtual int MaxProductionShards => 0;

    public virtual string? ProductionShardMultiplier => null;

    public virtual string? ProductionShardPowerExponent => null;

    public virtual IReadOnlyList<CostEntry> Cost => [];

    public MachineDefinition ToDefinition() => new()
    {
        Name = MachineName,
        Tier = Tier,
        AveragePower = AveragePower,
        MinPower = MinPower,
        BasePower = BasePower,
        BasePowerBoost = BasePowerBoost,
        FueledBasePowerBoost = FueledBasePowerBoost,
        OverclockPowerExponent = OverclockPowerExponent,
        MaxProductionShards = MaxProductionShards,
        ProductionShardMultiplier = ProductionShardMultiplier,
        ProductionShardPowerExponent = ProductionShardPowerExponent,
        Cost = Cost.ToList(),
    };

    /// <summary>
    /// All <see cref="MachineDefinition"/>s contributed by this class, each
    /// paired with its canonical sort key.  For standalone machines this is just
    /// <c>[(SortIndex, ToDefinition())]</c>.  Merged family classes override
    /// this to emit one pair per variant, each with its original SortIndex.
    /// </summary>
    public virtual IEnumerable<(int SortIndex, MachineDefinition Definition)> ToIndexedMachineDefinitions()
        => [(SortIndex, ToDefinition())];

    /// <summary>
    /// All <see cref="MachineDefinition"/>s contributed by this class.  For
    /// standalone machines this is just the single <see cref="ToDefinition()"/>;
    /// merged family classes override this to emit one definition per variant.
    /// </summary>
    public virtual IEnumerable<MachineDefinition> ToMachineDefinitions()
        => [ToDefinition()];

    /// <summary>
    /// The <see cref="MultiMachineDefinition"/> contributed by this class, or
    /// <c>null</c> if this machine has no family (the common case).  Merged
    /// family classes override this to emit their family definition.
    /// </summary>
    public virtual MultiMachineDefinition? ToFamilyDefinition() => null;
}
