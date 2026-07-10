namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>How a machine's recipe module is populated.</summary>
public enum ModuleKind
{
    /// <summary>No recipe module (storage, sink, uploader, …).</summary>
    None,

    /// <summary>Rows come from FGRecipe entries whose mProducedIn names the build class.</summary>
    Recipes,

    /// <summary>Rows are synthesized from extractor stats (ores, water, oil, wells).</summary>
    Extractor,

    /// <summary>Rows are synthesized from the generator's fuel list and fuel energy values.</summary>
    FuelGenerator,

    /// <summary>Rows come from a curated table in <see cref="Overrides"/> (the export has no equivalent).</summary>
    Curated,
}

/// <summary>
/// One machine the app models, tied to its UE build class. This table is the curated
/// bridge between the export and the catalog: which buildings exist as canvas machines,
/// what they are called, and how their recipe modules are derived. Hand-maintained.
/// </summary>
public sealed record ModeledMachine(string Name, string BuildClass, ModuleKind Kind)
{
    /// <summary>The machine name recipes target: the family name for marks ("Miner"), else the machine name.</summary>
    public string RecipeMachine { get; init; } = Name;
}

/// <summary>Every machine the app models (canonical names as they appear on the canvas).</summary>
public static class MachineTable
{
    public static readonly IReadOnlyList<ModeledMachine> All =
    [
        // Production machines whose recipes come from FGRecipe.
        new("Smelter", "Build_SmelterMk1_C", ModuleKind.Recipes),
        new("Foundry", "Build_FoundryMk1_C", ModuleKind.Recipes),
        new("Constructor", "Build_ConstructorMk1_C", ModuleKind.Recipes),
        new("Assembler", "Build_AssemblerMk1_C", ModuleKind.Recipes),
        new("Manufacturer", "Build_ManufacturerMk1_C", ModuleKind.Recipes),
        new("Refinery", "Build_OilRefinery_C", ModuleKind.Recipes),
        new("Packager", "Build_Packager_C", ModuleKind.Recipes),
        new("Blender", "Build_Blender_C", ModuleKind.Recipes),
        new("Particle Accelerator", "Build_HadronCollider_C", ModuleKind.Recipes),
        new("Converter", "Build_Converter_C", ModuleKind.Recipes),
        new("Quantum Encoder", "Build_QuantumEncoder_C", ModuleKind.Recipes),

        // Extractors; the Miner marks share one family module named "Miner".
        new("Miner Mk.1", "Build_MinerMk1_C", ModuleKind.Extractor) { RecipeMachine = "Miner" },
        new("Miner Mk.2", "Build_MinerMk2_C", ModuleKind.None) { RecipeMachine = "Miner" },
        new("Miner Mk.3", "Build_MinerMk3_C", ModuleKind.None) { RecipeMachine = "Miner" },
        new("Water Extractor", "Build_WaterPump_C", ModuleKind.Extractor),
        new("Oil Extractor", "Build_OilPump_C", ModuleKind.Extractor),
        new("Resource Well Extractor", "Build_FrackingExtractor_C", ModuleKind.Extractor),
        new("Resource Well Pressurizer", "Build_FrackingSmasher_C", ModuleKind.Curated),

        // Power generators (fuel-burning ones synthesize one recipe per fuel).
        new("Biomass Burner", "Build_GeneratorBiomass_Automated_C", ModuleKind.FuelGenerator),
        new("Coal-Powered Generator", "Build_GeneratorCoal_C", ModuleKind.FuelGenerator),
        new("Fuel-Powered Generator", "Build_GeneratorFuel_C", ModuleKind.FuelGenerator),
        new("Nuclear Power Plant", "Build_GeneratorNuclear_C", ModuleKind.FuelGenerator),
        new("Geothermal Generator", "Build_GeneratorGeoThermal_C", ModuleKind.Curated),
        new("Alien Power Augmenter", "Build_AlienPowerBuilding_C", ModuleKind.Curated),

        // Specials.
        new("AWESOME Sink", "Build_ResourceSink_C", ModuleKind.None),
        new("Space Elevator", "Build_SpaceElevator_C", ModuleKind.Curated),
        new("FICSMAS Gift Tree", "Build_TreeGiftProducer_C", ModuleKind.Curated),
        new("Dimensional Depot Uploader", "Build_CentralStorage_C", ModuleKind.None),

        // Storage.
        new("Storage Container", "Build_StorageContainerMk1_C", ModuleKind.None),
        new("Industrial Storage Container", "Build_StorageContainerMk2_C", ModuleKind.None),
        new("Fluid Buffer", "Build_PipeStorageTank_C", ModuleKind.None),
        new("Industrial Fluid Buffer", "Build_IndustrialTank_C", ModuleKind.None),
    ];

    public static ModeledMachine? ByBuildClass(string buildClass)
        => All.FirstOrDefault(m => m.BuildClass == buildClass);
}
