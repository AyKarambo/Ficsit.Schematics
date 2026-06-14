namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Converter.</summary>
public sealed class ConverterRecipes : RecipeModule
{
    protected override string Machine => "Converter";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(15, "Excited Photonic Matter", "3", "9-2", [Out("Excited Photonic Matter", 10)], AveragePower: "-250", MinPower: "-400"),
        new(68, "Iron Ore (Limestone)", "6", "9-1", [In("Reanimated SAM", 1), In("Limestone", 24), Out("Iron Ore", 12)], AveragePower: "-250", MinPower: "-400"),
        new(69, "Limestone (Sulfur)", "6", "9-1", [In("Reanimated SAM", 1), In("Sulfur", 2), Out("Limestone", 12)], AveragePower: "-250", MinPower: "-400"),
        new(70, "Copper Ore (Quartz)", "6", "9-1", [In("Reanimated SAM", 1), In("Raw Quartz", 10), Out("Copper Ore", 12)], AveragePower: "-250", MinPower: "-400"),
        new(71, "Copper Ore (Sulfur)", "6", "9-1", [In("Reanimated SAM", 1), In("Sulfur", 12), Out("Copper Ore", 12)], AveragePower: "-250", MinPower: "-400"),
        new(72, "Caterium Ore (Copper)", "6", "9-1", [In("Reanimated SAM", 1), In("Copper Ore", 15), Out("Caterium Ore", 12)], AveragePower: "-250", MinPower: "-400"),
        new(73, "Caterium Ore (Quartz)", "6", "9-1", [In("Reanimated SAM", 1), In("Raw Quartz", 12), Out("Caterium Ore", 12)], AveragePower: "-250", MinPower: "-400"),
        new(74, "Sulfur (Coal)", "6", "9-1", [In("Reanimated SAM", 1), In("Coal", 20), Out("Sulfur", 12)], AveragePower: "-250", MinPower: "-400"),
        new(75, "Sulfur (Iron)", "6", "9-1", [In("Reanimated SAM", 1), In("Iron Ore", 30), Out("Sulfur", 12)], AveragePower: "-250", MinPower: "-400"),
        new(76, "Raw Quartz (Bauxite)", "6", "9-1", [In("Reanimated SAM", 1), In("Bauxite", 10), Out("Raw Quartz", 12)], AveragePower: "-250", MinPower: "-400"),
        new(77, "Raw Quartz (Coal)", "6", "9-1", [In("Reanimated SAM", 1), In("Coal", 24), Out("Raw Quartz", 12)], AveragePower: "-250", MinPower: "-400"),
        new(99, "Coal (Iron)", "6", "9-1", [In("Reanimated SAM", 1), In("Iron Ore", 18), Out("Coal", 12)], AveragePower: "-250", MinPower: "-400"),
        new(100, "Coal (Limestone)", "6", "9-1", [In("Reanimated SAM", 1), In("Limestone", 36), Out("Coal", 12)], AveragePower: "-250", MinPower: "-400"),
        new(176, "Dark-Ion Fuel", "3", "9-2", [In("Packaged Rocket Fuel", 12), In("Dark Matter Crystal", 4), Out("Ionized Fuel", 10), Out("Compacted Coal", 2)], Alternate: true, AveragePower: "-250", MinPower: "-400"),
        new(216, "Bauxite (Caterium)", "6", "9-1", [In("Reanimated SAM", 1), In("Caterium Ore", 15), Out("Bauxite", 12)], AveragePower: "-250", MinPower: "-400"),
        new(217, "Bauxite (Copper)", "6", "9-1", [In("Reanimated SAM", 1), In("Copper Ore", 18), Out("Bauxite", 12)], AveragePower: "-250", MinPower: "-400"),
        new(245, "Uranium Ore (Bauxite)", "6", "9-1", [In("Reanimated SAM", 1), In("Bauxite", 48), Out("Uranium", 12)], AveragePower: "-250", MinPower: "-400"),
        new(263, "Nitrogen Gas (Bauxite)", "6", "9-1", [In("Reanimated SAM", 1), In("Bauxite", 10), Out("Nitrogen Gas", 12)], AveragePower: "-250", MinPower: "-400"),
        new(264, "Nitrogen Gas (Caterium)", "6", "9-1", [In("Reanimated SAM", 1), In("Caterium Ore", 12), Out("Nitrogen Gas", 12)], AveragePower: "-250", MinPower: "-400"),
        new(291, "Pink Diamonds", "4", "9-1", [In("Coal", 8), In("Quartz Crystal", 3), Out("Diamonds", 1)], Alternate: true, AveragePower: "-250", MinPower: "-400"),
        new(293, "Time Crystal", "10", "9-1", [In("Diamonds", 2), Out("Time Crystal", 1)], AveragePower: "-250", MinPower: "-400"),
        new(295, "Ficsite Ingot (Aluminum)", "2", "9-1", [In("Reanimated SAM", 2), In("Aluminum Ingot", 4), Out("Ficsite Ingot", 1)], AveragePower: "-250", MinPower: "-400"),
        new(296, "Ficsite Ingot (Caterium)", "4", "9-1", [In("Reanimated SAM", 3), In("Caterium Ingot", 4), Out("Ficsite Ingot", 1)], AveragePower: "-250", MinPower: "-400"),
        new(297, "Ficsite Ingot (Iron)", "6", "9-1", [In("Reanimated SAM", 4), In("Iron Ingot", 24), Out("Ficsite Ingot", 1)], AveragePower: "-250", MinPower: "-400"),
        new(306, "Dark Matter Residue", "6", "9-2", [In("Reanimated SAM", 5), Out("Dark Matter Residue", 10)], AveragePower: "-250", MinPower: "-400"),
    ];
}
