namespace Ficsit.Schematics.Core.Planning;

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
