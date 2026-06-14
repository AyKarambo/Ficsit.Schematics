namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of recipes that all run on one <see cref="Machine"/>, authored as a
/// readable table (one <see cref="Recipe"/> per row). Every concrete module is
/// discovered via reflection by <see cref="GameDataCatalog"/>; each row's
/// <see cref="Recipe.Sort"/> places it in the canonical game-data order.
/// </summary>
public abstract class RecipeModule
{
    /// <summary>The machine every recipe in this module runs on (e.g. "Smelter").</summary>
    protected abstract string Machine { get; }

    /// <summary>The recipes, as a table; order within the table is cosmetic (sort wins).</summary>
    protected abstract IReadOnlyList<Recipe> Recipes { get; }

    /// <summary>Each recipe paired with its canonical sort key, machine stamped on.</summary>
    public IEnumerable<(int Sort, RecipeDefinition Definition)> Build()
        => Recipes.Select(r => (r.Sort, r.ToDefinition(Machine)));

    /// <summary>A consumed input, stored as a negative per-batch amount.</summary>
    protected static RecipePart In(string part, int amount) => new() { Part = part, Amount = (-amount).ToString() };

    /// <summary>A consumed input with a fractional amount (e.g. "5/2").</summary>
    protected static RecipePart In(string part, string amount) => new() { Part = part, Amount = "-" + amount };

    /// <summary>A produced output, stored as a positive per-batch amount.</summary>
    protected static RecipePart Out(string part, int amount) => new() { Part = part, Amount = amount.ToString() };

    /// <summary>A produced output with a fractional amount (e.g. "3/2").</summary>
    protected static RecipePart Out(string part, string amount) => new() { Part = part, Amount = amount };
}

/// <summary>
/// One recipe row. Build the part list from <see cref="RecipeModule.In"/> /
/// <see cref="RecipeModule.Out"/>. Optional columns (alternates, per-recipe power,
/// special multipliers) are named arguments so the common case stays a short line.
/// </summary>
public sealed record Recipe(
    int Sort,
    string Name,
    string Batch,
    string Tier,
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
        BatchTime = Batch,
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
