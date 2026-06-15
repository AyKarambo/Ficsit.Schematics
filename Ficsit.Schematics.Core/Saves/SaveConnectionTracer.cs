namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Traces the raw component→component graph (<see cref="SaveWorld.ComponentLinks"/>) into
/// machine→machine edges: from each real machine's output port, follow the wiring through
/// transport — belts and lifts pass straight through, splitters/mergers are junctions that route
/// to all their ports — until it reaches another real machine's input port. Manifolds and
/// load-balancers are just transport, so they collapse into direct producer→consumer edges. Pure.
/// </summary>
public static class SaveConnectionTracer
{
    /// <summary>Distinct producer-machine → consumer-machine instance pairs implied by the wiring.
    /// <paramref name="isRealMachine"/> tells a modelled machine (a placed node) from transport.</summary>
    public static IReadOnlyList<(string From, string To)> MachineEdges(
        IReadOnlyDictionary<string, string> links, Func<string, bool> isRealMachine)
    {
        // Undirected adjacency over component instances.
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Link(string a, string b)
        {
            (adj.TryGetValue(a, out var sa) ? sa : adj[a] = new(StringComparer.Ordinal)).Add(b);
            (adj.TryGetValue(b, out var sb) ? sb : adj[b] = new(StringComparer.Ordinal)).Add(a);
        }
        foreach (var (a, b) in links) Link(a, b);

        // Interconnect every transport machine's own components: a belt/lift passes through, a
        // splitter/merger routes among all its ports. Real machines stay terminals (not joined),
        // so a trace stops when it reaches one.
        foreach (var group in adj.Keys.GroupBy(MachineOf).ToList())
        {
            if (isRealMachine(ClassOf(group.Key))) continue;
            var comps = group.ToList();
            for (var i = 0; i < comps.Count; i++)
                for (var j = i + 1; j < comps.Count; j++)
                    Link(comps[i], comps[j]);
        }

        var edges = new HashSet<(string, string)>();
        foreach (var start in adj.Keys.ToList())
        {
            if (!IsOutput(start)) continue;
            var source = MachineOf(start);
            if (!isRealMachine(ClassOf(source))) continue;

            var seen = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>(adj[start]);
            foreach (var c in adj[start]) seen.Add(c);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                var machine = MachineOf(node);
                if (isRealMachine(ClassOf(machine)))
                {
                    if (machine != source && IsInput(node)) edges.Add((source, machine));
                    continue; // real machines are terminals — never traverse through them
                }
                foreach (var next in adj[node]) if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        return edges.ToList();
    }

    /// <summary>The owning machine instance of a component path (drop the trailing ".Port").</summary>
    public static string MachineOf(string component)
    {
        var dot = component.LastIndexOf('.');
        return dot < 0 ? component : component[..dot];
    }

    /// <summary>The build class of a machine instance (e.g. "Build_ConstructorMk1_C" from
    /// "…PersistentLevel.Build_ConstructorMk1_C_2146698887").</summary>
    public static string ClassOf(string machineInstance)
    {
        var dot = machineInstance.LastIndexOf('.');
        var name = dot < 0 ? machineInstance : machineInstance[(dot + 1)..];
        var ci = name.IndexOf("_C_", StringComparison.Ordinal);
        return ci >= 0 ? name[..(ci + 2)] : name;
    }

    private static bool IsOutput(string component) => component.Contains("Output", StringComparison.Ordinal);
    private static bool IsInput(string component) => component.Contains("Input", StringComparison.Ordinal);
}
