using Ficsit.Schematics.Core.GameData;

namespace Ficsit.Schematics.Core.Solver;

public static class SolverFactory
{
    public static readonly string[] Names = ["None", "Manual", "Basic", "Full"];

    public static ISolver Create(string name, GameDatabase data) => name switch
    {
        "None" => new NoneSolver(),
        "Manual" => new BasicSolver(data) { EnableDemandPull = false, LimitsAreExact = true },
        // "Full" adds priority splitter/merger routing on top of the Basic propagation
        // (see docs/specs/full-solver.md). A global-LP refinement is a future step.
        "Full" => new BasicSolver(data) { HonorPriorities = true },
        _ => new BasicSolver(data),
    };
}
