using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Maps a parsed <see cref="SaveWorld"/> onto factory nodes (Phase 1 of "import built
/// factories"): one node per recognised machine at its real map position. Extractors snap to
/// the resource node they sit on and take that node's recipe / purity, so miners land on their
/// nodes already producing the right ore; other machines are placed with a best-effort recipe
/// the user can correct. Pure and UI-free — the caller adds the nodes through the editor in one
/// undoable batch. No connections yet (Phase 2).
/// </summary>
public static class SaveImport
{
    /// <summary>Save build-class → our catalog machine/family name. Unlisted classes
    /// (conveyors, walls, power poles, storage, …) are skipped on import.</summary>
    private static readonly Dictionary<string, string> ClassToMachine = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Build_ConstructorMk1_C"] = "Constructor",
        ["Build_AssemblerMk1_C"] = "Assembler",
        ["Build_ManufacturerMk1_C"] = "Manufacturer",
        ["Build_SmelterMk1_C"] = "Smelter",
        ["Build_FoundryMk1_C"] = "Foundry",
        ["Build_OilRefinery_C"] = "Refinery",
        ["Build_Packager_C"] = "Packager",
        ["Build_Blender_C"] = "Blender",
        ["Build_HadronCollider_C"] = "Particle Accelerator",
        ["Build_Converter_C"] = "Converter",
        ["Build_QuantumEncoder_C"] = "Quantum Encoder",
        ["Build_MinerMk1_C"] = "Miner",
        ["Build_MinerMk2_C"] = "Miner",
        ["Build_MinerMk3_C"] = "Miner",
        ["Build_OilPump_C"] = "Oil Extractor",
        ["Build_WaterPump_C"] = "Water Extractor",
        ["Build_FrackingExtractor_C"] = "Resource Well Extractor",
        ["Build_FrackingSmasher_C"] = "Resource Well Pressurizer",
        ["Build_GeneratorBiomass_Automated_C"] = "Biomass Burner",
        ["Build_GeneratorBiomass_C"] = "Biomass Burner",
        ["Build_GeneratorCoal_C"] = "Coal-Powered Generator",
        ["Build_GeneratorFuel_C"] = "Fuel-Powered Generator",
        ["Build_GeneratorNuclear_C"] = "Nuclear Power Plant",
        ["Build_GeneratorGeoThermal_C"] = "Geothermal Generator",
    };

    /// <summary>Machine families whose node sits on a map resource node (so it snaps + takes
    /// the node's recipe).</summary>
    private static readonly HashSet<string> SnapFamilies =
        ["Miner", "Oil Extractor", "Resource Well Extractor"];

    /// <summary>Centimetres per canvas unit (1 unit = 1 m) — matches <see cref="MapSnap.CmPerUnit"/>.</summary>
    private const double CmPerUnit = MapSnap.CmPerUnit;

    /// <summary>How close (cm) a machine must be to a resource node to be treated as sitting on it.</summary>
    private const double SnapRadiusCm = 5_000; // 50 m — each node holds at most one extractor

    /// <summary>Build factory nodes for the parsed world. Extractors snap to their resource node.</summary>
    public static IReadOnlyList<FactoryNode> BuildNodes(SaveWorld world, GameDatabase data)
    {
        var nodes = new List<FactoryNode>();
        var usedNodes = new HashSet<string>();

        foreach (var building in world.Buildings)
        {
            if (!ClassToMachine.TryGetValue(building.ClassName, out var machine)) continue;

            // An extractor's recipe is the ore of the node it sits on, so without a free matching
            // node we can't say what it mines — snap it or drop it (rather than guess an ore).
            if (SnapFamilies.Contains(machine))
            {
                if (TrySnapExtractor(building, machine, world, data, usedNodes) is { } extractor)
                    nodes.Add(extractor);
                continue;
            }

            // Other machine: place at its world position with its recipe, as one physical machine.
            // RecipeStem is honoured when the save carried one.
            var recipe = ResolveRecipe(data, building.RecipeStem, machine);
            if (recipe is null) continue; // machine we don't model a recipe for (skip silently)

            nodes.Add(new FactoryNode
            {
                Name = recipe,
                Kind = NodeKind.Recipe,
                X = building.X / CmPerUnit,
                Y = building.Y / CmPerUnit,
                ClockSpeed = building.ClockSpeed,
                Somersloops = building.Somersloops,
                Max = "1", // one physical machine (count display)
            });
        }

        return nodes;
    }

    private static FactoryNode? TrySnapExtractor(
        SaveBuilding building, string machine, SaveWorld world, GameDatabase data, HashSet<string> usedNodes)
    {
        ResourceNodeInfo? bestNode = null;
        string? bestRecipe = null;
        var bestDistance = SnapRadiusCm;
        foreach (var rn in world.ResourceNodes)
        {
            if (usedNodes.Contains(rn.Instance)) continue;
            var dx = rn.X - building.X;
            var dy = rn.Y - building.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= bestDistance) continue;
            var recipe = RecipeForNode(data, machine, rn);
            if (recipe is null) continue;
            bestDistance = distance;
            bestNode = rn;
            bestRecipe = recipe;
        }
        if (bestNode is null || bestRecipe is null) return null;

        usedNodes.Add(bestNode.Instance);
        var family = data.MultiMachineFor(machine);
        var node = new FactoryNode
        {
            Name = bestRecipe,
            Kind = NodeKind.Recipe,
            X = bestNode.X / CmPerUnit,
            Y = bestNode.Y / CmPerUnit,
            ClockSpeed = building.ClockSpeed,
            Somersloops = building.Somersloops,
            ResourceNodeId = bestNode.Instance,
            MachineVariant = MarkVariant(building.ClassName, family),
            Capacity = family?.Capacities.Any(c => c.Name == bestNode.Purity) == true ? bestNode.Purity : null,
        };
        return node;
    }

    /// <summary>The recipe a machine of <paramref name="machine"/> would run on resource node
    /// <paramref name="rn"/> (its single matching recipe), or null if it can't work that node.</summary>
    private static string? RecipeForNode(GameDatabase data, string machine, ResourceNodeInfo rn)
        => data.Document.Recipes.FirstOrDefault(r => r.Machine == machine && MapSnap.Matches(data, r, rn))?.Name;

    /// <summary>Resolve a recipe to place: the save's recipe stem when present, else the machine's
    /// first standard recipe as a starting point. Null when we model no recipe for the machine.</summary>
    private static string? ResolveRecipe(GameDatabase data, string? recipeStem, string machine)
    {
        if (recipeStem is not null
            && data.Document.Recipes.FirstOrDefault(r => r.Machine == machine
                && string.Equals(StemOf(r.Name), recipeStem, StringComparison.OrdinalIgnoreCase)) is { } matched)
            return matched.Name;

        return data.Document.Recipes.FirstOrDefault(r => r.Machine == machine && !r.Alternate)?.Name
            ?? data.Document.Recipes.FirstOrDefault(r => r.Machine == machine)?.Name;
    }

    /// <summary>The selected mark variant for a multi-mark family from the class (e.g.
    /// "Build_MinerMk2_C" → "Miner Mk.2"); null for single-machine families.</summary>
    private static string? MarkVariant(string className, MultiMachineDefinition? family)
    {
        if (family is not { Machines.Count: > 0 }) return null;
        var mk = className.IndexOf("Mk", StringComparison.Ordinal);
        if (mk < 0) return null;
        var digits = new string(className[(mk + 2)..].TakeWhile(char.IsAsciiDigit).ToArray());
        var name = $"{family.Name} Mk.{digits}";
        return family.Machines.Any(v => v.Name == name) ? name : null;
    }

    /// <summary>A recipe name reduced to a comparable stem (spaces/punctuation stripped).</summary>
    private static string StemOf(string name)
        => new(name.Where(char.IsLetterOrDigit).ToArray());
}
