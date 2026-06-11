using Ficsit.Schematics.Core.GameData;

namespace Ficsit.Schematics.Core.Solver;

public static class SolverFactory
{
    public static readonly string[] Names = ["None", "Manual", "Basic", "Full"];

    public static ISolver Create(string name, GameDatabase data) => name switch
    {
        "None" => new NoneSolver(),
        "Manual" => new BasicSolver(data) { EnableDemandPull = false, LimitsAreExact = true },
        // v1: the Full LP solver is not implemented yet; Basic is the closest behavior.
        _ => new BasicSolver(data),
    };
}
