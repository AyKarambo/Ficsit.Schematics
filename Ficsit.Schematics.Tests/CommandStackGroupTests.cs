using Ficsit.Schematics.Core.Editing;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// Transaction grouping on the command stack — the mechanism that lets the
/// drag-out-of-a-marker gesture (add machine + snap + purity + connect) undo as one step.
/// </summary>
public class CommandStackGroupTests
{
    private static EditCommand Set(List<string> log, string tag) => new()
    {
        Label = tag,
        Apply = () => log.Add($"+{tag}"),
        Revert = () => log.Add($"-{tag}"),
    };

    [Fact]
    public void Group_collapses_into_one_undo_step()
    {
        var log = new List<string>();
        var stack = new CommandStack();

        stack.BeginGroup("compose");
        stack.Push(Set(log, "a"));
        stack.Push(Set(log, "b"));
        stack.Push(Set(log, "c"));
        stack.EndGroup();

        Assert.True(stack.CanUndo);
        log.Clear();
        stack.Undo();

        // One Undo reverts all three, in reverse order.
        Assert.Equal(new[] { "-c", "-b", "-a" }, log);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void Group_redo_reapplies_all_members_in_order()
    {
        var log = new List<string>();
        var stack = new CommandStack();

        stack.BeginGroup("compose");
        stack.Push(Set(log, "a"));
        stack.Push(Set(log, "b"));
        stack.EndGroup();
        stack.Undo();

        log.Clear();
        stack.Redo();

        Assert.Equal(new[] { "+a", "+b" }, log);
    }

    [Fact]
    public void CancelGroup_reverts_applied_members_and_pushes_nothing()
    {
        var log = new List<string>();
        var stack = new CommandStack();

        stack.BeginGroup("compose");
        stack.Push(Set(log, "a"));
        stack.Push(Set(log, "b"));
        log.Clear();
        stack.CancelGroup();

        Assert.Equal(new[] { "-b", "-a" }, log);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void EndGroup_with_no_members_is_a_no_op()
    {
        var stack = new CommandStack();
        stack.BeginGroup("empty");
        stack.EndGroup();
        Assert.False(stack.CanUndo);
    }
}
