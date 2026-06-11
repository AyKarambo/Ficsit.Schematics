using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// A node on the canvas: a recipe machine, or one of the specialty machines.
/// Mutable by design — the editor manipulates it through undo/redo commands.
/// </summary>
public sealed class FactoryNode
{
    private static int _nextId;

    public int Id { get; } = Interlocked.Increment(ref _nextId);

    public NodeKind Kind { get; set; } = NodeKind.Recipe;

    /// <summary>Recipe name for recipe nodes; specialty display name otherwise ("Outpost", "AWESOME Sink", …).</summary>
    public string Name { get; set; } = string.Empty;

    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Optional user caption shown above the node.</summary>
    public string? Title { get; set; }

    /// <summary>Limit as entered ("60", "1 1/5"); null/empty = no limit.</summary>
    public string? Max { get; set; }

    /// <summary>1 = 100%. Valid range (0, 2.5].</summary>
    public Rational ClockSpeed { get; set; } = Rational.One;

    public int Somersloops { get; set; }

    public bool AutoRound { get; set; }

    /// <summary>Display machine count (false) or parts-per-minute (true); null = machine-family default.</summary>
    public bool? ShowPpm { get; set; }

    /// <summary>Selected variant for multi-machines, e.g. "Miner Mk.2".</summary>
    public string? MachineVariant { get; set; }

    /// <summary>Selected capacity, e.g. "Pure", "Mk.3 Belt", "30/min".</summary>
    public string? Capacity { get; set; }

    public StorageMode StorageMode { get; set; } = StorageMode.PartiallyFull;

    /// <summary>Nested canvas for outposts/blueprints.</summary>
    public FactoryGraph? Children { get; set; }

    public double InnerZoom { get; set; } = 1.0;
    public double InnerPanX { get; set; }
    public double InnerPanY { get; set; }

    public bool HasLimit => !string.IsNullOrWhiteSpace(Max);

    public Rational? LimitValue =>
        HasLimit && Rational.TryParse(Max, out var v) ? v : null;
}
