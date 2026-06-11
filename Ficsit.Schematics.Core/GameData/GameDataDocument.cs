namespace Ficsit.Schematics.Core.GameData;

/// <summary>Root shape of game_data.json (reference-app compatible).</summary>
public sealed class GameDataDocument
{
    public List<MachineDefinition> Machines { get; set; } = [];
    public List<MultiMachineDefinition> MultiMachines { get; set; } = [];
    public List<PartDefinition> Parts { get; set; } = [];
    public List<RecipeDefinition> Recipes { get; set; } = [];
}
