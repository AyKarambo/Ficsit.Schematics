using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Solver;

/// <summary>A flow calculator: turns the factory graph into per-node/per-port results.</summary>
public interface ISolver
{
    string Name { get; }
    SolveResult Solve(FactoryDocument document);
}

/// <summary>The "None" calculator: does nothing; every value reads zero.</summary>
public sealed class NoneSolver : ISolver
{
    public string Name => "None";
    public SolveResult Solve(FactoryDocument document) => new();
}

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
