using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Planning;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class FactoryPlannerTests
{
    /// <summary>
    /// The community-known zero-waste plastic build: 300 crude oil + water in,
    /// 900 plastic out, every byproduct recycled through alternate recipes.
    /// The LP must rediscover it (this exercises recipe cycles and byproduct
    /// elimination, the hard parts of auto-planning).
    /// </summary>
    [Fact]
    public void Finds_the_zero_waste_plastic_optimum()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.Eliminate,
        };
        request.Targets.Add(new PlanTarget("Plastic", new Rational(900)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.Equal(new Rational(300), plan.Supplies["Crude Oil"]);
        Assert.Empty(plan.Sinks);
        Assert.True(plan.Supplies.ContainsKey("Water"));
        Assert.True(plan.TotalMachines.IsPositive);
        // No raw besides oil and water.
        Assert.Equal(2, plan.Supplies.Count);
    }

    [Fact]
    public void Banning_oil_makes_plastic_infeasible()
    {
        var request = new PlanRequest();
        request.Targets.Add(new PlanTarget("Plastic", new Rational(90)));
        request.BannedResources.Add("Crude Oil");

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        // Without oil, plastic only exists via SAM conversion chains or not at
        // all; either the planner finds such a chain or reports infeasible —
        // but it must never consume the banned resource.
        if (plan.Status == PlanStatus.Optimal)
            Assert.False(plan.Supplies.ContainsKey("Crude Oil"));
        else
            Assert.Equal(PlanStatus.Infeasible, plan.Status);
    }

    [Fact]
    public void Plans_adaptive_control_units()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.AllowSink,
        };
        request.Targets.Add(new PlanTarget("Adaptive Control Unit", new Rational(10)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.NotEmpty(plan.Recipes);
        Assert.True(plan.TotalMachines.IsPositive);
        Assert.True(plan.TotalPowerMW.IsPositive);
        Assert.Contains(plan.Supplies.Keys, k => k is "Iron Ore" or "Copper Ore");
    }

    [Fact]
    public void Maximizes_output_from_provided_inputs()
    {
        var request = new PlanRequest
        {
            MaximizeFromProvisions = true,
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.AllowSink,
        };
        request.Targets.Add(new PlanTarget("Iron Plate", Rational.One));
        request.Provisions.Add(new PlanProvision("Iron Ore", new Rational(60)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        // 60 ore yields at least 40 plates with the base chain; alternates may do better.
        Assert.True(plan.Outputs["Iron Plate"] >= new Rational(40),
            $"Expected ≥40 plates/min from 60 ore, got {plan.Outputs["Iron Plate"]}");
    }

    /// <summary>
    /// "I already make 45 iron ingots/min elsewhere and that is all there is":
    /// 60 plates need 90 ingots, so the whole output scales to half.
    /// </summary>
    [Fact]
    public void Exclusive_provision_bottlenecks_and_scales_the_output()
    {
        var request = new PlanRequest
        {
            Byproducts = ByproductMode.AllowSink,
            UseAlternateRecipes = false,
        };
        request.Targets.Add(new PlanTarget("Iron Plate", new Rational(60)));
        request.Provisions.Add(new PlanProvision("Iron Ingot", new Rational(45), Exclusive: true));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.Equal(new Rational(1, 2), plan.AchievedFraction);
        Assert.Equal(new Rational(30), plan.Outputs["Iron Plate"]);
        Assert.Contains("Iron Ingot", plan.Bottlenecks);
        // No extra ingot production was planned.
        Assert.DoesNotContain(plan.Recipes, r => r.Recipe == "Iron Ingot");
    }

    /// <summary>Same provision without the cap toggle: the planner just builds
    /// the missing ingot production and delivers the full target.</summary>
    [Fact]
    public void Non_exclusive_provision_tops_up_with_own_production()
    {
        var request = new PlanRequest
        {
            Byproducts = ByproductMode.AllowSink,
            UseAlternateRecipes = false,
        };
        request.Targets.Add(new PlanTarget("Iron Plate", new Rational(60)));
        request.Provisions.Add(new PlanProvision("Iron Ingot", new Rational(45), Exclusive: false));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.Equal(Rational.One, plan.AchievedFraction);
        Assert.Equal(new Rational(60), plan.Outputs["Iron Plate"]);
        Assert.Contains(plan.Recipes, r => r.Recipe == "Iron Ingot");
    }

    [Fact]
    public void Power_bias_prefers_cheaper_energy_plans()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Power,
            Byproducts = ByproductMode.AllowSink,
        };
        request.Targets.Add(new PlanTarget("Iron Rod", new Rational(60)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.True(plan.TotalPowerMW.IsPositive);
    }
}
