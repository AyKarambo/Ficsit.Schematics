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

    /// <summary>Include alternate recipes (default true — they make the magic happen).</summary>
    public bool UseAlternateRecipes { get; set; } = true;
}
