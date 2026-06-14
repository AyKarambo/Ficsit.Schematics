namespace Ficsit.Schematics.Core.Planning;

public enum PlanStatus
{
    Optimal,

    /// <summary>No factory can satisfy the request (e.g. a needed raw is banned).</summary>
    Infeasible,

    /// <summary>Maximize mode without effective input caps.</summary>
    Unbounded,
}
