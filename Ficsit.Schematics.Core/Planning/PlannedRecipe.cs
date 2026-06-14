using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>One recipe in the solved plan and how many machines run it.</summary>
public sealed record PlannedRecipe(string Recipe, Rational Machines);
