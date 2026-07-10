using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>One recipe in the solved plan, how many machines run it, and how many
/// Somersloops each machine carries (0 = unamplified).</summary>
public sealed record PlannedRecipe(string Recipe, Rational Machines, int Somersloops = 0);
