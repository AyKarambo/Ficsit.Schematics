using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>One derived part row (mirrors the catalog's <c>Part(...)</c> row).</summary>
public sealed record PartRow(string Name, Tier Tier, int SinkPoints, bool Fluid, bool ManuallyGathered);

/// <summary>One consumed/produced amount: negative = input, positive = output (per batch).</summary>
public sealed record AmountRow(string Part, Rational Amount);

/// <summary>One derived recipe row (mirrors the catalog's <c>Recipe(...)</c> row).</summary>
public sealed record RecipeRow(
    string Machine,
    string Name,
    Rational Batch,
    Tier Tier,
    IReadOnlyList<AmountRow> Parts,
    bool Alternate = false,
    bool Ficsmas = false,
    Rational? AveragePower = null,
    Rational? MinPower = null,
    bool IgnoreInputMultiplier = false,
    string? SpaceElevatorMultiplier = null)
{
    /// <summary>The milestone unlock tier, when one exists (null = the tier fixpoint decides).</summary>
    public Tier? UnlockTier { get; init; }
}

/// <summary>Export-derived stats for one machine (feeds the generated MachineStats table).</summary>
public sealed record MachineStatRow(
    string Name,
    Tier Tier,
    Rational? Power = null,
    Rational? MinPower = null,
    Rational? BasePower = null,
    Rational? BasePowerBoost = null,
    Rational? FueledBasePowerBoost = null,
    Rational? OverclockExp = null,
    int Sloops = 0,
    Rational? SloopMultiplier = null,
    Rational? SloopPowerExp = null,
    IReadOnlyList<(string Part, long Amount)>? Cost = null,
    Rational? Throughput = null);

/// <summary>The fully derived catalog: everything the emitters need.</summary>
public sealed class CatalogModel
{
    public required IReadOnlyList<PartRow> Parts { get; init; }

    /// <summary>Recipes grouped per machine module (module name → rows), rows in emit order.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<RecipeRow>> RecipesByModule { get; init; }

    public required IReadOnlyList<MachineStatRow> MachineStats { get; init; }

    public IEnumerable<RecipeRow> AllRecipes => RecipesByModule.Values.SelectMany(r => r);
}
