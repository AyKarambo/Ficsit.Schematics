namespace Ficsit.Schematics.Canvas;

/// <summary>
/// Canvas palette. Our own design language: graphite surfaces, FICSIT-orange
/// accent, soft supply/surplus chips — not a copy of the reference styling.
/// </summary>
public sealed class CanvasTheme
{
    public required Color Background { get; init; }
    public required Color GridDot { get; init; }
    public required Color CardBackground { get; init; }
    public required Color CardBorder { get; init; }
    public required Color SelectedBorder { get; init; }
    public required Color ValueRowBackground { get; init; }
    public required Color LimitBoxBackground { get; init; }
    public required Color Text { get; init; }
    public required Color MutedText { get; init; }
    public required Color Wire { get; init; }
    public required Color PortChip { get; init; }
    public required Color UnmadeFlag { get; init; }
    public required Color UnusedFlag { get; init; }
    public required Color InvalidText { get; init; }
    public required Color NonMatchingText { get; init; }
    public required Color RubberBand { get; init; }
    public required Color TooltipBackground { get; init; }
    public required Color TooltipBorder { get; init; }
    public required Color TooltipText { get; init; }

    /// <summary>FICSIT orange, the app accent.</summary>
    public static readonly Color Accent = Color.FromArgb("#F89B4B");
    public static readonly Color AccentDeep = Color.FromArgb("#E8772E");

    public static readonly CanvasTheme Dark = new()
    {
        Background = Color.FromArgb("#121212"),
        GridDot = Color.FromArgb("#262626"),
        CardBackground = Color.FromArgb("#2B2B2B"),
        CardBorder = Color.FromArgb("#454545"),
        SelectedBorder = Accent,
        ValueRowBackground = Color.FromArgb("#222222"),
        LimitBoxBackground = Color.FromArgb("#1B1B1B"),
        Text = Color.FromArgb("#F2F2F2"),
        MutedText = Color.FromArgb("#9A9A9A"),
        Wire = Color.FromArgb("#8C8C8C"),
        PortChip = Color.FromArgb("#3A3A3A"),
        UnmadeFlag = Color.FromArgb("#D04A60"),
        UnusedFlag = Color.FromArgb("#2F9E55"),
        InvalidText = Color.FromArgb("#FF5252"),
        NonMatchingText = Color.FromArgb("#FFA552"),
        RubberBand = Color.FromArgb("#33F89B4B"),
        TooltipBackground = Color.FromArgb("#262626"),
        TooltipBorder = Color.FromArgb("#454545"),
        TooltipText = Color.FromArgb("#F2F2F2"),
    };

    public static readonly CanvasTheme Light = new()
    {
        Background = Color.FromArgb("#FAFAFA"),
        GridDot = Color.FromArgb("#E2E2E2"),
        CardBackground = Colors.White,
        CardBorder = Color.FromArgb("#D0D0D0"),
        SelectedBorder = AccentDeep,
        ValueRowBackground = Color.FromArgb("#F0F0F0"),
        LimitBoxBackground = Color.FromArgb("#F7F7F7"),
        Text = Color.FromArgb("#1A1A1A"),
        MutedText = Color.FromArgb("#707070"),
        Wire = Color.FromArgb("#9A9A9A"),
        PortChip = Color.FromArgb("#ECECEC"),
        UnmadeFlag = Color.FromArgb("#F2A4B2"),
        UnusedFlag = Color.FromArgb("#9ED9A6"),
        InvalidText = Color.FromArgb("#C62828"),
        NonMatchingText = Color.FromArgb("#E07C00"),
        RubberBand = Color.FromArgb("#33E8772E"),
        TooltipBackground = Colors.White,
        TooltipBorder = Color.FromArgb("#D0D0D0"),
        TooltipText = Color.FromArgb("#1A1A1A"),
    };
}
