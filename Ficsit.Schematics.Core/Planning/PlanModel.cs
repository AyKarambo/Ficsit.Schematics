using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

// The auto-planner's public model: what you ask for (PlanRequest) and what comes
// back (PlanResult), plus the small value types they are built from.

/// <summary>What the auto-planner optimizes for.</summary>
public enum PlanBias
{
    /// <summary>Use as few raw resources as possible, weighted by scarcity.</summary>
    Resources,

    /// <summary>Use as little power as possible.</summary>
    Power,

    /// <summary>Use as few machines as possible.</summary>
    Machines,
}

/// <summary>How the auto-planner deals with byproducts.</summary>
public enum ByproductMode
{
    /// <summary>
    /// Recycle byproducts back into the chain wherever possible (zero waste);
    /// sinking is a heavily penalized last resort.
    /// </summary>
    Eliminate,

    /// <summary>Byproducts may go straight into the AWESOME Sink.</summary>
    AllowSink,
}

public enum PlanStatus
{
    Optimal,

    /// <summary>No factory can satisfy the request (e.g. a needed raw is banned).</summary>
    Infeasible,

    /// <summary>Maximize mode without effective input caps.</summary>
    Unbounded,
}

/// <summary>One requested output: a part and, in target mode, its rate per minute.
/// In maximize mode the rate acts as the bundle ratio (e.g. 1 of each phase part).</summary>
public sealed record PlanTarget(string Part, Rational Rate);

/// <summary>
/// An input the user is willing to provide, capped at a rate per minute.
/// <para>
/// <see cref="Exclusive"/> = true means "this is all there is": the planner
/// must not build extra production for the part, and in target mode the whole
/// output scales down if the supply bottlenecks the chain. False means the
/// supply is a free head start and the planner produces whatever more it needs.
/// </para>
/// </summary>
public sealed record PlanProvision(string Part, Rational MaxPerMinute, bool Exclusive = false);

/// <summary>One recipe in the solved plan and how many machines run it.</summary>
public sealed record PlannedRecipe(string Recipe, Rational Machines);

/// <summary>Everything the auto-planner needs to synthesize a factory.</summary>
public sealed class PlanRequest
{
    /// <summary>Desired outputs. Target mode: exact rates. Maximize mode: bundle ratios.</summary>
    public List<PlanTarget> Targets { get; } = [];

    /// <summary>
    /// True: maximize the common multiple of the target bundle from the provided
    /// inputs. False: meet the target rates exactly at minimum cost.
    /// </summary>
    public bool MaximizeFromProvisions { get; set; }

    /// <summary>Inputs the user can supply, with caps (raw or intermediate parts).</summary>
    public List<PlanProvision> Provisions { get; } = [];

    /// <summary>Raw resources the plan must not consume (e.g. "Crude Oil").</summary>
    public HashSet<string> BannedResources { get; } = [];

    public PlanBias Bias { get; set; } = PlanBias.Resources;

    public ByproductMode Byproducts { get; set; } = ByproductMode.Eliminate;

    /// <summary>
    /// Recipes (by name) the planner must not use. The UI's per-recipe enable
    /// list and the "allow ore conversion" toggle both map onto this set when the
    /// request is built — the planner itself learns no new concepts.
    /// </summary>
    public HashSet<string> DisabledRecipes { get; } = [];
}

/// <summary>The solved factory plan.</summary>
public sealed class PlanResult
{
    public PlanStatus Status { get; set; }

    /// <summary>Recipes to build, with exact machine counts.</summary>
    public List<PlannedRecipe> Recipes { get; } = [];

    /// <summary>External inputs the factory consumes, per minute.</summary>
    public Dictionary<string, Rational> Supplies { get; } = [];

    /// <summary>Byproducts sent to the AWESOME Sink, per minute.</summary>
    public Dictionary<string, Rational> Sinks { get; } = [];

    /// <summary>Achieved output rates per target part.</summary>
    public Dictionary<string, Rational> Outputs { get; } = [];

    public Rational TotalMachines { get; set; } = Rational.Zero;

    /// <summary>Average MW consumed by the planned machines.</summary>
    public Rational TotalPowerMW { get; set; } = Rational.Zero;

    /// <summary>Maximize mode: the achieved bundle multiplier.</summary>
    public Rational BundleMultiplier { get; set; } = Rational.Zero;

    /// <summary>
    /// Fraction of the requested target rates actually achieved (1 unless an
    /// exclusive provision bottlenecks the chain in target mode).
    /// </summary>
    public Rational AchievedFraction { get; set; } = Rational.One;

    /// <summary>Provided inputs that are fully used up and cap the output.</summary>
    public List<string> Bottlenecks { get; } = [];
}
