using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Relative cost of one unit/min of each raw resource: scarcer raws cost more
/// (Iron Ore = 1). Defaults reflect the classic map's total extraction caps;
/// when resource nodes have been imported from a save, weights are rebuilt
/// from what that world actually offers — an oil-rich randomized map makes
/// oil-hungry recipes correspondingly cheaper.
/// </summary>
public static class ScarcityWeights
{
    /// <summary>Classic-map total extractable rates (approx., Mk.3 @100%).</summary>
    private static readonly Dictionary<string, Rational> ClassicTotals = new()
    {
        ["Iron Ore"] = new Rational(92100),
        ["Limestone"] = new Rational(69900),
        ["Coal"] = new Rational(42300),
        ["Copper Ore"] = new Rational(36900),
        ["Caterium Ore"] = new Rational(15000),
        ["Raw Quartz"] = new Rational(13500),
        ["Crude Oil"] = new Rational(12600),
        ["Bauxite"] = new Rational(12300),
        ["Nitrogen Gas"] = new Rational(12000),
        ["Sulfur"] = new Rational(10800),
        ["SAM"] = new Rational(10200),
        ["Uranium"] = new Rational(2100),
    };

    /// <summary>Effectively free inputs.</summary>
    private static readonly Rational WaterWeight = new(1, 1000);

    /// <summary>Leaf parts with no node total (biomass, alien drops, …).</summary>
    private static readonly Rational DefaultLeafWeight = new(50);

    public static Dictionary<string, Rational> Build(IReadOnlyList<ResourceNodeInfo>? mapNodes)
    {
        var totals = mapNodes is { Count: > 0 } ? TotalsFromMap(mapNodes) : ClassicTotals;
        var iron = totals.GetValueOrDefault("Iron Ore", Rational.One);
        if (!iron.IsPositive) iron = totals.Values.Where(v => v.IsPositive).DefaultIfEmpty(Rational.One).Max();

        var weights = new Dictionary<string, Rational>();
        foreach (var name in ClassicTotals.Keys)
        {
            var total = totals.GetValueOrDefault(name, Rational.Zero);
            weights[name] = total.IsPositive ? iron / total : new Rational(1000);
        }
        weights["Water"] = WaterWeight;
        return weights;
    }

    public static Rational WeightFor(Dictionary<string, Rational> weights, string part)
        => weights.GetValueOrDefault(part, DefaultLeafWeight);

    /// <summary>Sum of purity factors per resource over the imported nodes.</summary>
    private static Dictionary<string, Rational> TotalsFromMap(IReadOnlyList<ResourceNodeInfo> nodes)
    {
        var totals = new Dictionary<string, Rational>();
        foreach (var node in nodes)
        {
            if (node.Kind == ResourceNodeKind.Geyser) continue;
            var factor = node.Purity switch
            {
                "Pure" => new Rational(2),
                "Impure" => new Rational(1, 2),
                _ => Rational.One,
            };
            // Normalize so a classic-ish map lands near the classic totals
            // (one normal node ≈ 240/min on a Mk.3 miner).
            totals[node.Part] = totals.GetValueOrDefault(node.Part, Rational.Zero) + factor * 240;
        }
        return totals;
    }
}
