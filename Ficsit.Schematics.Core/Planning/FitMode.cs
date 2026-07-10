namespace Ficsit.Schematics.Core.Planning;

/// <summary>Post-solve uniform-clock fit mode: scale the solved plan into a
/// machine-count or power budget by choosing a single overclock factor.</summary>
public enum FitMode
{
    /// <summary>No fit — plan is returned at 100% clock (default behavior).</summary>
    None,

    /// <summary>Fit into a machine-count budget: overclock until total machines ≤ budget.</summary>
    Machines,

    /// <summary>Fit into a power budget: overclock until total power ≈ budget.</summary>
    Power,
}
