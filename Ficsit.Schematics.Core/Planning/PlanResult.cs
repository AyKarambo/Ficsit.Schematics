using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

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

    /// <summary>Total Somersloops the plan spends on production amplification (0 = none).</summary>
    public int SomersloopsUsed { get; set; }

    /// <summary>Total MW the planned generators produce (0 unless a power target was set).</summary>
    public Rational PowerGeneratedMW { get; set; } = Rational.Zero;

    /// <summary>
    /// Uniform clock factor applied to all recipes by the clock-fit post-pass (1 = no fit,
    /// today's default). When &gt; 1 the plan is overclocked; &lt; 1 it is underclocked.
    /// <see cref="PlannedRecipe.Machines"/> already reflects the divided count at this clock;
    /// <see cref="TotalMachines"/> and <see cref="TotalPowerMW"/> are also recomputed.
    /// </summary>
    public Rational ClockFactor { get; set; } = Rational.One;
}
