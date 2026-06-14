namespace Ficsit.Schematics.Core.GameData;

/// <summary>
/// A game-progression tier as a phase-milestone pair (e.g. "0-0", "8-3"). Stored
/// typed; parses from and renders as "phase-milestone", and orders by phase then
/// milestone. Catalog code may write a plain string literal — it converts implicitly.
/// </summary>
public readonly record struct Tier(int Phase, int Milestone) : IComparable<Tier>
{
    public static Tier Parse(string text)
    {
        var dash = text.IndexOf('-');
        return dash < 0
            ? new Tier(int.Parse(text), 0)
            : new Tier(int.Parse(text[..dash]), int.Parse(text[(dash + 1)..]));
    }

    public static implicit operator Tier(string text) => Parse(text);

    public int CompareTo(Tier other)
    {
        var byPhase = Phase.CompareTo(other.Phase);
        return byPhase != 0 ? byPhase : Milestone.CompareTo(other.Milestone);
    }

    public override string ToString() => $"{Phase}-{Milestone}";
}
