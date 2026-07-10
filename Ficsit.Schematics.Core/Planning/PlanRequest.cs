using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

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

    /// <summary>
    /// Per-resource cost-weight multipliers for the <see cref="PlanBias.Resources"/>
    /// objective. 1 (or absent) leaves the scarcity default untouched; &lt;1 makes a
    /// raw cheaper so the plan reaches for it, &gt;1 makes it dearer so it is spared.
    /// The Auto-Plan "resource preference" budget produces these — the planner just
    /// multiplies the baseline weight, so it learns no new concept.
    /// </summary>
    public Dictionary<string, Rational> WeightMultipliers { get; } = [];

    /// <summary>
    /// Somersloops the plan may spend on production amplification (0 = off, today's
    /// behavior). The planner greedily picks which sloopable recipes to amplify, then
    /// re-solves so the whole chain rebalances around the boosted output. Ignored under
    /// <see cref="PlanBias.Power"/> (slooping costs power).
    /// </summary>
    public int SomersloopBudget { get; set; }

    /// <summary>
    /// Post-solve uniform overclock fit mode (default <see cref="FitMode.None"/> —
    /// today's behavior unchanged). When set to <see cref="FitMode.Machines"/> or
    /// <see cref="FitMode.Power"/>, a single clock factor is computed after the solve
    /// and applied uniformly to all recipes so the total fits the given
    /// <see cref="FitBudget"/>. The factor is clamped to (0.01, 2.5].
    /// </summary>
    public FitMode FitMode { get; set; } = FitMode.None;

    /// <summary>
    /// The target budget for <see cref="FitMode"/>: machine count or MW (depending on
    /// <see cref="FitMode"/>). Ignored when <see cref="FitMode"/> is
    /// <see cref="FitMode.None"/> or when ≤ 0.
    /// </summary>
    public Rational FitBudget { get; set; }
}
