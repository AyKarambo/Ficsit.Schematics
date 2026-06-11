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

    public FactoryEditor(GameDatabase data)
    {
        _data = data;
        CurrentScope = Document.Root;
        Commands.Changed += Resolve;
        Resolve();
    }

    public GameDatabase Data => _data;

    public void LoadDocument(FactoryDocument document)
    {
        Document = document;
        ScopePath.Clear();
        CurrentScope = document.Root;
        Commands.Clear();
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

        Commands.Push(new EditCommand
        {
            Label = removed.Count == 1 ? $"Delete {removed[0].Name}" : $"Delete {removed.Count} machines",
            Apply = () =>
            {
                foreach (var node in removed) scope.Nodes.Remove(node);
                foreach (var connection in affectedConnections) scope.Connections.Remove(connection);
            },
            Revert = () =>
            {
                for (var i = 0; i < removed.Count; i++)
                    scope.Nodes.Insert(Math.Min(nodeIndices[i], scope.Nodes.Count), removed[i]);
                scope.Connections.AddRange(affectedConnections);
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
            Apply = () => { foreach (var n in moved) { n.X += deltaX; n.Y += deltaY; } },
            Revert = () => { foreach (var n in moved) { n.X -= deltaX; n.Y -= deltaY; } },
        });
    }

    public bool Connect(FactoryNode from, string part, FactoryNode to)
    {
        var scope = CurrentScope;
        if (from == to) return false;
        if (scope.Connections.Any(c => c.From == from && c.To == to && c.Part == part)) return false;

        var connection = new NodeConnection { From = from, To = to, Part = part };
        Commands.Push(new EditCommand
        {
            Label = $"Connect {part}",
            Apply = () => scope.Connections.Add(connection),
            Revert = () => scope.Connections.Remove(connection),
        });
        return true;
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

    public void SetClockSpeed(FactoryNode node, Rational clock)
    {
        if (!clock.IsPositive) return;
        if (clock > new Rational(5, 2)) clock = new Rational(5, 2);
        SetProperty(node, "Clock Speed", n => n.ClockSpeed, (n, v) => n.ClockSpeed = v, clock);
    }

    /// <summary>
    /// The popup's − / + steppers: choose the clock speed that makes the solved count
    /// land exactly on the next whole machine (down = round count up, up = round down).
    /// </summary>
    public void StepClockToWholeMachines(FactoryNode node, bool roundCountUp)
    {
        var solved = Result.For(node);
        var count = solved.Count;
        if (!count.IsPositive) return;

        var target = roundCountUp ? count.Ceiling() : count.Floor();
        if (target.IsZero) target = 1;
        var newClock = node.ClockSpeed * count / new Rational(target);
        if (newClock > new Rational(5, 2)) return;
        SetClockSpeed(node, newClock);
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
