namespace Ficsit.Schematics.Core.Model;

/// <summary>Per machine-family placement defaults (Machine Defaults settings).</summary>
public sealed class MachineDefaultSetting
{
    public string Name { get; set; } = string.Empty;
    public bool? ShowPpm { get; set; }
    public string? DefaultMax { get; set; }
    public bool? AutoRound { get; set; }
    public string? DefaultMachine { get; set; }
    public string? DefaultCapacity { get; set; }
    public string? DefaultMode { get; set; }
}
