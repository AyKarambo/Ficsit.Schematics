using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

/// <summary>
/// Pre-computed per-node solve characteristics: effective per-machine rates
/// (clock, variant, capacity, somersloop and global multipliers applied),
/// limit-as-count conversion, and behavioral category.
/// </summary>
internal sealed class NodeProfile
{
    public required FactoryNode Node { get; init; }

    /// <summary>ppm consumed per machine, by part. Empty for pass-through/sink-any nodes.</summary>
    public Dictionary<string, Rational> InRates { get; } = [];

    /// <summary>ppm produced per machine, by part.</summary>
    public Dictionary<string, Rational> OutRates { get; } = [];

    /// <summary>Per-"machine" ppm unit for ppm-display and ppm-limit conversion (primary output or belt/upload rate).</summary>
    public Rational PpmUnit { get; set; } = Rational.One;

    public bool IsPpmDisplay { get; set; }

    /// <summary>Entered limit converted to a machine count; null when no limit.</summary>
    public Rational? LimitCount { get; set; }

    /// <summary>Pass-through of any part: splurgers, priority nodes, Input=Output storage.</summary>
    public bool IsPassThrough { get; set; }

    /// <summary>Accepts any part with unbounded demand: sinks, Empty/PartiallyFull storage.</summary>
    public bool IsOpenSink { get; set; }

    /// <summary>Provides any connected part with unbounded supply: Full/PartiallyFull storage.</summary>
    public bool IsOpenSource { get; set; }

    /// <summary>Average MW per machine (signed; negative consumes).</summary>
    public Rational PowerPerMachine { get; set; } = Rational.Zero;

    // Inputs for re-evaluating power at a different clock (Auto-Round).
    private MachineDefinition? _machine;
    private Rational _powerRatio = Rational.One;
    private Rational _powerMultiplier = Rational.Zero; // zero = unset

    /// <summary>Sink points per minute per ppm-unit (AWESOME Sink).</summary>
    public bool IsAwesomeSink { get; set; }

    public static NodeProfile Build(FactoryNode node, GameDatabase data, FactoryDocument document)
    {
        var profile = new NodeProfile { Node = node };

        switch (node.Kind)
        {
            case NodeKind.Recipe:
                BuildRecipeProfile(profile, node, data, document);
                break;

            case NodeKind.Splurger:
            case NodeKind.PrioritySplitter:
            case NodeKind.PriorityMerger:
            case NodeKind.PrioritySplurger:
                profile.IsPassThrough = true;
                profile.LimitCount = node.LimitValue;
                profile.IsPpmDisplay = true;
                break;

            case NodeKind.StorageContainer:
                profile.IsPpmDisplay = true;
                profile.LimitCount = node.LimitValue;
                switch (node.StorageMode)
                {
                    case StorageMode.Full: profile.IsOpenSource = true; break;
                    case StorageMode.Empty: profile.IsOpenSink = true; break;
                    case StorageMode.InputEqualsOutput: profile.IsPassThrough = true; break;
                    default: profile.IsOpenSource = profile.IsOpenSink = true; break;
                }
                break;

            case NodeKind.AwesomeSink:
                profile.IsOpenSink = true;
                profile.IsAwesomeSink = true;
                profile.IsPpmDisplay = true;
                profile.PpmUnit = CapacityRatio(node, data, "AWESOME Sink");
                profile.LimitCount = node.LimitValue is { } sinkLimit ? sinkLimit / profile.PpmUnit : null;
                break;

            case NodeKind.DimensionalDepot:
                profile.IsOpenSink = true;
                profile.IsPpmDisplay = true;
                profile.PpmUnit = CapacityRatio(node, data, "Dimensional Depot Uploader");
                profile.LimitCount = node.LimitValue is { } depotLimit ? depotLimit / profile.PpmUnit : null;
                break;

            case NodeKind.Generator:
                BuildGeneratorProfile(profile, node, data, document);
                break;

            case NodeKind.Outpost:
            case NodeKind.Blueprint:
                // Pure grouping ("bracket"): not a machine, excluded from the solve. Members
                // (Parent == this outpost) are normal nodes solved as part of the flat graph.
                break;
        }

        return profile;
    }

    /// <summary>
    /// A unified generator burns whichever fuel is wired in: it runs that fuel's recipe (so a
    /// single connected fuel behaves exactly like the per-fuel recipe — fuel/water in, power out,
    /// waste out). With nothing wired in it shows the machine's rated power for the entered count.
    /// </summary>
    private static void BuildGeneratorProfile(NodeProfile profile, FactoryNode node, GameDatabase data, FactoryDocument document)
    {
        if (SelectGeneratorRecipe(node, data, document) is { } recipe)
        {
            ApplyRecipe(profile, node, data, document, recipe);
            return;
        }

        profile.IsPpmDisplay = false;
        profile.LimitCount = node.LimitValue;
        if (data.MachinesByName.TryGetValue(node.Name, out var machine))
        {
            profile._machine = machine;
            profile._powerMultiplier = GameDatabase.ParseOrZero(document.PowerMultiplier);
            profile.PowerPerMachine = profile.PowerPerMachineAt(node.ClockSpeed);
        }
    }

    /// <summary>The recipe a generator runs: the one whose fuel (its first input) is wired in.
    /// When several fuels are connected, the first matching recipe in catalog order wins —
    /// not the order in which the fuels were wired. When no fuel is wired but another input
    /// is (only water reaches a coal generator whose coal is supplied off-canvas), the first
    /// catalog-order recipe with any wired input runs, so the wired input keeps flowing and
    /// the missing fuel surfaces as unmade demand instead of a dead connection.</summary>
    private static RecipeDefinition? SelectGeneratorRecipe(FactoryNode node, GameDatabase data, FactoryDocument document)
    {
        var connected = document.Root.Connections
            .Where(c => c.To == node).Select(c => c.Part).ToHashSet();
        if (connected.Count == 0) return null;
        var candidates = data.Document.Recipes.Where(r => r.Machine == node.Name).ToList();
        return candidates.FirstOrDefault(
                r => r.Inputs.FirstOrDefault() is { } fuel && connected.Contains(fuel.Part))
            ?? candidates.FirstOrDefault(r => r.Inputs.Any(i => connected.Contains(i.Part)));
    }

    private static void BuildRecipeProfile(NodeProfile profile, FactoryNode node, GameDatabase data, FactoryDocument document)
    {
        if (!data.RecipesByName.TryGetValue(node.Name, out var recipe))
        {
            profile.IsPpmDisplay = true;
            profile.LimitCount = node.LimitValue;
            return;
        }
        ApplyRecipe(profile, node, data, document, recipe);
    }

    private static void ApplyRecipe(NodeProfile profile, FactoryNode node, GameDatabase data, FactoryDocument document, RecipeDefinition recipe)
    {
        var family = data.MultiMachinesByName.TryGetValue(recipe.Machine, out var byFamily)
            ? byFamily
            : data.MultiMachineFor(recipe.Machine);

        // Variant ("Miner Mk.2") and capacity ("Pure", "Mk.3 Belt") throughput factors.
        var variantRatio = Rational.One;
        string machineName = recipe.Machine;
        if (family is { Machines.Count: > 0 })
        {
            var variant = family.Machines.FirstOrDefault(v => v.Name == node.MachineVariant)
                ?? family.Machines.FirstOrDefault(v => v.Default)
                ?? family.Machines[0];
            variantRatio = variant.PartsRatioValue;
            machineName = variant.Name;
        }

        var capacityRatio = Rational.One;
        var powerRatio = Rational.One;
        if (family is { Capacities.Count: > 0 })
        {
            var capacity = family.Capacities.FirstOrDefault(c => c.Name == node.Capacity)
                ?? family.Capacities.FirstOrDefault(c => c.Default)
                ?? family.Capacities[0];
            capacityRatio = capacity.PartsRatioValue;
            powerRatio = capacity.PowerRatioValue;
        }

        data.MachinesByName.TryGetValue(machineName, out var machine);

        var clock = node.ClockSpeed;
        var sloopBoost = machine is not null
            ? Amplification.OutputFactor(machine, node.Somersloops)
            : Rational.One;

        var inputMultiplier = GameDatabase.ParseOrZero(
            recipe.Machine == "Space Elevator" ? document.SpaceElevatorMultiplier : document.InputMultiplier);
        if (inputMultiplier.IsZero) inputMultiplier = Rational.One;

        var throughput = variantRatio * capacityRatio * clock;
        foreach (var part in recipe.Parts)
        {
            var rate = part.AmountValue * 60 / recipe.BatchTimeValue * throughput;
            if (rate.IsNegative)
                profile.InRates[part.Part] = -rate * inputMultiplier;
            else if (rate.IsPositive)
                profile.OutRates[part.Part] = rate * sloopBoost;
        }

        profile.PpmUnit = profile.OutRates.Count > 0
            ? profile.OutRates.Values.First()
            : profile.InRates.Count > 0 ? profile.InRates.Values.First() : Rational.One;

        profile.IsPpmDisplay = node.ShowPpm ?? family?.ShowPpm ?? false;

        if (node.LimitValue is { } limit)
            profile.LimitCount = profile.IsPpmDisplay && !profile.PpmUnit.IsZero ? limit / profile.PpmUnit : limit;

        // A map-snapped extractor models exactly one physical machine fixed to its resource
        // node: its output is mark × purity × clock at full rate. Pin it to a single machine so
        // the ppm scales with the overclock (issue #7) — the auto-applied ppm default Max would
        // otherwise cap output at the 100% value and silently cancel any overclock or purity.
        if (node.ResourceNodeId is not null)
            profile.LimitCount = Rational.One;

        if (machine is not null)
        {
            profile._machine = machine;
            profile._powerRatio = powerRatio;
            profile._powerMultiplier = GameDatabase.ParseOrZero(document.PowerMultiplier);
            profile.PowerPerMachine = profile.PowerPerMachineAt(clock);
        }
    }

    /// <summary>
    /// Average MW per machine at the given clock — same formula that produced
    /// <see cref="PowerPerMachine"/> (which was evaluated at the entered clock).
    /// Auto-Round re-evaluates it at the rebalanced effective clock.
    /// </summary>
    public Rational PowerPerMachineAt(Rational clock)
    {
        if (_machine is null) return Rational.Zero;
        var power = _machine.AveragePowerValue;
        if (power.IsZero) return Rational.Zero;

        var clockFactor = clock == Rational.One
            ? 1.0
            : clock.Pow(_machine.OverclockPowerExponentValue);
        var sloopPowerFactor = Amplification.PowerFactor(_machine, Node.Somersloops);
        var approx = power.ToDouble() * clockFactor * (power.IsNegative ? sloopPowerFactor : 1.0);
        if (!_powerMultiplier.IsZero && power.IsNegative) approx *= _powerMultiplier.ToDouble();
        return FromDouble(approx) * _powerRatio;
    }

    private static Rational CapacityRatio(FactoryNode node, GameDatabase data, string familyName)
    {
        if (!data.MultiMachinesByName.TryGetValue(familyName, out var family) || family.Capacities.Count == 0)
            return Rational.One;
        var capacity = family.Capacities.FirstOrDefault(c => c.Name == node.Capacity)
            ?? family.Capacities.FirstOrDefault(c => c.Default)
            ?? family.Capacities[0];
        return capacity.PartsRatioValue;
    }

    private static Rational FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return Rational.Zero;
        var scaled = (long)Math.Round(value * 1_000_000.0);
        return new Rational(scaled, 1_000_000);
    }
}
