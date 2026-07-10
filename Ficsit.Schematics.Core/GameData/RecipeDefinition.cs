using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class RecipeDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;

    /// <summary>Seconds per batch at 100% clock.</summary>
    public Rational BatchTime { get; set; } = 60;

    public bool Alternate { get; set; }

    public bool Ficsmas { get; set; }

    public Tier Tier { get; set; }

    /// <summary>Recipe-level power override in MW (variable-power machines).</summary>
    public Rational? AveragePower { get; set; }
    public Rational? MinPower { get; set; }

    /// <summary>True for recipes exempt from the global input-cost multiplier.</summary>
    public bool IgnoreInputMultiplier { get; set; }

    /// <summary>Per-recipe Space Elevator cost multiplier flag ("True" when set).</summary>
    public string? SpaceElevatorMultiplier { get; set; }

    /// <summary>Negative amounts are inputs, positive are outputs, per batch.</summary>
    public List<RecipePart> Parts { get; set; } = [];

    public Rational BatchTimeValue => BatchTime;

    public IEnumerable<RecipePart> Inputs => Parts.Where(p => p.AmountValue.IsNegative);

    public IEnumerable<RecipePart> Outputs => Parts.Where(p => p.AmountValue.IsPositive);

    /// <summary>Parts per minute for one machine at 100% clock (positive for outputs, negative for inputs).</summary>
    public Rational RatePerMinute(string part)
    {
        var entry = Parts.FirstOrDefault(p => p.Part == part);
        return entry is null ? Rational.Zero : entry.AmountValue * 60 / BatchTimeValue;
    }
}
