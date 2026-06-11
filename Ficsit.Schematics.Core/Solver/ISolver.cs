using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Solver;

public interface ISolver
{
    string Name { get; }
    SolveResult Solve(FactoryDocument document);
}
