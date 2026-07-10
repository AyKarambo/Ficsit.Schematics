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
    public void LoadDocument_restores_the_saved_active_outpost()
    {
        var doc = new FactoryDocument();
        var outpost = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost };
        var inner = new FactoryNode { Name = "Iron Ingot", Parent = outpost };
        doc.Root.Nodes.AddRange([outpost, inner]);
        doc.ActiveOutpost = outpost;

        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(doc);

        Assert.Same(outpost, editor.ActiveOutpost);
        Assert.Equal([outpost], editor.ScopePath);      // breadcrumb shows the outpost
        Assert.Contains(inner, editor.VisibleNodes);    // its members fill the canvas

        editor.LeaveOutpost();                          // back affordance returns to root
        Assert.Null(editor.ActiveOutpost);
        Assert.Null(doc.ActiveOutpost);                 // and the document tracks it
    }

    [Fact]
    public void LoadDocument_falls_back_to_root_for_a_stale_active_outpost()
    {
        // Not in the graph at all.
        var doc = new FactoryDocument();
        doc.Root.Nodes.Add(new FactoryNode { Name = "Iron Ingot" });
        doc.ActiveOutpost = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost };

        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(doc);
        Assert.Null(editor.ActiveOutpost);

        // In the graph, but not an outpost/blueprint.
        var doc2 = new FactoryDocument();
        var machine = new FactoryNode { Name = "Iron Ingot" };
        doc2.Root.Nodes.Add(machine);
        doc2.ActiveOutpost = machine;

        editor.LoadDocument(doc2);
        Assert.Null(editor.ActiveOutpost);
    }

    [Fact]
    public void Entering_an_outpost_marks_it_on_the_document()
    {
        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(new FactoryDocument());
        var outpost = editor.AddNode("Outpost", 0, 0);

        editor.EnterOutpost(outpost);
        Assert.Same(outpost, editor.Document.ActiveOutpost); // save/reload will restore it
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
