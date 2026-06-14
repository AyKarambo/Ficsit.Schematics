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

    /// <summary>The scope being edited: the root canvas or an outpost interior.</summary>
    public FactoryGraph CurrentScope { get; private set; }

    /// <summary>Outpost drill-down trail, root first.</summary>
    public List<FactoryNode> ScopePath { get; } = [];

    public event Action? DocumentReplaced;
    public event Action? Solved;

    /// <summary>Raised after a pure geometry edit (node move): the view should refresh but the
    /// solver result is unchanged, so no <see cref="Solved"/> / re-solve fires.</summary>
    public event Action? GeometryChanged;

    public FactoryEditor(GameDatabase data)
    {
        _data = data;
        CurrentScope = Document.Root;
        Commands.Changed += Resolve;
        Commands.GeometryChanged += () => GeometryChanged?.Invoke();
        Resolve();
    }

    public GameDatabase Data => _data;

    public void LoadDocument(FactoryDocument document)
    {
        Document = document;
        ScopePath.Clear();
        CurrentScope = document.Root;
        Commands.Clear();
        EnsureOutpostBoundaries(); // migrate pre-pass-through saves
        DocumentReplaced?.Invoke();
        Resolve();
    }

    public void EnterOutpost(FactoryNode outpost)
    {
        if (outpost.Kind is not (NodeKind.Outpost or NodeKind.Blueprint)) return;
        outpost.Children ??= new FactoryGraph();
        ScopePath.Add(outpost);
        CurrentScope = outpost.Children;
        DocumentReplaced?.Invoke();
    }

    public void LeaveOutpost()
    {
        if (ScopePath.Count == 0) return;
        ScopePath.RemoveAt(ScopePath.Count - 1);
        CurrentScope = ScopePath.Count == 0 ? Document.Root : ScopePath[^1].Children!;
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
        var node = new FactoryNode { Name = name, Kind = kind, X = x, Y = y };

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
        if (kind is NodeKind.Outpost or NodeKind.Blueprint)
            node.Children = new FactoryGraph();

        var scope = CurrentScope;
        Commands.Push(new EditCommand
        {
            Label = $"Add {name}",
            Apply = () => { if (!scope.Nodes.Contains(node)) scope.Nodes.Add(node); },
            Revert = () => scope.RemoveNode(node),
        });
        return node;
    }

    public void DeleteNodes(IReadOnlyList<FactoryNode> nodes)
    {
        if (nodes.Count == 0) return;
        var scope = CurrentScope;
        var removed = nodes.Where(scope.Nodes.Contains).ToList();
        var affectedConnections = scope.Connections
            .Where(c => removed.Contains(c.From) || removed.Contains(c.To))
            .ToList();
        var nodeIndices = removed.Select(n => scope.Nodes.IndexOf(n)).ToList();

        // Deleting an outpost boundary node also drops its container's matching exterior
        // connections (they reference the container in the scope above this one).
        var outpost = ScopePath.Count > 0 ? ScopePath[^1] : null;
        var parentScope = ScopePath.Count == 0 ? null
            : ScopePath.Count == 1 ? Document.Root
            : ScopePath[^2].Children;
        var exteriorRemoved = new List<NodeConnection>();
        if (outpost is not null && parentScope is not null)
            foreach (var node in removed.Where(n => n.Kind is NodeKind.Import or NodeKind.Export))
                exteriorRemoved.AddRange(parentScope.Connections.Where(c =>
                    (node.Kind == NodeKind.Import ? c.To == outpost : c.From == outpost) && c.Part == node.Name));

        Commands.Push(new EditCommand
        {
            Label = removed.Count == 1 ? $"Delete {removed[0].Name}" : $"Delete {removed.Count} machines",
            Apply = () =>
            {
                foreach (var node in removed) scope.Nodes.Remove(node);
                foreach (var connection in affectedConnections) scope.Connections.Remove(connection);
                foreach (var connection in exteriorRemoved) parentScope!.Connections.Remove(connection);
            },
            Revert = () =>
            {
                for (var i = 0; i < removed.Count; i++)
                    scope.Nodes.Insert(Math.Min(nodeIndices[i], scope.Nodes.Count), removed[i]);
                scope.Connections.AddRange(affectedConnections);
                if (exteriorRemoved.Count > 0) parentScope!.Connections.AddRange(exteriorRemoved);
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
        var scope = CurrentScope;
        if (from == to) return false;
        if (scope.Connections.Any(c => c.From == from && c.To == to && c.Part == part)) return false;

        // Wiring to/from an outpost container materializes the matching boundary node so the
        // flow has somewhere to pass through; group it with the connection (unless already in
        // a group — CommandStack doesn't nest).
        var ownGroup = !Commands.InGroup;
        if (ownGroup) Commands.BeginGroup($"Connect {part}");
        EnsureBoundaryFor(to, part, isImport: true);
        EnsureBoundaryFor(from, part, isImport: false);

        var connection = new NodeConnection { From = from, To = to, Part = part };
        Commands.Push(new EditCommand
        {
            Label = $"Connect {part}",
            Apply = () => scope.Connections.Add(connection),
            Revert = () => scope.Connections.Remove(connection),
        });
        if (ownGroup) Commands.EndGroup();
        return true;
    }

    /// <summary>Add an outpost boundary node (Import/Export) in the current interior scope —
    /// the "add from inside" gesture (drag a machine port to empty canvas).</summary>
    public FactoryNode AddBoundaryNode(string part, bool isImport, double x, double y)
    {
        var node = new FactoryNode { Name = part, Kind = isImport ? NodeKind.Import : NodeKind.Export, X = x, Y = y };
        var scope = CurrentScope;
        Commands.Push(new EditCommand
        {
            Label = $"Add {(isImport ? "import" : "export")} {part}",
            Apply = () => { if (!scope.Nodes.Contains(node)) scope.Nodes.Add(node); },
            Revert = () => scope.RemoveNode(node),
        });
        return node;
    }

    private void EnsureBoundaryFor(FactoryNode container, string part, bool isImport)
    {
        if (container.Kind is not (NodeKind.Outpost or NodeKind.Blueprint)) return;
        if (string.IsNullOrEmpty(part) || part == "AnyPart") return;
        container.Children ??= new FactoryGraph();
        var kind = isImport ? NodeKind.Import : NodeKind.Export;
        if (container.Children.Nodes.Any(n => n.Kind == kind && n.Name == part)) return;

        var node = new FactoryNode { Name = part, Kind = kind };
        PlaceBoundary(container.Children, node, isImport);
        var children = container.Children;
        Commands.Push(new EditCommand
        {
            Label = $"Add {(isImport ? "import" : "export")} {part}",
            Apply = () => { if (!children.Nodes.Contains(node)) children.Nodes.Add(node); },
            Revert = () => children.RemoveNode(node),
        });
    }

    private static void PlaceBoundary(FactoryGraph children, FactoryNode node, bool isImport)
    {
        var sameKind = children.Nodes.Count(n => n.Kind == node.Kind);
        if (children.Nodes.Count == 0)
        {
            node.X = isImport ? 0 : 280;
            node.Y = 0;
        }
        else
        {
            node.X = isImport ? children.Nodes.Min(n => n.X) - 160 : children.Nodes.Max(n => n.X) + 160;
            node.Y = sameKind * 80;
        }
    }

    /// <summary>Create boundary nodes for any outpost exterior connections that lack them —
    /// migrates documents made before the pass-through model (idempotent).</summary>
    public void EnsureOutpostBoundaries() => EnsureBoundariesRecursive(Document.Root);

    private static void EnsureBoundariesRecursive(FactoryGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Children is null) continue;
            EnsureBoundariesRecursive(node.Children);
            if (node.Kind is not (NodeKind.Outpost or NodeKind.Blueprint)) continue;

            foreach (var part in graph.IncomingTo(node).Select(c => c.Part).Where(p => p != "AnyPart").Distinct())
                if (!node.Children.Nodes.Any(n => n.Kind == NodeKind.Import && n.Name == part))
                {
                    var boundary = new FactoryNode { Name = part, Kind = NodeKind.Import };
                    PlaceBoundary(node.Children, boundary, true);
                    node.Children.Nodes.Add(boundary);
                }
            foreach (var part in graph.OutgoingFrom(node).Select(c => c.Part).Where(p => p != "AnyPart").Distinct())
                if (!node.Children.Nodes.Any(n => n.Kind == NodeKind.Export && n.Name == part))
                {
                    var boundary = new FactoryNode { Name = part, Kind = NodeKind.Export };
                    PlaceBoundary(node.Children, boundary, false);
                    node.Children.Nodes.Add(boundary);
                }
        }
    }

    public void Disconnect(NodeConnection connection)
    {
        var scope = CurrentScope;
        if (!scope.Connections.Contains(connection)) return;
        Commands.Push(new EditCommand
        {
            Label = $"Disconnect {connection.Part}",
            Apply = () => scope.Connections.Remove(connection),
            Revert = () => scope.Connections.Add(connection),
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

        var scope = CurrentScope;
        var oldName = node.Name;
        var inputParts = recipe.Inputs.Select(p => p.Part).ToHashSet();
        var outputParts = recipe.Outputs.Select(p => p.Part).ToHashSet();
        var dropped = scope.Connections
            .Where(c => (c.To == node && !inputParts.Contains(c.Part))
                || (c.From == node && !outputParts.Contains(c.Part)))
            .ToList();

        Commands.Push(new EditCommand
        {
            Label = $"Switch to {newRecipeName}",
            Apply = () =>
            {
                node.Name = newRecipeName;
                foreach (var connection in dropped) scope.Connections.Remove(connection);
            },
            Revert = () =>
            {
                node.Name = oldName;
                scope.Connections.AddRange(dropped);
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
        var scope = CurrentScope;
        var temp = new FactoryGraph();
        temp.Nodes.AddRange(nodes);
        temp.Connections.AddRange(scope.Connections
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
        }

        var scope = CurrentScope;
        var connections = doc.Root.Connections.ToList();
        Commands.Push(new EditCommand
        {
            Label = "Paste",
            Apply = () =>
            {
                foreach (var node in pasted)
                    if (!scope.Nodes.Contains(node)) scope.Nodes.Add(node);
                foreach (var connection in connections)
                    if (!scope.Connections.Contains(connection)) scope.Connections.Add(connection);
            },
            Revert = () =>
            {
                foreach (var node in pasted) scope.RemoveNode(node);
            },
        });
        return pasted;
    }
}
