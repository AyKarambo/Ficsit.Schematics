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

    /// <summary>Disables every alternate recipe — the "Alternates off" bulk
    /// action's effect, replacing the retired <c>UseAlternateRecipes = false</c>.</summary>
    private static void DisableAlternates(PlanRequest request)
    {
        foreach (var recipe in TestData.Database.Document.Recipes)
            if (recipe.Alternate)
                request.DisabledRecipes.Add(recipe.Name);
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
        };
        DisableAlternates(request);
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
        };
        DisableAlternates(request);
        request.Targets.Add(new PlanTarget("Iron Plate", new Rational(60)));
        request.Provisions.Add(new PlanProvision("Iron Ingot", new Rational(45), Exclusive: false));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.Equal(Rational.One, plan.AchievedFraction);
        Assert.Equal(new Rational(60), plan.Outputs["Iron Plate"]);
        Assert.Contains(plan.Recipes, r => r.Recipe == "Iron Ingot");
    }

    /// <summary>
    /// Adds every manually gathered part to the ban set — mirrors what the
    /// Auto-Plan panel does when "Exclude manually gathered parts" is on (default).
    /// </summary>
    private static void ExcludeManualParts(PlanRequest request)
    {
        foreach (var part in TestData.Database.Document.Parts)
            if (part.IsManuallyGathered)
                request.BannedResources.Add(part.Name);
    }

    /// <summary>
    /// Default planning (manual parts excluded) sources Coal by mining it, never
    /// through the Biomass→Coal / Wood→Coal hand-gathered chains. #6.
    /// </summary>
    [Fact]
    public void Default_plan_mines_coal_not_biomass()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.AllowSink,
        };
        ExcludeManualParts(request);
        request.Targets.Add(new PlanTarget("Steel Ingot", new Rational(60)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.True(plan.Supplies.ContainsKey("Coal"));
        foreach (var manual in new[] { "Leaves", "Wood", "Mycelia", "Biomass" })
            Assert.False(plan.Supplies.ContainsKey(manual), $"{manual} should not be supplied");
        Assert.DoesNotContain(plan.Recipes, r => r.Recipe is "Biocoal" or "Charcoal");
    }

    /// <summary>A disabled recipe is never used by the planner. #5.</summary>
    [Fact]
    public void Disabled_recipe_is_absent_from_the_plan()
    {
        PlanRequest Build()
        {
            // Alternates off so the standard Steel Ingot recipe is the one used.
            var r = new PlanRequest { Byproducts = ByproductMode.AllowSink };
            DisableAlternates(r);
            r.Targets.Add(new PlanTarget("Steel Ingot", new Rational(60)));
            return r;
        }

        // Baseline: the standard Steel Ingot recipe is available and used.
        var baseline = FactoryPlanner.Plan(TestData.Database, Build());
        Assert.Equal(PlanStatus.Optimal, baseline.Status);
        Assert.Contains(baseline.Recipes, r => r.Recipe == "Steel Ingot");

        var request = Build();
        request.DisabledRecipes.Add("Steel Ingot");
        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.DoesNotContain(plan.Recipes, r => r.Recipe == "Steel Ingot");
    }

    /// <summary>
    /// A banned part that the user nonetheless provisions still gets its capped
    /// supply column (provision overrides the ban — the bug-order fix). #6.
    /// </summary>
    [Fact]
    public void Banned_but_provisioned_part_supplies_up_to_its_cap()
    {
        var request = new PlanRequest { Byproducts = ByproductMode.AllowSink };
        request.Targets.Add(new PlanTarget("Iron Plate", new Rational(20)));
        request.BannedResources.Add("Iron Ore");
        request.Provisions.Add(new PlanProvision("Iron Ore", new Rational(60)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);

        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.True(plan.Supplies.TryGetValue("Iron Ore", out var used));
        Assert.True(used.IsPositive && used <= new Rational(60),
            $"Provisioned Iron Ore should supply up to its cap, got {used}");
    }

    /// <summary>
    /// With ore conversion disabled (default), no Converter Coal recipe appears
    /// even when the planner is forced off mined coal; enabling it brings them
    /// back. #6.
    /// </summary>
    [Fact]
    public void Ore_conversion_toggle_adds_or_removes_converter_columns()
    {
        var conversionRecipes = FactoryPlanner.OreConversionRecipes(TestData.Database).ToHashSet();
        Assert.Contains("Coal (Iron)", conversionRecipes);

        PlanRequest Build()
        {
            // Target Coal directly with mining banned: the only remaining
            // producers are the SAM ore-conversion recipes.
            var r = new PlanRequest { Byproducts = ByproductMode.AllowSink };
            ExcludeManualParts(r);
            r.Targets.Add(new PlanTarget("Coal", new Rational(60)));
            r.BannedResources.Add("Coal");
            return r;
        }

        // Conversion OFF: every conversion recipe disabled -> no Converter coal,
        // and with no other Coal producer the plan is infeasible.
        var off = Build();
        foreach (var name in conversionRecipes) off.DisabledRecipes.Add(name);
        var offPlan = FactoryPlanner.Plan(TestData.Database, off);
        Assert.Equal(PlanStatus.Infeasible, offPlan.Status);

        // Conversion ON: the recipes are available, so the plan becomes feasible
        // and uses a Converter coal recipe.
        var on = Build();
        var onPlan = FactoryPlanner.Plan(TestData.Database, on);
        Assert.Equal(PlanStatus.Optimal, onPlan.Status);
        Assert.Contains(onPlan.Recipes, r => conversionRecipes.Contains(r.Recipe));
    }

    /// <summary>
    /// The resource-preference budget biases the scarcity weights. Steel can be made
    /// from coal (standard recipe) or oil (the Coke Steel Ingot → Petroleum Coke
    /// chain); penalizing one raw flips the planner onto the other — proving
    /// WeightMultipliers reaches the objective in both directions.
    /// </summary>
    [Fact]
    public void Resource_preference_flips_steel_between_coal_and_oil()
    {
        PlanRequest Build()
        {
            // Exclude hand-gathered parts so steel's carbon comes from mined coal or
            // oil, not the free Biocoal-from-alien-remains chain.
            var r = new PlanRequest { Bias = PlanBias.Resources, Byproducts = ByproductMode.AllowSink };
            ExcludeManualParts(r);
            r.Targets.Add(new PlanTarget("Steel Ingot", new Rational(60)));
            return r;
        }

        var preferCoal = Build();
        preferCoal.WeightMultipliers["Crude Oil"] = new Rational(50); // oil dear → coke route out
        var coalPlan = FactoryPlanner.Plan(TestData.Database, preferCoal);
        Assert.Equal(PlanStatus.Optimal, coalPlan.Status);
        Assert.True(coalPlan.Supplies.ContainsKey("Coal"), "with oil penalized steel should source coal");
        Assert.False(coalPlan.Supplies.ContainsKey("Crude Oil"), "penalized oil should be avoided");

        var preferOil = Build();
        preferOil.WeightMultipliers["Coal"] = new Rational(50); // coal dear → coal routes out
        var oilPlan = FactoryPlanner.Plan(TestData.Database, preferOil);
        Assert.Equal(PlanStatus.Optimal, oilPlan.Status);
        Assert.True(oilPlan.Supplies.ContainsKey("Crude Oil"), "with coal penalized steel should source oil");
        Assert.False(oilPlan.Supplies.ContainsKey("Coal"), "penalized coal should be avoided");
    }

    /// <summary>A neutral budget (multiplier 1 everywhere) is a no-op: identical supplies.</summary>
    [Fact]
    public void Neutral_weight_multiplier_changes_nothing()
    {
        PlanRequest Build()
        {
            var r = new PlanRequest { Bias = PlanBias.Resources, Byproducts = ByproductMode.AllowSink };
            r.Targets.Add(new PlanTarget("Steel Ingot", new Rational(60)));
            return r;
        }

        var plain = FactoryPlanner.Plan(TestData.Database, Build());
        var withOnes = Build();
        foreach (var raw in ScarcityWeights.WeightedResources)
            withOnes.WeightMultipliers[raw] = Rational.One;
        var neutral = FactoryPlanner.Plan(TestData.Database, withOnes);

        Assert.Equal(PlanStatus.Optimal, neutral.Status);
        Assert.Equal(plain.Supplies.Count, neutral.Supplies.Count);
        foreach (var (part, rate) in plain.Supplies)
            Assert.Equal(rate, neutral.Supplies[part]);
    }

    /// <summary>
    /// The tier cap names exactly the recipes above the chosen progression tier, and
    /// a plan with them disabled uses nothing past the cap — the Auto-Plan "available
    /// up to tier" lever (e.g. no Blender) maps onto DisabledRecipes.
    /// </summary>
    [Fact]
    public void Tier_cap_excludes_higher_tier_recipes()
    {
        var db = TestData.Database;

        var above7 = FactoryPlanner.RecipesAboveTier(db, 7).ToHashSet();
        Assert.NotEmpty(above7); // phase 8/9 recipes exist to cap
        Assert.All(above7, name => Assert.True(db.RecipesByName[name].Tier.Phase > 7));
        Assert.Contains(db.Document.Recipes, r => r.Tier.Phase <= 7 && !above7.Contains(r.Name));

        var request = new PlanRequest { Bias = PlanBias.Resources, Byproducts = ByproductMode.AllowSink };
        request.Targets.Add(new PlanTarget("Fuel", new Rational(120)));
        foreach (var name in above7) request.DisabledRecipes.Add(name);

        var plan = FactoryPlanner.Plan(db, request);
        Assert.Equal(PlanStatus.Optimal, plan.Status);
        Assert.All(plan.Recipes, r => Assert.True(db.RecipesByName[r.Recipe].Tier.Phase <= 7,
            $"{r.Recipe} is tier {db.RecipesByName[r.Recipe].Tier} > cap"));
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
