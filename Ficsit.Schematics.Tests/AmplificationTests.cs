using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class AmplificationTests
{
    private static MachineDefinition Machine(int slots, int multiplier = 1, int powerExponent = 2) => new()
    {
        Name = "Test Machine",
        MaxProductionShards = slots,
        ProductionShardMultiplier = new Rational(multiplier),
        ProductionShardPowerExponent = new Rational(powerExponent),
    };

    [Fact]
    public void No_sloops_or_no_slots_is_identity()
    {
        var m = Machine(slots: 1);
        Assert.Equal(Rational.One, Amplification.OutputFactor(m, 0));
        Assert.Equal(1.0, Amplification.PowerFactor(m, 0));

        var noSlots = Machine(slots: 0);
        Assert.Equal(Rational.One, Amplification.OutputFactor(noSlots, 1));
        Assert.Equal(1.0, Amplification.PowerFactor(noSlots, 1));
    }

    [Fact]
    public void Full_sloop_doubles_output_and_quadruples_power()
    {
        var m = Machine(slots: 1);
        Assert.Equal(new Rational(2), Amplification.OutputFactor(m, 1));   // 1 + (1/1)·1
        Assert.Equal(4.0, Amplification.PowerFactor(m, 1));                // (2/1)^2
    }

    [Fact]
    public void Half_filled_two_slot_machine_scales_correctly()
    {
        var m = Machine(slots: 2);
        Assert.Equal(new Rational(3, 2), Amplification.OutputFactor(m, 1)); // 1 + (1/2)·1
        Assert.Equal(2.25, Amplification.PowerFactor(m, 1));               // (3/2)^2
    }
}
