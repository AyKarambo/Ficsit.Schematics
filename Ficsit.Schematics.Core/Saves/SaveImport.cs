using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Serialization;

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

    /// <summary>True when a build class is one we place as a node (a real producer/consumer),
    /// as opposed to transport (belts, lifts, splitters/mergers, pipes). Used by the connection
    /// tracer to know where a wire run terminates.</summary>
    public static bool IsModelledMachine(string className) => ClassToMachine.ContainsKey(className);

    /// <summary>Machine families whose node sits on a map resource node (so it snaps + takes
    /// the node's recipe).</summary>
    private static readonly HashSet<string> SnapFamilies =
        ["Miner", "Oil Extractor", "Resource Well Extractor"];

    /// <summary>Centimetres per canvas unit (1 unit = 1 m) — matches <see cref="MapSnap.CmPerUnit"/>.</summary>
    private const double CmPerUnit = MapSnap.CmPerUnit;

    /// <summary>How close (cm) a machine must be to a resource node to be treated as sitting on it.</summary>
    private const double SnapRadiusCm = 5_000; // 50 m — each node holds at most one extractor

    /// <summary>Build factory nodes for the parsed world. Extractors snap to their resource node;
    /// other machines take their real recipe from the save's <c>mCurrentRecipe</c> values.</summary>
    public static IReadOnlyList<FactoryNode> BuildNodes(SaveWorld world, GameDatabase data)
        => PlaceBuildings(world, data).Nodes;

    /// <summary>Full import: machines placed at their world positions plus the connections recovered
    /// from the save's belt/pipe graph (recipe-compatible producer→consumer edges) and its vehicle
    /// circuits (the same edges across a truck road network or drone pairing, tagged with the
    /// vehicle kind).</summary>
    public static (IReadOnlyList<FactoryNode> Nodes, IReadOnlyList<NodeConnection> Connections) Build(
        SaveWorld world, GameDatabase data)
    {
        var (nodes, byInstance) = PlaceBuildings(world, data);
        var connections = new List<NodeConnection>();
        var seen = new HashSet<(int, int, string)>();

        Materialize(SaveConnectionTracer.MachineEdges(world.ComponentLinks, IsModelledMachine),
            byInstance, data, connections, seen, LogisticsKind.None);

        // Vehicle circuits: wire each route's stations together as transport and re-trace. Belt
        // edges reappear and are deduped, so the edges that survive are the vehicle-borne ones.
        foreach (var group in world.VehicleRoutes.GroupBy(r => r.Kind))
        {
            var augmented = new Dictionary<string, string>(world.ComponentLinks, StringComparer.Ordinal);
            var hop = 0;
            foreach (var route in group)
                for (var s = 1; s < route.Stations.Count; s++, hop++)
                    augmented[$"{route.Stations[s - 1]}.VehicleRoute{hop}A"]
                        = $"{route.Stations[s]}.VehicleRoute{hop}B";
            Materialize(SaveConnectionTracer.MachineEdges(augmented, IsModelledMachine),
                byInstance, data, connections, seen, group.Key);
        }
        return (nodes, connections);
    }

    private static (List<FactoryNode> Nodes, Dictionary<string, FactoryNode> ByInstance) PlaceBuildings(
        SaveWorld world, GameDatabase data)
    {
        var nodes = new List<FactoryNode>();
        var byInstance = new Dictionary<string, FactoryNode>(StringComparer.Ordinal);
        var usedNodes = new HashSet<string>();
        var byToken = RecipeTokenIndex(data);
        // Per-actor recipes are exact; the order-correlated queues are the whole-save fallback
        // for bodies where the per-object scan yielded nothing (mixing the two would misalign).
        var recipeQueues = world.Buildings.Any(b => b.RecipeStem is not null)
            ? null : BuildRecipeQueues(world, data, byToken);

        foreach (var building in world.Buildings)
        {
            if (!ClassToMachine.TryGetValue(building.ClassName, out var machine)) continue;
            FactoryNode? node = null;

            // An extractor's recipe is the ore of the node it sits on, so without a free matching
            // node we can't say what it mines — snap it or drop it (rather than guess an ore).
            if (SnapFamilies.Contains(machine))
            {
                node = TrySnapExtractor(building, machine, world, data, usedNodes);
            }
            else if (data.GeneratorMachines.Contains(machine))
            {
                // A generator is one machine that burns any fuel; place the unified node — its fuel
                // comes from the connection, not a guessed recipe.
                node = new FactoryNode
                {
                    Name = machine,
                    Kind = NodeKind.Generator,
                    X = building.X / CmPerUnit,
                    Y = building.Y / CmPerUnit,
                    ClockSpeed = building.ClockSpeed,
                    Somersloops = building.Somersloops,
                    Max = "1", // one physical generator
                };
            }
            else
            {
                // The machine's own mCurrentRecipe when the scan attributed one (exact); else the
                // k-th recipe of its type from the ordered stems; else a best-effort first recipe.
                var recipe = ResolveRecipeStem(building.RecipeStem, byToken, data, machine)
                    ?? (recipeQueues is not null
                        && recipeQueues.TryGetValue(machine, out var queue) && queue.Count > 0
                        ? queue.Dequeue()
                        : FirstRecipe(data, machine));
                if (recipe is not null)
                    node = new FactoryNode
                    {
                        Name = recipe,
                        Kind = NodeKind.Recipe,
                        X = building.X / CmPerUnit,
                        Y = building.Y / CmPerUnit,
                        ClockSpeed = building.ClockSpeed,
                        Somersloops = building.Somersloops,
                        Max = "1", // one physical machine (count display)
                    };
            }

            if (node is null) continue;
            nodes.Add(node);
            byInstance[building.Instance] = node;
        }

        return (nodes, byInstance);
    }

    /// <summary>Materialize traced machine→machine edges as recipe-compatible connections: an
    /// edge becomes a connection only when the producer makes a part the consumer takes. Edges
    /// already materialized (in <paramref name="seen"/>) are skipped, so belt edges keep their
    /// plain kind when a vehicle re-trace reproduces them.</summary>
    private static void Materialize(
        IReadOnlyList<(string From, string To)> edges,
        Dictionary<string, FactoryNode> byInstance,
        GameDatabase data,
        List<NodeConnection> connections,
        HashSet<(int, int, string)> seen,
        LogisticsKind kind)
    {
        foreach (var (fromInstance, toInstance) in edges)
        {
            if (!byInstance.TryGetValue(fromInstance, out var from)
                || !byInstance.TryGetValue(toInstance, out var to) || from == to) continue;
            var part = OutputParts(from, data).FirstOrDefault(InputParts(to, data).Contains);
            if (part is null) continue;
            if (seen.Add((from.Id, to.Id, part)))
                connections.Add(new NodeConnection { From = from, To = to, Part = part, Logistics = kind });
        }
    }

    /// <summary>Parts a node can put out: its recipe's outputs (or, for a generator, its machine's
    /// outputs, e.g. nuclear waste).</summary>
    private static IEnumerable<string> OutputParts(FactoryNode node, GameDatabase data)
        => node.Kind == NodeKind.Generator
            ? data.Document.Recipes.Where(r => r.Machine == node.Name).SelectMany(r => r.Outputs).Select(p => p.Part)
            : data.RecipesByName.TryGetValue(node.Name, out var r) ? r.Outputs.Select(p => p.Part) : [];

    /// <summary>Parts a node takes in: its recipe's inputs (or, for a generator, any of its fuels).</summary>
    private static IEnumerable<string> InputParts(FactoryNode node, GameDatabase data)
        => node.Kind == NodeKind.Generator
            ? data.Document.Recipes.Where(r => r.Machine == node.Name).SelectMany(r => r.Inputs).Select(p => p.Part)
            : data.RecipesByName.TryGetValue(node.Name, out var r) ? r.Inputs.Select(p => p.Part) : [];

    /// <summary>Recipe stems whose internal name shares no words with the display name — the
    /// space-elevator project parts and a few renames. Validated against the catalog (and the
    /// machine) on use, so a wrong entry is a no-op rather than a mislabel.</summary>
    private static readonly Dictionary<string, string> RecipeStemOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SpaceElevatorPart_1"] = "Smart Plating",
        ["SpaceElevatorPart_2"] = "Versatile Framework",
        ["SpaceElevatorPart_3"] = "Automated Wiring",
        ["SpaceElevatorPart_4"] = "Modular Engine",
        ["SpaceElevatorPart_5"] = "Adaptive Control Unit",
        ["SpaceElevatorPart_6"] = "Magnetic Field Generator",
        ["SpaceElevatorPart_7"] = "Assembly Director System",
        ["SpaceElevatorPart_8"] = "Thermal Propulsion Rocket",
        ["SpaceElevatorPart_9"] = "Nuclear Pasta",
        ["SpaceElevatorPart_10"] = "Biochemical Sculptor",
        ["SpaceElevatorPart_11"] = "Ballistic Warp Drive",
        ["SpaceElevatorPart_12"] = "AI Expansion Server",
        ["Biofuel"] = "Solid Biofuel",
    };

    /// <summary>The catalog recipe name a save stem points at, or null: the recipe-stem override
    /// table, then (for alternates) the divergent-schematic-name table, then the shared token
    /// match. No catalog validation here — callers check existence and machine.</summary>
    private static string? StemToRecipeName(string stem, Dictionary<string, string?> byToken)
    {
        var isAlternate = stem.StartsWith("Alternate_", StringComparison.Ordinal);
        var bare = isAlternate ? stem["Alternate_".Length..] : stem;
        return RecipeStemOverrides.GetValueOrDefault(stem)
            ?? (isAlternate ? SchematicRecipeMap.OverrideFor(bare) : null)
            ?? byToken.GetValueOrDefault(SchematicRecipeMap.TokenKey(bare));
    }

    /// <summary>Token-set index over every recipe name (e.g. "packaged|water"), unique matches
    /// only — how a save recipe stem finds its catalog recipe. Legacy names from the alias
    /// table index onto the official recipe too (never shadowing a live key), so a save stem
    /// that still matches a pre-rename name (e.g. "Screw" → "Screws") keeps resolving.</summary>
    private static Dictionary<string, string?> RecipeTokenIndex(GameDatabase data)
    {
        var byToken = new Dictionary<string, string?>();
        foreach (var recipe in data.Document.Recipes)
        {
            var key = SchematicRecipeMap.TokenKey(recipe.Name);
            byToken[key] = byToken.ContainsKey(key) ? null : recipe.Name;
        }
        foreach (var (legacy, official) in NameAliases.ByLegacyName)
            if (data.RecipesByName.ContainsKey(official))
                byToken.TryAdd(SchematicRecipeMap.TokenKey(legacy), official);
        return byToken;
    }

    /// <summary>The catalog recipe a save stem names, when it exists and belongs to
    /// <paramref name="machine"/> (a stem resolving to another machine's recipe is a misparse).</summary>
    private static string? ResolveRecipeStem(
        string? stem, Dictionary<string, string?> byToken, GameDatabase data, string machine)
    {
        if (stem is null || StemToRecipeName(stem, byToken) is not { } name) return null;
        return data.RecipesByName.TryGetValue(name, out var def) && def.Machine == machine ? name : null;
    }

    /// <summary>Resolve the save's recipe stems to catalog recipes and group them by machine, in
    /// order. Same serialization order as the building headers, so dequeuing per machine lines the
    /// k-th machine of a type up with the k-th recipe of that type.</summary>
    private static Dictionary<string, Queue<string>> BuildRecipeQueues(
        SaveWorld world, GameDatabase data, Dictionary<string, string?> byToken)
    {
        var queues = new Dictionary<string, Queue<string>>();
        foreach (var stem in world.RecipeStems)
        {
            if (StemToRecipeName(stem, byToken) is not { } name) continue;
            if (!data.RecipesByName.TryGetValue(name, out var def)) continue;
            if (!queues.TryGetValue(def.Machine, out var queue)) queues[def.Machine] = queue = new();
            queue.Enqueue(name);
        }
        return queues;
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

    /// <summary>The machine's first standard recipe, as a best-effort placeholder when the save
    /// carried no recipe for it. Null when we model no recipe for the machine.</summary>
    private static string? FirstRecipe(GameDatabase data, string machine)
        => data.Document.Recipes.FirstOrDefault(r => r.Machine == machine && !r.Alternate)?.Name
            ?? data.Document.Recipes.FirstOrDefault(r => r.Machine == machine)?.Name;

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
}
