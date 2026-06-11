namespace Ficsit.Schematics.Core.Editing;

public sealed class CommandStack
{
    private readonly List<EditCommand> _undo = [];
    private readonly List<EditCommand> _redo = [];

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(EditCommand command)
    {
        command.Apply();
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
            };
        }
        else
        {
            _undo.Add(command);
        }
        _redo.Clear();
        Changed?.Invoke();
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
