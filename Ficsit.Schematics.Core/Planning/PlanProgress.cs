namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// A coarse progress signal from <see cref="FactoryPlanner.Plan"/> so the UI can
/// show which phase of the solve is running. An LP has no meaningful percentage,
/// so this is phase text only — the host pairs it with an indeterminate spinner.
/// </summary>
public readonly record struct PlanProgress(string Phase);
