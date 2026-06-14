namespace Ficsit.Schematics.Core.Editing;

/// <summary>A reversible edit. Apply is called once when pushed, again on redo.</summary>
public sealed class EditCommand
{
    public required string Label { get; init; }
    public required Action Apply { get; init; }
    public required Action Revert { get; init; }

    /// <summary>Commands with the same non-null key merge into one undo step (drag coalescing).</summary>
    public string? CoalesceKey { get; init; }

    /// <summary>Whether applying this command can change solver results. False for pure
    /// geometry edits (node moves), which skip the re-solve so dragging stays smooth.</summary>
    public bool AffectsSolve { get; init; } = true;
}
