namespace Ficsit.Schematics.Core.Editing;

public sealed class CommandStack
{
    private readonly List<EditCommand> _undo = [];
    private readonly List<EditCommand> _redo = [];

    // Open transaction: commands collected here collapse into one undo step on EndGroup.
    private List<EditCommand>? _group;
    private string? _groupLabel;

    /// <summary>Raised when a command that affects solver results is applied/undone.</summary>
    public event Action? Changed;

    /// <summary>Raised for pure geometry edits (node moves) — the view should refresh but the
    /// graph need not be re-solved.</summary>
    public event Action? GeometryChanged;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True while a transaction is open (so callers can avoid nesting groups).</summary>
    public bool InGroup => _group is not null;

    /// <summary>
    /// Begin a transaction: every <see cref="Push"/> until <see cref="EndGroup"/>
    /// applies immediately but collapses into a single undo step. Nesting is not
    /// supported (calls while a group is open are ignored).
    /// </summary>
    public void BeginGroup(string label)
    {
        if (_group is not null) return;
        _group = [];
        _groupLabel = label;
    }

    /// <summary>
    /// Close the transaction opened by <see cref="BeginGroup"/>, pushing the collected
    /// commands as one composite undo step. A no-op when nothing was pushed.
    /// </summary>
    public void EndGroup()
    {
        var group = _group;
        var label = _groupLabel;
        _group = null;
        _groupLabel = null;
        if (group is null || group.Count == 0) return;

        var commands = group.ToList();
        _undo.Add(new EditCommand
        {
            Label = label ?? commands[^1].Label,
            Apply = () => { foreach (var c in commands) c.Apply(); },
            Revert = () => { for (var i = commands.Count - 1; i >= 0; i--) commands[i].Revert(); },
        });
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Discard an open transaction, reverting whatever it applied so far.</summary>
    public void CancelGroup()
    {
        var group = _group;
        _group = null;
        _groupLabel = null;
        if (group is null) return;
        for (var i = group.Count - 1; i >= 0; i--) group[i].Revert();
        Changed?.Invoke();
    }

    public void Push(EditCommand command)
    {
        command.Apply();
        if (_group is not null)
        {
            _group.Add(command);
            Changed?.Invoke();
            return;
        }
        if (command.CoalesceKey is not null
            && _undo.Count > 0
            && _undo[^1].CoalesceKey == command.CoalesceKey)
        {
            var previous = _undo[^1];
            _undo[^1] = new EditCommand
            {
                Label = command.Label,
                Apply = () => { previous.Apply(); command.Apply(); },
                Revert = () => { command.Revert(); previous.Revert(); },
                CoalesceKey = command.CoalesceKey,
                AffectsSolve = command.AffectsSolve,
            };
        }
        else
        {
            _undo.Add(command);
        }
        _redo.Clear();
        // Pure geometry edits skip the re-solve (smooth dragging); everything else solves.
        if (command.AffectsSolve) Changed?.Invoke();
        else GeometryChanged?.Invoke();
    }

    /// <summary>Stops further coalescing into the latest step (e.g. at drag end).</summary>
    public void BreakCoalescing()
    {
        if (_undo.Count > 0 && _undo[^1].CoalesceKey is not null)
            _undo[^1] = new EditCommand
            {
                Label = _undo[^1].Label,
                Apply = _undo[^1].Apply,
                Revert = _undo[^1].Revert,
                CoalesceKey = null,
            };
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Revert();
        _redo.Add(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Apply();
        _undo.Add(command);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
