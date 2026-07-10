using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.Model;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class FactoryEditorTests
{
    [Fact]
    public void GroupIntoOutpost_reparents_and_preserves_flows()
    {
        var editor = new FactoryEditor(TestData.Database);
        var ore = editor.AddNode("Iron Ore", 0, 0);
        editor.SetLimit(ore, "60");
        var ingot = editor.AddNode("Iron Ingot", 100, 0);
        var plate = editor.AddNode("Iron Plate", 200, 0);
        editor.Connect(ore, "Iron Ore", ingot);
        editor.Connect(ingot, "Iron Ingot", plate);

        var before = (editor.Result.For(ore).Count, editor.Result.For(ingot).Count, editor.Result.For(plate).Count);

        var outpost = editor.GroupIntoOutpost([ore, ingot], "Iron Ingot");

        Assert.NotNull(outpost);
        Assert.Equal(NodeKind.Outpost, outpost!.Kind);
        Assert.Same(outpost, ore.Parent);
        Assert.Same(outpost, ingot.Parent);
        Assert.Null(plate.Parent);
        Assert.Equal(4, editor.Graph.Nodes.Count);
        // Reparenting is flow-invariant — the flat solver keeps every real connection.
        var after = (editor.Result.For(ore).Count, editor.Result.For(ingot).Count, editor.Result.For(plate).Count);
        Assert.Equal(before, after);
    }

    [Fact]
    public void GroupIntoOutpost_undo_restores_the_graph()
    {
        var editor = new FactoryEditor(TestData.Database);
        var ore = editor.AddNode("Iron Ore", 0, 0);
        var ingot = editor.AddNode("Iron Ingot", 100, 0);
        editor.Connect(ore, "Iron Ore", ingot);

        editor.GroupIntoOutpost([ore, ingot], "Iron Ingot");
        Assert.Equal(3, editor.Graph.Nodes.Count);

        editor.Commands.Undo();
        Assert.Equal(2, editor.Graph.Nodes.Count);
        Assert.Null(ore.Parent);
        Assert.Null(ingot.Parent);
    }


    [Fact]
    public void SuspendSolve_collapses_a_bulk_edit_into_one_solve()
    {
        var editor = new FactoryEditor(TestData.Database);
        var solves = 0;
        editor.Solved += () => solves++;

        using (editor.SuspendSolve())
        {
            var ore = editor.AddNode("Iron Ore", 0, 0);
            var ingot = editor.AddNode("Iron Ingot", 0, 0);
            editor.SetLimit(ore, "60");
            editor.Connect(ore, "Iron Ore", ingot);
        }

        // One solve for the whole batch, not one per edit — this is what keeps
        // applying a dense plan from freezing the UI.
        Assert.Equal(1, solves);
        Assert.Equal(2, editor.Graph.Nodes.Count);
        Assert.Single(editor.Graph.Connections);
    }

    [Fact]
    public void Edits_outside_a_suspension_each_solve()
    {
        var editor = new FactoryEditor(TestData.Database);
        var solves = 0;
        editor.Solved += () => solves++;

        editor.AddNode("Iron Ore", 0, 0);
        editor.AddNode("Iron Ingot", 0, 0);

        Assert.Equal(2, solves);
    }

    [Fact]
    public void Nested_edits_after_resume_solve_again()
    {
        var editor = new FactoryEditor(TestData.Database);
        var solves = 0;
        editor.Solved += () => solves++;

        using (editor.SuspendSolve())
            editor.AddNode("Iron Ore", 0, 0);
        Assert.Equal(1, solves);

        editor.AddNode("Iron Ingot", 0, 0);
        Assert.Equal(2, solves); // suspension lifted, normal solving resumes
    }
}
