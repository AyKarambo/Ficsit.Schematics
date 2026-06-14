namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>Every machine and multi-machine family, grouped by category.</summary>
public sealed class MachinesCatalog : MachineModule
{
    public override IReadOnlyList<(int Sort, MachineDefinition Definition)> Machines =>
    [
        // ----- Extractors
        (4, new() { Name = "Miner Mk.1", Tier = "0-5", AveragePower = "-5", OverclockPowerExponent = "1321929/1000000", Cost = [C("Portable Miner", 1), C("Iron Plate", 10), C("Concrete", 10)] }),
        (10, new() { Name = "Water Extractor", Tier = "3-1", AveragePower = "-20", OverclockPowerExponent = "1321929/1000000", Cost = [C("Copper Sheet", 20), C("Reinforced Iron Plate", 10), C("Rotor", 10)] }),
        (14, new() { Name = "Miner Mk.2", Tier = "4-3", AveragePower = "-15", OverclockPowerExponent = "1321929/1000000", Cost = [C("Portable Miner", 2), C("Encased Industrial Beam", 10), C("Steel Pipe", 20), C("Modular Frame", 10)] }),
        (16, new() { Name = "Oil Extractor", Tier = "5-2", AveragePower = "-40", OverclockPowerExponent = "1321929/1000000", Cost = [C("Motor", 15), C("Encased Industrial Beam", 20), C("Cable", 60)] }),
        (24, new() { Name = "Resource Well Extractor", Tier = "8-3", Cost = [C("Steel Beam", 10), C("Aluminum Casing", 10)] }),
        (25, new() { Name = "Resource Well Pressurizer", Tier = "8-3", AveragePower = "-150", OverclockPowerExponent = "1321929/1000000", Cost = [C("Radio Control Unit", 10), C("Heavy Modular Frame", 25), C("Motor", 50), C("Alclad Aluminum Sheet", 50), C("Rubber", 100)] }),
        (26, new() { Name = "Miner Mk.3", Tier = "8-4", AveragePower = "-45", OverclockPowerExponent = "1321929/1000000", Cost = [C("Portable Miner", 3), C("Steel Pipe", 50), C("Supercomputer", 5), C("Fused Modular Frame", 10), C("Turbo Motor", 3)] }),
        // ----- Generators
        (6, new() { Name = "Biomass Burner", Tier = "0-6", AveragePower = "30", OverclockPowerExponent = "1", Cost = [C("Iron Plate", 15), C("Iron Rod", 15), C("Wire", 25)] }),
        (11, new() { Name = "Coal-Powered Generator", Tier = "3-1", AveragePower = "75", OverclockPowerExponent = "1", Cost = [C("Reinforced Iron Plate", 20), C("Rotor", 10), C("Cable", 30)] }),
        (19, new() { Name = "Fuel-Powered Generator", Tier = "5-5", AveragePower = "250", OverclockPowerExponent = "1", Cost = [C("Motor", 15), C("Encased Industrial Beam", 15), C("Copper Sheet", 30), C("Rubber", 50), C("Quickwire", 50)] }),
        (21, new() { Name = "Geothermal Generator", Tier = "6-1", AveragePower = "200", MinPower = "100", Cost = [C("Motor", 10), C("High-Speed Connector", 25), C("Modular Frame", 25), C("Copper Sheet", 50), C("Wire", 250)] }),
        (23, new() { Name = "Nuclear Power Plant", Tier = "8-2", AveragePower = "2500", OverclockPowerExponent = "1", Cost = [C("Supercomputer", 10), C("Heavy Modular Frame", 25), C("Alclad Aluminum Sheet", 100), C("Cable", 200), C("Concrete", 250)] }),
        (28, new() { Name = "Alien Power Augmenter", Tier = "9-1", BasePower = "500", BasePowerBoost = "1/10", FueledBasePowerBoost = "3/10", Cost = [C("Somersloop", 10), C("SAM Fluctuator", 50), C("Cable", 100), C("Encased Industrial Beam", 50), C("Motor", 25), C("Computer", 10)] }),
        // ----- Smelting
        (0, new() { Name = "Smelter", Tier = "0-2", AveragePower = "-4", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 1, ProductionShardMultiplier = "1", ProductionShardPowerExponent = "2", Cost = [C("Iron Rod", 5), C("Wire", 8)] }),
        (12, new() { Name = "Foundry", Tier = "3-3", AveragePower = "-16", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 2, ProductionShardMultiplier = "1/2", ProductionShardPowerExponent = "2", Cost = [C("Modular Frame", 10), C("Rotor", 10), C("Concrete", 20)] }),
        // ----- Production
        (2, new() { Name = "Constructor", Tier = "0-3", AveragePower = "-4", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 1, ProductionShardMultiplier = "1", ProductionShardPowerExponent = "2", Cost = [C("Reinforced Iron Plate", 2), C("Cable", 8)] }),
        (7, new() { Name = "Assembler", Tier = "2-1", AveragePower = "-15", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 2, ProductionShardMultiplier = "1/2", ProductionShardPowerExponent = "2", Cost = [C("Reinforced Iron Plate", 8), C("Rotor", 4), C("Cable", 10)] }),
        (15, new() { Name = "Refinery", Tier = "5-2", AveragePower = "-30", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 2, ProductionShardMultiplier = "1/2", ProductionShardPowerExponent = "2", Cost = [C("Motor", 10), C("Encased Industrial Beam", 10), C("Steel Pipe", 30), C("Copper Sheet", 20)] }),
        (17, new() { Name = "Packager", Tier = "5-4", AveragePower = "-10", OverclockPowerExponent = "1321929/1000000", Cost = [C("Steel Beam", 20), C("Rubber", 10), C("Plastic", 10)] }),
        (20, new() { Name = "Manufacturer", Tier = "6-1", AveragePower = "-55", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 4, ProductionShardMultiplier = "1/4", ProductionShardPowerExponent = "2", Cost = [C("Motor", 10), C("Modular Frame", 20), C("Plastic", 50), C("Cable", 50)] }),
        (22, new() { Name = "Blender", Tier = "7-5", AveragePower = "-75", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 4, ProductionShardMultiplier = "1/4", ProductionShardPowerExponent = "2", Cost = [C("Computer", 10), C("Heavy Modular Frame", 10), C("Motor", 20), C("Aluminum Casing", 50)] }),
        (27, new() { Name = "Particle Accelerator", Tier = "8-5", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 4, ProductionShardMultiplier = "1/4", ProductionShardPowerExponent = "2", Cost = [C("Turbo Motor", 10), C("Supercomputer", 10), C("Fused Modular Frame", 25), C("Cooling System", 50), C("Quickwire", 500)] }),
        (29, new() { Name = "Converter", Tier = "9-1", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 2, ProductionShardMultiplier = "1/2", ProductionShardPowerExponent = "2", Cost = [C("Fused Modular Frame", 10), C("Cooling System", 25), C("Radio Control Unit", 50), C("SAM Fluctuator", 100)] }),
        (31, new() { Name = "Quantum Encoder", Tier = "9-2", OverclockPowerExponent = "1321929/1000000", MaxProductionShards = 4, ProductionShardMultiplier = "1/4", ProductionShardPowerExponent = "2", Cost = [C("Turbo Motor", 20), C("Supercomputer", 20), C("Cooling System", 50), C("Time Crystal", 50), C("Ficsite Trigon", 100)] }),
        // ----- Storage
        (3, new() { Name = "Storage Container", Tier = "0-5", Cost = [C("Iron Plate", 10), C("Iron Rod", 10)] }),
        (9, new() { Name = "Fluid Buffer", Tier = "3-1", Cost = [C("Copper Sheet", 10), C("Modular Frame", 5)] }),
        (13, new() { Name = "Industrial Storage Container", Tier = "4-2", Cost = [C("Steel Beam", 20), C("Steel Pipe", 20)] }),
        (18, new() { Name = "Industrial Fluid Buffer", Tier = "5-5", Cost = [C("Encased Industrial Beam", 5), C("Copper Sheet", 10), C("Plastic", 25)] }),
        // ----- Special
        (1, new() { Name = "FICSMAS Gift Tree", Tier = "0-3", Cost = [C("FICSMAS Gift", 50), C("FICSMAS Tree Branch", 100), C("Red FICSMAS Ornament", 30), C("Blue FICSMAS Ornament", 30)] }),
        (5, new() { Name = "Space Elevator", Tier = "0-6", Cost = [C("Concrete", 500), C("Iron Plate", 250), C("Iron Rod", 400), C("Wire", 1500)] }),
        (8, new() { Name = "AWESOME Sink", Tier = "2-4", AveragePower = "-30", Cost = [C("Reinforced Iron Plate", 15), C("Cable", 30), C("Concrete", 45)] }),
        (30, new() { Name = "Dimensional Depot Uploader", Tier = "9-1", Cost = [C("Mercer Sphere", 1), C("SAM Fluctuator", 10), C("Modular Frame", 10), C("Wire", 100)] }),
    ];

    public override IReadOnlyList<(int Sort, MultiMachineDefinition Definition)> Families =>
    [
        // ----- Extractors
        (0, new() { Name = "Miner", ShowPpm = true, AutoRound = false, DefaultMax = "60", Machines = [V("Miner Mk.1", "60", isDefault: true), V("Miner Mk.2", "120"), V("Miner Mk.3", "240")], Capacities = [Cap("Impure", "1/2", color: 13775920), Cap("Normal", isDefault: true), Cap("Pure", "2", color: 8433977)] }),
        (1, new() { Name = "Oil Extractor", ShowPpm = true, AutoRound = false, DefaultMax = "120", Capacities = [Cap("Impure", "1/2", color: 13775920), Cap("Normal", isDefault: true), Cap("Pure", "2", color: 8433977)] }),
        (2, new() { Name = "Resource Well Extractor", ShowPpm = true, DefaultMax = "60", Capacities = [Cap("Impure", "1/2", color: 13775920), Cap("Normal", isDefault: true), Cap("Pure", "2", color: 8433977)] }),
        // ----- Generators
        (5, new() { Name = "Geothermal Generator", DefaultMax = "1", Capacities = [Cap("Impure", color: 13775920, powerRatio: "1/2"), Cap("Normal", isDefault: true), Cap("Pure", color: 8433977, powerRatio: "2")] }),
        // ----- Special
        (3, new() { Name = "Dimensional Depot Uploader", ShowPpm = true, Capacities = [Cap("15/min", "15", isDefault: true), Cap("30/min", "30", color: 13775920), Cap("60/min", "60", color: 16311334), Cap("120/min", "120", color: 8433977), Cap("240/min", "240", color: 12330744)] }),
        (4, new() { Name = "AWESOME Sink", ShowPpm = true, Capacities = [Cap("Mk.1 Belt", "60", isDefault: true), Cap("Mk.2 Belt", "120", color: 13775920), Cap("Mk.3 Belt", "270", color: 16311334), Cap("Mk.4 Belt", "480", color: 8433977), Cap("Mk.5 Belt", "780", color: 2504952), Cap("Mk.6 Belt", "1200", color: 12330744)] }),
        (6, new() { Name = "Space Elevator", DefaultMax = "1" }),
    ];
}
