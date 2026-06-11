namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A recipe contributed to the game catalog; discovered via reflection by
/// <see cref="GameDataCatalog"/>. Amounts are per batch: negative = input,
/// positive = output, as fraction strings.
/// </summary>
public abstract class RecipeBase
{
    /// <summary>Position in the canonical game-data ordering (game/tier order in the chooser).</summary>
    public abstract int SortIndex { get; }

    public abstract string RecipeName { get; }

    public abstract string Machine { get; }

    /// <summary>Seconds per batch at 100% clock.</summary>
    public abstract string BatchTime { get; }

    /// <summary>Unlock tier, e.g. "0-2".</summary>
    public virtual string Tier => "";

    public virtual bool Alternate => false;

    public virtual bool Ficsmas => false;

    /// <summary>Recipe-level power override in MW (variable-power machines).</summary>
    public virtual string? AveragePower => null;

    public virtual string? MinPower => null;

    public virtual bool IgnoreInputMultiplier => false;

    public virtual string? SpaceElevatorMultiplier => null;

    public abstract IReadOnlyList<RecipePart> Parts { get; }

    public RecipeDefinition ToDefinition() => new()
    {
        Name = RecipeName,
        Machine = Machine,
        BatchTime = BatchTime,
        Tier = Tier,
        Alternate = Alternate,
        Ficsmas = Ficsmas,
        AveragePower = AveragePower,
        MinPower = MinPower,
        IgnoreInputMultiplier = IgnoreInputMultiplier,
        SpaceElevatorMultiplier = SpaceElevatorMultiplier,
        Parts = Parts.ToList(),
    };
}
