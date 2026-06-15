using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveConnectionTracerTests
{
    private const string P = "Persistent_Level:PersistentLevel.";

    // A bidirectional component link (factory connections are mutual).
    private static void Wire(Dictionary<string, string> links, string a, string b)
    {
        links[a] = b;
        links[b] = a;
    }

    [Fact]
    public void Traces_a_belt_run_into_one_machine_edge()
    {
        var links = new Dictionary<string, string>();
        Wire(links, $"{P}Build_MinerMk1_C_1.Output0", $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny1", $"{P}Build_SmelterMk1_C_3.Input0");

        var edges = SaveConnectionTracer.MachineEdges(links, SaveImport.IsModelledMachine);

        var edge = Assert.Single(edges);
        Assert.Equal($"{P}Build_MinerMk1_C_1", edge.From);
        Assert.Equal($"{P}Build_SmelterMk1_C_3", edge.To);
    }

    [Fact]
    public void A_splitter_manifold_fans_one_source_out_to_each_machine()
    {
        // Smelter → belt → splitter → two belts → two constructors.
        var links = new Dictionary<string, string>();
        Wire(links, $"{P}Build_SmelterMk1_C_1.Output0", $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny1", $"{P}Build_ConveyorAttachmentSplitter_C_3.Input0");
        Wire(links, $"{P}Build_ConveyorAttachmentSplitter_C_3.Output0", $"{P}Build_ConveyorBeltMk1_C_4.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorAttachmentSplitter_C_3.Output1", $"{P}Build_ConveyorBeltMk1_C_5.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_4.ConveyorAny1", $"{P}Build_ConstructorMk1_C_6.Input0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_5.ConveyorAny1", $"{P}Build_ConstructorMk1_C_7.Input0");

        var edges = SaveConnectionTracer.MachineEdges(links, SaveImport.IsModelledMachine);

        Assert.Equal(2, edges.Count);
        Assert.Contains(($"{P}Build_SmelterMk1_C_1", $"{P}Build_ConstructorMk1_C_6"), edges);
        Assert.Contains(($"{P}Build_SmelterMk1_C_1", $"{P}Build_ConstructorMk1_C_7"), edges);
    }

    [Fact]
    public void Does_not_trace_through_a_real_machine()
    {
        // Miner → Smelter → Constructor: a Smelter is a terminal, so the miner does NOT connect
        // straight to the constructor.
        var links = new Dictionary<string, string>();
        Wire(links, $"{P}Build_MinerMk1_C_1.Output0", $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_2.ConveyorAny1", $"{P}Build_SmelterMk1_C_3.Input0");
        Wire(links, $"{P}Build_SmelterMk1_C_3.Output0", $"{P}Build_ConveyorBeltMk1_C_4.ConveyorAny0");
        Wire(links, $"{P}Build_ConveyorBeltMk1_C_4.ConveyorAny1", $"{P}Build_ConstructorMk1_C_5.Input0");

        var edges = SaveConnectionTracer.MachineEdges(links, SaveImport.IsModelledMachine);

        Assert.Equal(2, edges.Count);
        Assert.Contains(($"{P}Build_MinerMk1_C_1", $"{P}Build_SmelterMk1_C_3"), edges);
        Assert.Contains(($"{P}Build_SmelterMk1_C_3", $"{P}Build_ConstructorMk1_C_5"), edges);
        Assert.DoesNotContain(($"{P}Build_MinerMk1_C_1", $"{P}Build_ConstructorMk1_C_5"), edges);
    }
}
