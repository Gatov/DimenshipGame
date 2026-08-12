using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// Planning against the vessel the game ships with. Every other planner test builds its own world
/// through <see cref="WorldBuilder"/>, which is what let the default vessel's standing orders
/// carry arithmetic nobody intended: a million outstanding runs on six facilities, netted into
/// <see cref="SimulationEngine.Uncommitted"/>, made Matter Mix look like a deficit of eight
/// billion and Robot Frames like fifty million in stock.
/// <para>
/// These are worth keeping whatever the numbers do next. The gap they close is that nothing else
/// asks the shipping world to plan anything.
/// </para>
/// </summary>
public class DefaultVesselPlanningTests
{
    [Test]
    public void AGoalOfFourRobotFrames_PlansWorkRatherThanBeingMetFromStock()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        var plan = ProductionPlanner.Plan(new ItemAmount(WorldDefinition.RobotFrame, 4), engine);

        // No frame exists aboard at tick zero, so a plan that proposes nothing has satisfied the
        // goal from stock that is not there.
        Assert.That(
            engine.Available(WorldDefinition.ResourceStorage, WorldDefinition.RobotFrame),
            Is.Zero,
            "the vessel starts with robot frames, so this test no longer proves what it claims");

        Assert.That(plan.Runs, Is.Not.Empty, "planning four robot frames proposed no work");
        Assert.That(plan.Transfers, Is.Not.Empty, "no material is routed to any facility");

        // The plan is not yet complete, and the reason is a separate finding: the vessel's
        // transport is a pipeline — A to B to C to Resource Storage — while the planner routes
        // every leg through the hold, so a branch placed on a facility with no line home comes
        // back as NoCompatibleExecutor. That is a topology gap, not a supply one.
        //
        // What is asserted here is the symptom this stage removed: nothing is short of a raw
        // resource the vessel is holding, and nothing is met from stock that does not exist.
        Assert.That(
            plan.Shortages.Select(s => s.Kind),
            Is.All.EqualTo(ShortageKind.NoCompatibleExecutor),
            "a shortage that is not a routing gap means the planner is reading stock wrongly again");
    }

    [Test]
    public void MatterMixAboard_IsSpendable_RatherThanReportedAsAShortage()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());
        var aboard = engine.Available(WorldDefinition.ResourceStorage, WorldDefinition.MatterMix);

        Assert.That(aboard, Is.GreaterThan(0), "the opening stock is gone before the first tick");
        Assert.That(
            engine.Uncommitted(WorldDefinition.MatterMix),
            Is.GreaterThan(0),
            "the hold holds Matter Mix the vessel cannot spend");

        // Nothing produces Matter Mix — missions do not exist — so asking for what is already
        // aboard is the case where the planner must spend it rather than send the player mining.
        var plan = ProductionPlanner.Plan(new ItemAmount(WorldDefinition.MatterMix, 1_000), engine);

        Assert.That(
            plan.Shortages,
            Is.Empty,
            "a goal smaller than the opening stock came back short");
    }
}
