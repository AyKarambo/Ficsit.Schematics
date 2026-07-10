using Ficsit.Schematics.Core.GameData.Catalog.Machines;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of machines for one category, authored as <see cref="MachineGroup"/>s so a
/// multi-machine family lives next to the machine(s) it describes. Discovered via
/// reflection by <see cref="GameDataCatalog"/>; each entry carries its canonical sort key.
/// The export-derived numbers (tier, power, somersloops, build cost) come from the
/// generated <c>MachineStats</c> table by machine name — these files only declare the
/// hand-authored structure: grouping, families, marks and capacities.
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

    /// <summary>A standalone machine; tier, power, somersloops and cost come from <c>MachineStats</c>.</summary>
    protected static MachineGroup Machine(int sort, string name)
        => new([(sort, Def(name, MachineStats.For(name)))]);

    /// <summary>A multi-mark family (e.g. the Miner): each mark is a machine named
    /// "{name} Mk.{n}" with stats from <c>MachineStats</c>, and recipes target the
    /// family <paramref name="name"/>.</summary>
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
            var stat = MachineStats.For(markName);
            machines.Add((mark.Sort, Def(markName, stat)));
            variants.Add(new MultiMachineVariant
            {
                Name = markName,
                PartsRatio = stat.Throughput,
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

    /// <summary>One mark of a multi-mark family; its stats resolve via "{family} Mk.{n}".</summary>
    protected static MarkSpec Variant(int sort, Mark mark, bool isDefault = false)
        => new(sort, mark, isDefault);

    /// <summary>A belt-mark capacity ("Mk.{n} Belt"), e.g. for the AWESOME Sink.</summary>
    protected static MultiMachineCapacity Belt(Mark mark, Rational throughput, int? color = null, bool isDefault = false)
        => new() { Name = $"Mk.{(int)mark} Belt", PartsRatio = throughput, Color = color, Default = isDefault };

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

    private static MachineDefinition Def(string name, MachineStat stat)
        => new()
        {
            Name = name,
            Tier = stat.Tier,
            AveragePower = stat.Power,
            MinPower = stat.MinPower,
            BasePower = stat.BasePower,
            BasePowerBoost = stat.BasePowerBoost,
            FueledBasePowerBoost = stat.FueledBasePowerBoost,
            OverclockPowerExponent = stat.OverclockExp,
            MaxProductionShards = stat.Sloops,
            ProductionShardMultiplier = stat.SloopMultiplier,
            ProductionShardPowerExponent = stat.SloopPowerExp,
            Cost = stat.Cost?.ToList() ?? [],
        };
}
