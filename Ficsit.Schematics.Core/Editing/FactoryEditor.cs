using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Serialization;
using Ficsit.Schematics.Core.Solver;

namespace Ficsit.Schematics.Core.Editing;

/// <summary>
/// Orchestrates all mutations of the open document through undoable commands,
/// re-solving after every change. The UI binds to this.
/// </summary>
public sealed class FactoryEditor
{
    private readonly GameDatabase _data;
    private string? _clipboard;

    public FactoryDocument Document { get; private set; } = new();
    public CommandStack Commands { get; } = new();
    public SolveResult Result { get; private set; } = new();

    /// <summary>The single flat graph holding every node and connection.</summary>
    public FactoryGraph Graph => Document.Root;

    /// <summary>The outpost currently focused; null = root canvas. A view filter only —
    /// all nodes live in one flat list and group via <see cref="FactoryNode.Parent"/>.</summary>
    public FactoryNode? ActiveOutpost { get; private set; }

    /// <summary>Nodes visible in the current scope = direct members of the active outpost.</summary>
    public IEnumerable<FactoryNode> VisibleNodes => Document.Root.MembersOf(ActiveOutpost);

    /// <summary>The active outpost and its ancestor chain (root last) — for breadcrumbs.</summary>
    public IReadOnlyList<FactoryNode> ScopePath
    {
        get
        {
            var path = new List<FactoryNode>();
            for (var o = ActiveOutpost; o is not null; o = o.Parent) path.Add(o);
            path.Reverse();
            return path;
        }
    }

    public event Action? DocumentReplaced;
    public event Action? Solved;

    /// <summary>Raised after a pure geometry edit (node move): the view should refresh but the
    /// solver result is unchanged, so no <see cref="Solved"/> / re-solve fires.</summary>
    public event Action? GeometryChanged;

    public FactoryEditor(GameDatabase data)
    {
        _data = data;
        Commands.Changed += Resolve;
        Commands.GeometryChanged += () => GeometryChanged?.Invoke();
        Resolve();
    }

    public GameDatabase Data => _data;

    public void LoadDocument(FactoryDocument document)
    {
        Document = document;
        ActiveOutpost = null;
        Commands.Clear();
        DocumentReplaced?.Invoke();
        Resolve();
    }

    /// <summary>Focus an outpost (its members fill the canvas).</summary>
    public void EnterOutpost(FactoryNode outpost)
    {
        if (outpost.Kind is not (NodeKind.Outpost or NodeKind.Blueprint)) return;
        ActiveOutpost = outpost;
        DocumentReplaced?.Invoke();
    }

    /// <summary>Go up one level (to the containing outpost, or root).</summary>
    public void LeaveOutpost()
    {
        if (ActiveOutpost is null) return;
        ActiveOutpost = ActiveOutpost.Parent;
        DocumentReplaced?.Invoke();
    }

    public void Resolve()
    {
        Result = SolverFactory.Create(Document.Solver, _data).Solve(Document);
        Solved?.Invoke();
    }

    // ------------------------------------------------------------------- edits

    public FactoryNode AddNode(string name, double x, double y)
    {
        var kind = SfmdSerializer.KindFor(name);
        var node = new FactoryNode { Name = name, Kind = kind, X = x, Y = y, Parent = ActiveOutpost };

        if (kind == NodeKind.Recipe
            && _data.RecipesByName.TryGetValue(name, out var recipe))
        {
            var family = _data.MultiMachinesByName.GetValueOrDefault(recipe.Machine)
                ?? _data.MultiMachineFor(recipe.Machine);
            if (family is not null)
            {
                if (!string.IsNullOrEmpty(family.DefaultMax)) node.Max = family.DefaultMax;
                node.AutoRound = family.AutoRound;
            }
        }

        var graph = Document.Root;
        Commands.Push(new EditCommand
        {
            Label = $"Add {name}",
            Apply = () => { if (!graph.Nodes.Contains(node)) graph.Nodes.Add(node); },
            Revert = () => graph.RemoveNode(node),
        });
        return node;
    }

    public void DeleteNodes(IReadOnlyList<FactoryNode> nodes)
    {
        if (nodes.Count == 0) return;
        var graph = Document.Root;

        // Deleting an outpost also deletes everything inside it (its descendants).
        var removeSet = new HashSet<FactoryNode>();
        void Collect(FactoryNode n)
        {
            if (!graph.Nodes.Contains(n) || !removeSet.Add(n)) return;
            foreach (var child in graph.Nodes.Where(m => m.Parent == n).ToList())
                Collect(child);
        }
        foreach (var n in nodes) Collect(n);

        var removed = graph.Nodes.Where(removeSet.Contains).ToList();
        var affectedConnections = graph.Connections
            .Where(c => removeSet.Contains(c.From) || removeSet.Contains(c.To))
            .ToList();
        var nodeIndices = removed.Select(n => graph.Nodes.IndexOf(n)).ToList();

        Commands.Push(new EditCommand
        {
            Label = removed.Count == 1 ? $"Delete {removed[0].Name}" : $"Delete {removed.Count} machines",
            Apply = () =>
            {
                foreach (var node in removed) graph.Nodes.Remove(node);
                foreach (var connection in affectedConnections) graph.Connections.Remove(connection);
            },
            Revert = () =>
            {
                for (var i = 0; i < removed.Count; i++)
                    graph.Nodes.Insert(Math.Min(nodeIndices[i], graph.Nodes.Count), removed[i]);
                graph.Connections.AddRange(affectedConnections);
            },
        });
    }

    public void MoveNodes(IReadOnlyList<FactoryNode> nodes, double deltaX, double deltaY, bool coalesce = true)
    {
        if (nodes.Count == 0 || (deltaX == 0 && deltaY == 0)) return;
        var moved = nodes.ToList();
        Commands.Push(new EditCommand
        {
            Label = "Move",
            CoalesceKey = coalesce ? $"move:{string.Join(',', moved.Select(n => n.Id))}" : null,
            AffectsSolve = false, // position never changes flows — skip the re-solve
            Apply = () => { foreach (var n in moved) { n.X += deltaX; n.Y += deltaY; } },
            Revert = () => { foreach (var n in moved) { n.X -= deltaX; n.Y -= deltaY; } },
        });
    }

    public bool Connect(FactoryNode from, string part, FactoryNode to)
    {
        var graph = Document.Root;
        if (from == to) return false;
        if (graph.Connections.Any(c => c.From == from && c.To == to && c.Part == part)) return false;

        var connection = new NodeConnection { From = from, To = to, Part = part };
        Commands.Push(new EditCommand
        {
            Label = $"Connect {part}",
            Apply = () => graph.Connections.Add(connection),
            Revert = () => graph.Connections.Remove(connection),
        });
        return true;
    }

    public void Disconnect(NodeConnection connection)
    {
        var graph = Document.Root;
        if (!graph.Connections.Contains(connection)) return;
        Commands.Push(new EditCommand
        {
            Label = $"Disconnect {connection.Part}",
            Apply = () => graph.Connections.Remove(connection),
            Revert = () => graph.Connections.Add(connection),
        });
    }

    /// <summary>
    /// Swap a recipe node to another recipe of the same machine (e.g. switch a
    /// Fuel-Powered Generator between Fuel/Turbofuel/Rocket Fuel/…). Connections
    /// whose part the new recipe cannot carry are dropped, all in one undo step.
    /// </summary>
    public void SwitchRecipe(FactoryNode node, string newRecipeName)
    {
        if (node.Kind != NodeKind.Recipe || node.Name == newRecipeName) return;
        if (!_data.RecipesByName.TryGetValue(newRecipeName, out var recipe)) return;

        var graph = Document.Root;
        var oldName = node.Name;
        var inputParts = recipe.Inputs.Select(p => p.Part).ToHashSet();
        var outputParts = recipe.Outputs.Select(p => p.Part).ToHashSet();
        var dropped = graph.Connections
            .Where(c => (c.To == node && !inputParts.Contains(c.Part))
                || (c.From == node && !outputParts.Contains(c.Part)))
            .ToList();

        Commands.Push(new EditCommand
        {
            Label = $"Switch to {newRecipeName}",
            Apply = () =>
            {
                node.Name = newRecipeName;
                foreach (var connection in dropped) graph.Connections.Remove(connection);
            },
            Revert = () =>
            {
                node.Name = oldName;
                graph.Connections.AddRange(dropped);
            },
        });
    }

    public void SetProperty<T>(FactoryNode node, string label, Func<FactoryNode, T> getter, Action<FactoryNode, T> setter, T newValue)
    {
        var oldValue = getter(node);
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        Commands.Push(new EditCommand
        {
            Label = label,
            Apply = () => setter(node, newValue),
            Revert = () => setter(node, oldValue),
        });
    }

    public void SetLimit(FactoryNode node, string? max)
        => SetProperty(node, "Limit", n => n.Max, (n, v) => n.Max = v,
            string.IsNullOrWhiteSpace(max) ? null : max.Trim());

    /// <summary>Persist a drag-to-reorder of a node's input or output ports (undoable).</summary>
    public void SetPortOrder(FactoryNode node, bool isInput, List<string> order)
        => SetProperty(node, "Reorder ports",
            n => isInput ? n.InputOrder : n.OutputOrder,
            (n, v) => { if (isInput) n.InputOrder = v; else n.OutputOrder = v; },
            order);

    public void SetClockSpeed(FactoryNode node, Rational clock)
    {
        if (!clock.IsPositive) return;
        if (clock > FactoryNode.MaxClockSpeed) clock = FactoryNode.MaxClockSpeed;
        SetProperty(node, "Clock Speed", n => n.ClockSpeed, (n, v) => n.ClockSpeed = v, clock);
    }

    /// <summary>
    /// Auto-Round machine stepper: change the physical machine count by <paramref name="delta"/>
    /// (− = one more machine, + = one fewer) while holding the node's throughput constant.
    /// The clock rebalances to W/N'. A count-display <c>Max</c> pins the count independent of
    /// the clock, so it is moved to N' too (one undo step) — otherwise stepping would shift
    /// the output instead of the count. ppm-display limits already scale with the clock, and
    /// unlimited nodes follow the clock, so both need only the clock change.
    /// </summary>
    public void StepAutoRound(FactoryNode node, int delta)
    {
        var solved = Result.For(node);
        // Workload W (machine-equivalents at 100%) and the current whole count.
        var (workload, count) = solved.EffectiveClock is { } effective
            ? (solved.Count * effective, solved.Count)
            : (solved.Count * node.ClockSpeed, new Rational(solved.Count.Ceiling()));
        var target = count + new Rational(delta);
        if (!workload.IsPositive || !target.IsPositive) return;

        var newClock = workload / target;
        if (newClock <= FactoryNode.MinClockSpeed || newClock > FactoryNode.MaxClockSpeed) return;

        if (node.Max is { Length: > 0 } && !solved.IsPpmDisplay)
        {
            Commands.BeginGroup("Step machines");
            SetLimit(node, target.ToString());
            SetClockSpeed(node, newClock);
            Commands.EndGroup();
        }
        else
        {
            SetClockSpeed(node, newClock);
        }
    }

    // --------------------------------------------------------------- clipboard

    public void Copy(IReadOnlyList<FactoryNode> nodes)
    {
        if (nodes.Count == 0) return;
        var graph = Document.Root;
        var temp = new FactoryGraph();
        temp.Nodes.AddRange(nodes);
        temp.Connections.AddRange(graph.Connections
            .Where(c => nodes.Contains(c.From) && nodes.Contains(c.To)));
        var doc = new FactoryDocument { Root = temp };
        _clipboard = SfmdSerializer.Serialize(doc);
    }

    public void Cut(IReadOnlyList<FactoryNode> nodes)
    {
        Copy(nodes);
        DeleteNodes(nodes);
    }

    public bool CanPaste => _clipboard is not null;

    public IReadOnlyList<FactoryNode> Paste(double x, double y)
    {
        if (_clipboard is null) return [];
        var doc = SfmdSerializer.Deserialize(_clipboard);
        var pasted = doc.Root.Nodes;
        if (pasted.Count == 0) return [];

        var minX = pasted.Min(n => n.X);
        var minY = pasted.Min(n => n.Y);
        foreach (var node in pasted)
        {
            node.X += x - minX;
            node.Y += y - minY;
            // Top-level pasted nodes land in the current scope; members keep their relative parent.
            if (node.Parent is null) node.Parent = ActiveOutpost;
        }

        var graph = Document.Root;
        var connections = doc.Root.Connections.ToList();
        Commands.Push(new EditCommand
        {
            Label = "Paste",
            Apply = () =>
            {
                foreach (var node in pasted)
                    if (!graph.Nodes.Contains(node)) graph.Nodes.Add(node);
                foreach (var connection in connections)
                    if (!graph.Connections.Contains(connection)) graph.Connections.Add(connection);
            },
            Revert = () =>
            {
                foreach (var node in pasted) graph.RemoveNode(node);
            },
        });
        return pasted;
    }
}
