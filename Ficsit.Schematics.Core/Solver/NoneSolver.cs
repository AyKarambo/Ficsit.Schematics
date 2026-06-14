using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Solver;

/// <summary>The "None" calculator: does nothing; every value reads zero.</summary>
public sealed class NoneSolver : ISolver
{
    public string Name => "None";
    public SolveResult Solve(FactoryDocument document) => new();
}
