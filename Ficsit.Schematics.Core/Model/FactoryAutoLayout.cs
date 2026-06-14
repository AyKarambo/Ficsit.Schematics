namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// Layered ("Sugiyama-style") graph tidy. Producers sit to the left of what they
/// feed; within a column nodes are ordered to pull connected machines together and
/// cut crossing wires. Pipeline:
/// <list type="number">
/// <item><b>Islands → bands.</b> Fully-disconnected sub-graphs are laid out
///   separately and stacked as horizontal bands; one connected plan is a single
///   band, so this is purely additive.</item>
/// <item><b>Cycle breaking.</b> A DFS finds back-edges; layering runs on the
///   remaining DAG, so recycle loops (recycled plastic/rubber, aluminum scrap, …)
///   can't shove nodes off to the far right.</item>
/// <item><b>Layering (ALAP).</b> Each node is placed as late as possible — right
///   before what it feeds — so feeders hug their consumers and wires stay short.</item>
/// <item><b>Crossing reduction</b> (barycenter sweeps) and <b>coordinate
///   assignment</b> (slide toward neighbors, keep a min row gap).</item>
/// </list>
/// Only connections <em>within</em> the set count. UI-free: returns target
/// positions; the caller applies them.
/// </summary>
public static class FactoryAutoLayout
{
    public const double ColumnGap = 240;
    public const double RowGap = 180;
    /// <summary>Extra vertical space inserted between independent islands.</summary>
    public const double BandGap = 160;
    private const int OrderingSweeps = 8;
    private const int CoordinateSweeps = 6;

    public static Dictionary<FactoryNode, (double X, double Y)> Arrange(
        IReadOnlyList<FactoryNode> nodes, FactoryGraph graph, double originX, double originY)
    {
        var result = new Dictionary<FactoryNode, (double X, double Y)>();
        if (nodes.Count == 0) return result;

        var set = new HashSet<FactoryNode>(nodes);
        var outAdj = new Dictionary<FactoryNode, List<FactoryNode>>();
        var inAdj = new Dictionary<FactoryNode, List<FactoryNode>>();
        foreach (var node in nodes) { outAdj[node] = []; inAdj[node] = []; }
        foreach (var c in graph.Connections)
            if (c.From != c.To && set.Contains(c.From) && set.Contains(c.To))
            {
                outAdj[c.From].Add(c.To);
                inAdj[c.To].Add(c.From);
            }

        // Each independent island becomes a band, stacked top-to-bottom.
        var bandTop = originY;
        foreach (var component in Components(nodes, outAdj, inAdj))
        {
            var local = LayoutComponent(component, outAdj, inAdj, originX);
            var minY = double.MaxValue;
            var maxY = double.MinValue;
            foreach (var (_, p) in local) { if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y; }
            foreach (var (node, p) in local) result[node] = (p.X, bandTop + (p.Y - minY));
            bandTop += (maxY - minY) + RowGap + BandGap;
        }
        return result;
    }

    /// <summary>Weakly-connected components, each in a stable order (topmost first).</summary>
    private static List<List<FactoryNode>> Components(
        IReadOnlyList<FactoryNode> nodes,
        Dictionary<FactoryNode, List<FactoryNode>> outAdj,
        Dictionary<FactoryNode, List<FactoryNode>> inAdj)
    {
        var seen = new HashSet<FactoryNode>();
        var components = new List<List<FactoryNode>>();
        foreach (var start in Stable(nodes))
        {
            if (!seen.Add(start)) continue;
            var component = new List<FactoryNode>();
            var queue = new Queue<FactoryNode>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                component.Add(node);
                foreach (var m in outAdj[node]) if (seen.Add(m)) queue.Enqueue(m);
                foreach (var m in inAdj[node]) if (seen.Add(m)) queue.Enqueue(m);
            }
            components.Add(component);
        }
        return components;
    }

    private static Dictionary<FactoryNode, (double X, double Y)> LayoutComponent(
        List<FactoryNode> nodes,
        Dictionary<FactoryNode, List<FactoryNode>> outAdj,
        Dictionary<FactoryNode, List<FactoryNode>> inAdj,
        double originX)
    {
        // 1. Break cycles so layering runs on a DAG.
        var back = BackEdges(nodes, outAdj);
        var fwdOut = new Dictionary<FactoryNode, List<FactoryNode>>();
        var fwdIn = new Dictionary<FactoryNode, List<FactoryNode>>();
        foreach (var node in nodes) { fwdOut[node] = []; fwdIn[node] = []; }
        foreach (var node in nodes)
            foreach (var consumer in outAdj[node])
                if (!back.Contains((node, consumer)))
                {
                    fwdOut[node].Add(consumer);
                    fwdIn[consumer].Add(node);
                }

        // 2. Layer ALAP — each node as late as possible (next to what it feeds).
        var asap = LongestPath(nodes, fwdIn, forward: true, 0);
        var maxLayer = 0;
        foreach (var v in asap.Values) if (v > maxLayer) maxLayer = v;
        var alap = LongestPath(nodes, fwdOut, forward: false, maxLayer);
        var minAlap = int.MaxValue;
        foreach (var v in alap.Values) if (v < minAlap) minAlap = v;
        var layer = new Dictionary<FactoryNode, int>();
        foreach (var node in nodes) layer[node] = alap[node] - minAlap;
        maxLayer = 0;
        foreach (var v in layer.Values) if (v > maxLayer) maxLayer = v;

        var columns = new List<List<FactoryNode>>();
        for (var i = 0; i <= maxLayer; i++) columns.Add([]);
        foreach (var node in Stable(nodes)) columns[layer[node]].Add(node);

        var index = new Dictionary<FactoryNode, int>();
        void Reindex()
        {
            foreach (var column in columns)
                for (var i = 0; i < column.Count; i++) index[column[i]] = i;
        }
        Reindex();

        // 3. Crossing reduction (all edges, incl. cycle back-edges, guide positioning).
        for (var sweep = 0; sweep < OrderingSweeps; sweep++)
        {
            if (sweep % 2 == 0)
                for (var l = 1; l <= maxLayer; l++) SortByBarycenter(columns[l], inAdj, index);
            else
                for (var l = maxLayer - 1; l >= 0; l--) SortByBarycenter(columns[l], outAdj, index);
            Reindex();
        }

        // 4. Coordinate assignment.
        var y = new Dictionary<FactoryNode, double>();
        foreach (var column in columns)
            for (var i = 0; i < column.Count; i++) y[column[i]] = i * RowGap;
        for (var sweep = 0; sweep < CoordinateSweeps; sweep++)
            foreach (var column in columns)
            {
                foreach (var node in column)
                {
                    double sum = 0;
                    var count = 0;
                    foreach (var m in inAdj[node]) { sum += y[m]; count++; }
                    foreach (var m in outAdj[node]) { sum += y[m]; count++; }
                    if (count > 0) y[node] = sum / count;
                }
                for (var i = 1; i < column.Count; i++)
                    if (y[column[i]] < y[column[i - 1]] + RowGap)
                        y[column[i]] = y[column[i - 1]] + RowGap;
            }

        var positions = new Dictionary<FactoryNode, (double X, double Y)>();
        foreach (var node in nodes)
            positions[node] = (originX + layer[node] * ColumnGap, y[node]);
        return positions;
    }

    /// <summary>Longest-path layer over a DAG. forward: layer = max(predecessor)+1 from 0
    /// (ASAP). backward: layer = min(successor)−1 seeded at <paramref name="seed"/> (ALAP).</summary>
    private static Dictionary<FactoryNode, int> LongestPath(
        List<FactoryNode> nodes, Dictionary<FactoryNode, List<FactoryNode>> neighborsOf, bool forward, int seed)
    {
        var layer = new Dictionary<FactoryNode, int>();
        foreach (var node in nodes) layer[node] = forward ? 0 : seed;
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var node in nodes)
                foreach (var neighbor in neighborsOf[node])
                    if (forward)
                    {
                        if (layer[neighbor] + 1 > layer[node]) { layer[node] = layer[neighbor] + 1; changed = true; }
                    }
                    else
                    {
                        if (layer[neighbor] - 1 < layer[node]) { layer[node] = layer[neighbor] - 1; changed = true; }
                    }
            if (!changed) break;
        }
        return layer;
    }

    /// <summary>Iterative DFS back-edges (edge to a node still on the DFS stack).</summary>
    private static HashSet<(FactoryNode, FactoryNode)> BackEdges(
        List<FactoryNode> nodes, Dictionary<FactoryNode, List<FactoryNode>> outAdj)
    {
        var back = new HashSet<(FactoryNode, FactoryNode)>();
        var visited = new HashSet<FactoryNode>();
        var onStack = new HashSet<FactoryNode>();
        var cursor = new Dictionary<FactoryNode, int>();
        var stack = new Stack<FactoryNode>();
        foreach (var root in Stable(nodes))
        {
            if (visited.Contains(root)) continue;
            stack.Push(root); visited.Add(root); onStack.Add(root); cursor[root] = 0;
            while (stack.Count > 0)
            {
                var node = stack.Peek();
                var successors = outAdj[node];
                if (cursor[node] < successors.Count)
                {
                    var s = successors[cursor[node]++];
                    if (onStack.Contains(s)) back.Add((node, s));
                    else if (visited.Add(s)) { onStack.Add(s); cursor[s] = 0; stack.Push(s); }
                }
                else { onStack.Remove(node); stack.Pop(); }
            }
        }
        return back;
    }

    private static void SortByBarycenter(
        List<FactoryNode> column,
        Dictionary<FactoryNode, List<FactoryNode>> neighborsOf,
        Dictionary<FactoryNode, int> index)
    {
        if (column.Count < 2) return;
        var key = new Dictionary<FactoryNode, double>();
        for (var i = 0; i < column.Count; i++)
        {
            var node = column[i];
            var neighbors = neighborsOf[node];
            if (neighbors.Count == 0) { key[node] = i; continue; }
            double sum = 0;
            foreach (var m in neighbors) sum += index[m];
            key[node] = sum / neighbors.Count;
        }
        var ordered = column
            .Select((node, i) => (node, i))
            .OrderBy(t => key[t.node]).ThenBy(t => t.i)
            .Select(t => t.node)
            .ToList();
        column.Clear();
        column.AddRange(ordered);
    }

    private static IEnumerable<FactoryNode> Stable(IEnumerable<FactoryNode> nodes)
        => nodes.OrderBy(n => n.Y).ThenBy(n => n.X).ThenBy(n => n.Name);
}
