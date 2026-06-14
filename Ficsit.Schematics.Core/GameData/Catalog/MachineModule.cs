using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of machines for one category, authored as <see cref="MachineGroup"/>s so a
/// multi-machine family lives next to the machine(s) it describes. Discovered via
/// reflection by <see cref="GameDataCatalog"/>; each entry carries its canonical sort key.
/// Quantities are written as int literals or <see cref="R"/> for fractions.
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
    protected static readonly Rational StandardOverclock = Rational.Parse("1321929/1000000");

    /// <summary>An exact fraction from its canonical string (e.g. "1/2"); whole numbers use int literals.</summary>
    protected static Rational R(string text) => Rational.Parse(text);

    /// <summary>Resource-node purity capacities (Impure ×½, Normal ×1, Pure ×2), shared by extractors.</summary>
    protected static MultiMachineCapacity[] Purities =>
    [
        Cap(Purity.Impure, parts: R("1/2")),
        Cap(Purity.Normal),
        Cap(Purity.Pure, parts: 2),
    ];

    // ------------------------------------------------------------------ builders

    /// <summary>A standalone machine. Pass <c>overclockExp: StandardOverclock</c> for the usual
    /// clock^1.32 curve, <c>overclockExp: 1</c> for generators (linear), or omit it for none.</summary>
    protected static MachineGroup Machine(int sort, string name, Tier tier,
        Rational? power = null, Rational? minPower = null, Rational? basePower = null,
        Rational? basePowerBoost = null, Rational? fueledBasePowerBoost = null,
        Rational? overclockExp = null,
        int sloops = 0, Rational? sloopMultiplier = null, Rational? sloopPowerExp = null,
        CostEntry[]? cost = null)
        => new([(sort, Def(name, tier, power, minPower, basePower, basePowerBoost,
            fueledBasePowerBoost, overclockExp, sloops, sloopMultiplier, sloopPowerExp, cost))]);

    /// <summary>A multi-mark family (e.g. the Miner): each mark is a machine named
    /// "{name} Mk.{n}", and recipes target the family <paramref name="name"/>.</summary>
    protected static MachineGroup Family(int familySort, string name,
        bool showPpm = false, bool autoRound = true, string defaultMax = "",
        MarkSpec[]? marks = null, MultiMachineCapacity[]? capacities = null)
    {
        marks ??= [];
        var machines = new List<(int, MachineDefinition)>();
        var variants = new List<MultiMachineVariant>();
        foreach (var mark in marks)
        {
            var markName = $"{name} Mk.{(int)mark.Mark}";
            machines.Add((mark.Sort, Def(markName, mark.Tier, mark.Power, overclockExp: StandardOverclock, cost: mark.Cost)));
            variants.Add(new MultiMachineVariant
            {
                Name = markName,
                PartsRatio = mark.Throughput,
                Default = mark.IsDefault,
            });
        }
        var family = new MultiMachineDefinition
        {
            Name = name,
            ShowPpm = showPpm,
            AutoRound = autoRound,
            DefaultMax = defaultMax,
            Machines = variants,
            Capacities = (capacities ?? []).ToList(),
        };
        return new MachineGroup(machines, familySort, family);
    }

    /// <summary>One mark of a multi-mark family: its <see cref="Mark"/> plus per-mark stats.
    /// The family derives the name ("{family} Mk.{n}").</summary>
    protected static MarkSpec Variant(int sort, Mark mark, Tier tier, Rational throughput,
        bool isDefault = false, Rational? power = null, CostEntry[]? cost = null)
        => new(sort, mark, tier, throughput, isDefault, power, cost);

    /// <summary>A belt-mark capacity ("Mk.{n} Belt"), e.g. for the AWESOME Sink.</summary>
    protected static MultiMachineCapacity Belt(Mark mark, Rational throughput, int? color = null, bool isDefault = false)
        => new() { Name = $"Mk.{(int)mark} Belt", PartsRatio = throughput, Color = color, Default = isDefault };

    /// <summary>A build-cost entry.</summary>
    protected static CostEntry C(string part, int amount) => new() { Part = part, Amount = amount };

    /// <summary>A purity capacity (Impure ×½, Normal ×1, Pure ×2): <paramref name="parts"/> scales the
    /// extraction rate, <paramref name="power"/> the Geothermal's output. Accent color is by purity.</summary>
    protected static MultiMachineCapacity Cap(Purity purity, Rational? parts = null, Rational? power = null)
        => new()
        {
            Name = purity.ToString(),
            PartsRatio = parts,
            PowerRatio = power,
            Default = purity == Purity.Normal,
            Color = purity switch { Purity.Impure => 13775920, Purity.Pure => 8433977, _ => (int?)null },
        };

    /// <summary>An upload-rate capacity ("{n}/min"), e.g. for the Dimensional Depot Uploader.</summary>
    protected static MultiMachineCapacity Upload(int perMinute, int? color = null, bool isDefault = false)
        => new() { Name = $"{perMinute}/min", PartsRatio = perMinute, Color = color, Default = isDefault };

    private static MachineDefinition Def(string name, Tier tier,
        Rational? power, Rational? minPower, Rational? basePower, Rational? basePowerBoost,
        Rational? fueledBasePowerBoost, Rational? overclockExp, int sloops,
        Rational? sloopMultiplier, Rational? sloopPowerExp, CostEntry[]? cost)
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

    private static MachineDefinition Def(string name, Tier tier, Rational? power = null,
        Rational? overclockExp = null, CostEntry[]? cost = null)
        => Def(name, tier, power, null, null, null, null, overclockExp, 0, null, null, cost);
}
