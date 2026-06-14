using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// One recipe row. Build the part list from <see cref="RecipeModule.In"/> /
/// <see cref="RecipeModule.Out"/>. Optional columns (alternates, per-recipe power,
/// special multipliers) are named arguments so the common case stays a short line.
/// </summary>
public sealed record Recipe(
    int Sort,
    string Name,
    string Batch,
    Tier Tier,
    IReadOnlyList<RecipePart> Parts,
    bool Alternate = false,
    bool Ficsmas = false,
    string? AveragePower = null,
    string? MinPower = null,
    bool IgnoreInputMultiplier = false,
    string? SpaceElevatorMultiplier = null)
{
    public RecipeDefinition ToDefinition(string machine) => new()
    {
        Name = Name,
        Machine = machine,
        BatchTime = Rational.Parse(Batch),
        Tier = Tier,
        Alternate = Alternate,
        Ficsmas = Ficsmas,
        AveragePower = AveragePower is null ? null : Rational.Parse(AveragePower),
        MinPower = MinPower is null ? null : Rational.Parse(MinPower),
        IgnoreInputMultiplier = IgnoreInputMultiplier,
        SpaceElevatorMultiplier = SpaceElevatorMultiplier,
        Parts = Parts.ToList(),
    };
}
