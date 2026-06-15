using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// One built machine actor read from a Satisfactory save (the "import built factories"
/// foundation, Phase 0). Carries the raw save data; mapping to our catalog
/// (machine/variant, recipe) happens in <see cref="SaveImport"/>, which has the
/// <see cref="GameData.GameDatabase"/>, so the reader stays catalog-free.
/// </summary>
public sealed class SaveBuilding
{
    /// <summary>The actor's short class, e.g. "Build_ConstructorMk1_C", "Build_MinerMk2_C".</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Save-file actor instance path (unique per save).</summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary>World position in centimetres (Unreal coordinates).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    /// <summary>Clock as a fraction (1 = 100%) from <c>mCurrentPotential</c>; defaults to 100%
    /// (per-building clock parsing is not wired yet, so this stays at 100% on import).</summary>
    public Rational ClockSpeed { get; set; } = Rational.One;

    /// <summary>Production shards (Somersloops) installed.</summary>
    public int Somersloops { get; set; }
}
