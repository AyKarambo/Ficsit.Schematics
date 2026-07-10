using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Phase 3 of "import built factories": groups imported machines into outposts by spatial
/// density, so an import is navigable outposts rather than one flat sea of machines (the user's
/// "how do I split factories?" concern). Pure and UI-free — it sets each grouped node's
/// <see cref="FactoryNode.Parent"/> and returns the outpost nodes for the caller to add too.
/// Builds on the flat outpost model (#9): an outpost is just a node, membership is the parent.
/// </summary>
public static class SaveClustering
{
    /// <summary>Default proximity (canvas units = metres) within which machines join one outpost.</summary>
    public const double DefaultRadius = 80;

    /// <summary>A cluster needs at least this many nodes to become an outpost. Clustering runs on
    /// consolidated counted nodes, so two surviving nodes near each other are two *different*
    /// stations of one deliberate mini-factory (a packager + its refinery, "×20" banks included) —
    /// only lone nodes (scattered extractors, single strays) stay loose at the root.</summary>
    private const int MinClusterSize = 2;

    /// <summary>
    /// Cluster <paramref name="nodes"/> by proximity and create one outpost per dense cluster,
    /// reparenting its members. Returns the created outpost nodes (to be added alongside the
    /// machines). Nodes that already have a parent, and machines in sparse areas, are left loose.
    /// </summary>
    public static IReadOnlyList<FactoryNode> GroupByLocation(
        IReadOnlyList<FactoryNode> nodes, GameDatabase data, double radius)
    {
        var loose = nodes.Where(n => n.Parent is null).ToList();
        if (loose.Count == 0 || radius <= 0) return [];

        var outposts = new List<FactoryNode>();
        foreach (var cluster in Cluster(loose, radius))
        {
            if (cluster.Count < MinClusterSize) continue;
            var outpost = new FactoryNode
            {
                Kind = NodeKind.Outpost,
                Name = "Outpost",
                Title = OutpostName(cluster, data),
                X = cluster.Min(n => n.X),
                Y = cluster.Min(n => n.Y),
            };
            foreach (var node in cluster) node.Parent = outpost;
            outposts.Add(outpost);
        }
        return outposts;
    }

    /// <summary>Connected components of the proximity graph (two machines are linked when within
    /// <paramref name="radius"/>), via a grid-accelerated union-find so it scales to large saves.</summary>
    private static List<List<FactoryNode>> Cluster(List<FactoryNode> nodes, double radius)
    {
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        // Bin into cells of side = radius; only same/neighbouring cells can hold points within radius.
        (long, long) Cell(FactoryNode n) => ((long)Math.Floor(n.X / radius), (long)Math.Floor(n.Y / radius));
        var grid = new Dictionary<(long, long), List<int>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var cell = Cell(nodes[i]);
            if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = [];
            bucket.Add(i);
        }

        var r2 = radius * radius;
        for (var i = 0; i < nodes.Count; i++)
        {
            var (cx, cy) = Cell(nodes[i]);
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var bucket))
                        foreach (var j in bucket)
                            if (j > i)
                            {
                                var ex = nodes[i].X - nodes[j].X;
                                var ey = nodes[i].Y - nodes[j].Y;
                                if (ex * ex + ey * ey <= r2) Union(i, j);
                            }
        }

        var byRoot = new Dictionary<int, List<FactoryNode>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var root = Find(i);
            if (!byRoot.TryGetValue(root, out var list)) byRoot[root] = list = [];
            list.Add(nodes[i]);
        }
        return byRoot.Values.ToList();
    }

    /// <summary>Friendly names for common product lines, so a cluster making iron parts reads
    /// "Basic Iron Parts" rather than whichever single part happens to dominate.</summary>
    private static readonly Dictionary<string, string> ProductCategories = new(StringComparer.Ordinal)
    {
        ["Iron Rod"] = "Basic Iron Parts", ["Iron Plate"] = "Basic Iron Parts", ["Screw"] = "Basic Iron Parts",
        ["Reinforced Iron Plate"] = "Advanced Iron Parts", ["Modular Frame"] = "Advanced Iron Parts",
        ["Rotor"] = "Advanced Iron Parts", ["Smart Plating"] = "Advanced Iron Parts",
        ["Wire"] = "Copper Parts", ["Cable"] = "Copper Parts", ["Copper Sheet"] = "Copper Parts",
        ["Steel Beam"] = "Steel Parts", ["Steel Pipe"] = "Steel Parts",
        ["Encased Industrial Beam"] = "Steel Parts",
    };

    /// <summary>
    /// Name a cluster by what it actually makes: the part produced by the most machines that
    /// isn't consumed again inside the cluster (its net product), mapped to a friendly category
    /// when one fits. Weighting by machine count (not node count) means a "Concrete ×10" node
    /// outranks a couple of stray "Screw" nodes.
    /// </summary>
    private static string OutpostName(List<FactoryNode> cluster, GameDatabase data)
    {
        var produced = new Dictionary<string, int>();
        var consumed = new HashSet<string>();
        var generators = 0;
        foreach (var node in cluster)
        {
            if (node.Kind == NodeKind.Generator) { generators++; continue; }
            if (!data.RecipesByName.TryGetValue(node.Name, out var recipe)) continue;
            var count = int.TryParse(node.Max, out var m) ? Math.Max(m, 1) : 1;
            foreach (var output in recipe.Outputs) produced[output.Part] = produced.GetValueOrDefault(output.Part) + count;
            foreach (var input in recipe.Inputs) consumed.Add(input.Part);
        }
        if (produced.Count == 0) return generators > 0 ? "Power" : "Outpost";

        // Prefer net products (made here, not consumed here); fall back to everything produced.
        var net = produced.Where(kv => !consumed.Contains(kv.Key)).ToList();
        var pool = net.Count > 0 ? net : [.. produced];
        var dominant = pool.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        return ProductCategories.GetValueOrDefault(dominant, dominant);
    }
}
