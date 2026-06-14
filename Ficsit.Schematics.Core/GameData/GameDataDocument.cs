namespace Ficsit.Schematics.Core.GameData;

/// <summary>The assembled game data: machines, multi-machine families, parts and recipes.</summary>
public sealed class GameDataDocument
{
    public List<MachineDefinition> Machines { get; set; } = [];
    public List<MultiMachineDefinition> MultiMachines { get; set; } = [];
    public List<PartDefinition> Parts { get; set; } = [];
    public List<RecipeDefinition> Recipes { get; set; } = [];
}
