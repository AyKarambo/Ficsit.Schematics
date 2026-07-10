using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Solver;

/// <summary>A flow calculator: turns the factory graph into per-node/per-port results.</summary>
public interface ISolver
{
    string Name { get; }
    SolveResult Solve(FactoryDocument document);
}
