namespace Ficsit.Schematics.ViewModels;

/// <summary>One label/value line in the summary panel, optionally with a part icon.</summary>
public sealed class SummaryRow
{
    public ImageSource? Icon { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
}
