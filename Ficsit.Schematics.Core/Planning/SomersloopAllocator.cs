using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Greedy post-solve allocation of a Somersloop budget across the recipes a plan uses.
/// It only DECIDES the per-machine sloop level for each sloopable recipe; the planner
/// then re-solves with those recipes' output boosted, which rebalances the whole chain
/// and yields the real (reduced) machine counts. The choice ranks recipes by objective
/// benefit per marginal sloop — for the resource objective that favours the recipes
/// whose amplified output spares the most scarcity-weighted raw draw (slooping a smelter
/// halves the ore it pulls). This is an honest heuristic, not a mixed-integer optimum.
/// </summary>
public static class SomersloopAllocator
{
    /// <summary>
    /// Returns recipe name → per-machine sloop count (1..slots) for the recipes worth
    /// amplifying within <paramref name="budget"/>. Empty when nothing is worth it
    /// (no sloopable recipe, zero budget, or the power objective).
    /// </summary>
    public static Dictionary<string, int> Choose(
        GameDatabase data, IReadOnlyList<PlannedRecipe> recipes,
        Dictionary<string, Rational> weights, PlanBias bias, int budget)
    {
        var levels = new Dictionary<string, int>();
        if (budget <= 0 || bias == PlanBias.Power) return levels;

        var n = recipes.Count;
        var machine = new MachineDefinition?[n];
        var sloops = new int[n];          // current per-machine level
        var machines = new Rational[n];   // machine count at that level
        var intensity = new double[n];    // scarcity-weighted raw draw per machine

        for (var i = 0; i < n; i++)
        {
            var def = data.RecipesByName.GetValueOrDefault(recipes[i].Recipe);
            machine[i] = def is { } d ? FactoryPlanner.ResolveMachine(data, d) : null;
            machines[i] = recipes[i].Machines;
            intensity[i] = def is { } dd ? RawIntensity(dd, weights) : 0;
        }

        var remaining = budget;
        while (remaining > 0)
        {
            var bestIndex = -1;
            var bestScore = 0.0;
            var bestCost = 0;
            var bestMachines = Rational.Zero;

            for (var i = 0; i < n; i++)
            {
                var m = machine[i];
                if (m is null || m.MaxProductionShards <= 0 || sloops[i] >= m.MaxProductionShards) continue;

                var nextLevel = sloops[i] + 1;
                var newMachines = recipes[i].Machines / Amplification.OutputFactor(m, nextLevel);

                // Total physical sloops = whole machine count × per-machine level; the
                // marginal cost is how many more that step consumes.
                var prevTotal = sloops[i] == 0 ? 0 : (int)machines[i].Ceiling() * sloops[i];
                var nextTotal = (int)newMachines.Ceiling() * nextLevel;
                var marginalCost = nextTotal - prevTotal;
                if (marginalCost <= 0 || marginalCost > remaining) continue;

                var machinesRemoved = (machines[i] - newMachines).ToDouble();
                if (machinesRemoved <= 0) continue;

                // Resources (default): weight by scarce-raw draw spared. Machines: raw count.
                var score = (bias == PlanBias.Machines ? machinesRemoved : machinesRemoved * (1.0 + intensity[i]))
                    / marginalCost;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                    bestCost = marginalCost;
                    bestMachines = newMachines;
                }
            }

            if (bestIndex < 0) break;
            sloops[bestIndex]++;
            machines[bestIndex] = bestMachines;
            remaining -= bestCost;
        }

        for (var i = 0; i < n; i++)
            if (sloops[i] > 0) levels[recipes[i].Recipe] = sloops[i];
        return levels;
    }

    /// <summary>Scarcity-weighted raw draw of one machine of this recipe — only inputs the
    /// scarcity model prices as raws count (intermediates are produced internally, their
    /// raw cost captured upstream).</summary>
    private static double RawIntensity(RecipeDefinition recipe, Dictionary<string, Rational> weights)
    {
        var sum = 0.0;
        foreach (var input in recipe.Inputs)
            if (weights.TryGetValue(input.Part, out var weight))
                sum += Math.Abs(recipe.RatePerMinute(input.Part).ToDouble()) * weight.ToDouble();
        return sum;
    }
}
