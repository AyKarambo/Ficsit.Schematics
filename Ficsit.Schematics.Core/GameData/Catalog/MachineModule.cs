namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of machines for one category, authored as <see cref="MachineGroup"/>s so a
/// multi-machine family lives next to the machine(s) it describes. Discovered via
/// reflection by <see cref="GameDataCatalog"/>; each entry carries its canonical sort key.
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
    protected static MachineGroup Machine(int sort, string name, string tier,
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
    protected static MarkSpec Mark(int sort, string name, string tier, string throughput,
        bool isDefault = false, string? power = null, CostEntry[]? cost = null)
        => new(sort,
            Def(name, tier, power, overclockExp: StandardOverclock, cost: cost),
            new MultiMachineVariant { Name = name, PartsRatio = throughput, Default = isDefault });

    /// <summary>A build-cost entry.</summary>
    protected static CostEntry C(string part, int amount) => new() { Part = part, Amount = amount.ToString() };

    /// <summary>A family capacity mode (resource purity, belt mark, upload rate, …).</summary>
    protected static MultiMachineCapacity Cap(string name, string? partsRatio = null,
        int? color = null, bool isDefault = false, string? powerRatio = null)
        => new() { Name = name, PartsRatio = partsRatio, Color = color, Default = isDefault, PowerRatio = powerRatio };

    private static MachineDefinition Def(string name, string tier,
        string? power, string? minPower, string? basePower, string? basePowerBoost,
        string? fueledBasePowerBoost, string? overclockExp, int sloops,
        string? sloopMultiplier, string? sloopPowerExp, CostEntry[]? cost)
        => new()
        {
            Name = name,
            Tier = tier,
            AveragePower = power,
            MinPower = minPower,
            BasePower = basePower,
            BasePowerBoost = basePowerBoost,
            FueledBasePowerBoost = fueledBasePowerBoost,
            OverclockPowerExponent = overclockExp,
            MaxProductionShards = sloops,
            ProductionShardMultiplier = sloopMultiplier,
            ProductionShardPowerExponent = sloopPowerExp,
            Cost = cost?.ToList() ?? [],
        };

    private static MachineDefinition Def(string name, string tier, string? power = null,
        string? overclockExp = null, CostEntry[]? cost = null)
        => Def(name, tier, power, null, null, null, null, overclockExp, 0, null, null, cost);
}

/// <summary>
/// One machine, or a family of machines, plus the optional multi-machine family that
/// describes its marks/capacities. <see cref="MachineModule"/>'s builders produce these.
/// </summary>
public sealed record MachineGroup(
    IReadOnlyList<(int Sort, MachineDefinition Definition)> Machines,
    int FamilySort = -1,
    MultiMachineDefinition? Family = null)
{
    /// <summary>Attach a capacity-only family (purity / belt mark / upload rate + node
    /// defaults) to a single standalone machine; the family is keyed by the machine's name.</summary>
    public MachineGroup WithFamily(int familySort, bool showPpm = false, bool autoRound = true,
        string defaultMax = "", MultiMachineCapacity[]? capacities = null)
        => this with
        {
            FamilySort = familySort,
            Family = new MultiMachineDefinition
            {
                Name = Machines[0].Definition.Name,
                ShowPpm = showPpm,
                AutoRound = autoRound,
                DefaultMax = defaultMax,
                Capacities = (capacities ?? []).ToList(),
            },
        };
}

/// <summary>A machine mark within a multi-mark family: the machine it builds plus its variant entry.</summary>
public sealed record MarkSpec(int Sort, MachineDefinition Machine, MultiMachineVariant Variant);
