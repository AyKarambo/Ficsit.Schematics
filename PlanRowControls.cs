namespace Ficsit.Schematics;

/// <summary>The controls making up one Auto-Plan row (a target or a provision).</summary>
public sealed class PlanRowControls
{
    public required Grid Row { get; init; }
    public required Button PartButton { get; init; }
    public required Entry Rate { get; init; }

    /// <summary>Provision rows only: the "this is all there is" toggle.</summary>
    public Button? LockButton { get; init; }

    /// <summary>Raw (English) part name chosen via the part picker.</summary>
    public string? Part { get; set; }

    /// <summary>Provision rows: true = supply caps the output, never build more.</summary>
    public bool Exclusive { get; set; }
}
