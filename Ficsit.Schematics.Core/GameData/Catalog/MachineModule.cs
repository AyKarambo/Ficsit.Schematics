using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of machines for one category, authored as <see cref="MachineGroup"/>s so a
/// multi-machine family lives next to the machine(s) it describes. Discovered via
/// reflection by <see cref="GameDataCatalog"/>; each entry carries its canonical sort key.
/// Quantity arguments are written as canonical fraction strings and parsed to exact
/// <see cref="Rational"/> values.
/// </summary>
public abstract class MachineModule
{
    protected abstract IReadOnlyList<MachineGroup> Groups { get; }

    public IReadOnlyList<(int Sort, MachineDefinition Definition)> Machines
        => Groups.SelectMany(group => group.Machines).ToList();

    public IReadOnlyList<(int Sort, MultiMachineDefinition Definition)> Families
        => Groups.Where(group => group.Family is not null)
                 .Select(group => (group.FamilySort, group.Family!))
                 .ToList();

    /// <summary>Standard machine power curve: power ∝ clock^1.32 (most production/extraction machines).</summary>
    protected const string StandardOverclock = "1321929/1000000";

    /// <summary>Resource-node purity capacities (×½ / ×1 / ×2), shared by the extractor families.</summary>
    protected static MultiMachineCapacity[] Purity =>
    [
        Cap("Impure", "1/2", color: 13775920),
        Cap("Normal", isDefault: true),
        Cap("Pure", "2", color: 8433977),
    ];

    // ------------------------------------------------------------------ builders

    /// <summary>A standalone machine. Pass <c>overclockExp: null</c> for machines with no
    /// overclock scaling, or <c>"1"</c> for generators (linear).</summary>
    protected static MachineGroup Machine(int sort, string name, Tier tier,
        string? power = null, string? minPower = null, string? basePower = null,
        string? basePowerBoost = null, string? fueledBasePowerBoost = null,
        string? overclockExp = StandardOverclock,
        int sloops = 0, string? sloopMultiplier = null, string? sloopPowerExp = null,
        CostEntry[]? cost = null)
        => new([(sort, Def(name, tier, power, minPower, basePower, basePowerBoost,
            fueledBasePowerBoost, overclockExp, sloops, sloopMultiplier, sloopPowerExp, cost))]);

    /// <summary>A multi-mark family (e.g. the Miner): the marks ARE the machines, and recipes
    /// target the family <paramref name="name"/>.</summary>
    protected static MachineGroup Family(int familySort, string name,
        bool showPpm = false, bool autoRound = true, string defaultMax = "",
        MarkSpec[]? marks = null, MultiMachineCapacity[]? capacities = null)
    {
        marks ??= [];
        var family = new MultiMachineDefinition
        {
            Name = name,
            ShowPpm = showPpm,
            AutoRound = autoRound,
            DefaultMax = defaultMax,
            Machines = marks.Select(mark => mark.Variant).ToList(),
            Capacities = (capacities ?? []).ToList(),
        };
        return new MachineGroup(marks.Select(mark => (mark.Sort, mark.Machine)).ToList(), familySort, family);
    }

    /// <summary>One mark of a multi-mark family: a machine plus its throughput multiplier.</summary>
    protected static MarkSpec Mark(int sort, string name, Tier tier, string throughput,
        bool isDefault = false, string? power = null, CostEntry[]? cost = null)
        => new(sort,
            Def(name, tier, power, overclockExp: StandardOverclock, cost: cost),
            new MultiMachineVariant { Name = name, PartsRatio = Rational.Parse(throughput), Default = isDefault });

    /// <summary>A build-cost entry.</summary>
    protected static CostEntry C(string part, int amount) => new() { Part = part, Amount = amount };

    /// <summary>A family capacity mode (resource purity, belt mark, upload rate, …).</summary>
    protected static MultiMachineCapacity Cap(string name, string? partsRatio = null,
        int? color = null, bool isDefault = false, string? powerRatio = null)
        => new() { Name = name, PartsRatio = Rat(partsRatio), Color = color, Default = isDefault, PowerRatio = Rat(powerRatio) };

    private static Rational? Rat(string? text) => text is null ? null : Rational.Parse(text);

    private static MachineDefinition Def(string name, Tier tier,
        string? power, string? minPower, string? basePower, string? basePowerBoost,
        string? fueledBasePowerBoost, string? overclockExp, int sloops,
        string? sloopMultiplier, string? sloopPowerExp, CostEntry[]? cost)
        => new()
        {
            Name = name,
            Tier = tier,
            AveragePower = Rat(power),
            MinPower = Rat(minPower),
            BasePower = Rat(basePower),
            BasePowerBoost = Rat(basePowerBoost),
            FueledBasePowerBoost = Rat(fueledBasePowerBoost),
            OverclockPowerExponent = Rat(overclockExp),
            MaxProductionShards = sloops,
            ProductionShardMultiplier = Rat(sloopMultiplier),
            ProductionShardPowerExponent = Rat(sloopPowerExp),
            Cost = cost?.ToList() ?? [],
        };

    private static MachineDefinition Def(string name, Tier tier, string? power = null,
        string? overclockExp = null, CostEntry[]? cost = null)
        => Def(name, tier, power, null, null, null, null, overclockExp, 0, null, null, cost);
}
