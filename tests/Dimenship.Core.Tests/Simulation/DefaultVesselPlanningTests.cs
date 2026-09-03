using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// Planning against the vessel the game ships with. Every other planner test builds its own world
/// through <see cref="WorldBuilder"/>, which is what let the default vessel's standing orders
/// carry arithmetic nobody intended: a million outstanding runs on six facilities, netted into
/// <see cref="SimulationEngine.Uncommitted"/>, made Matter Mix look like a deficit of eight
/// billion and Robot Frames like fifty million in stock.
/// <para>
/// The vessel opens quiet now, so these also prove the hold-star is enough for the planner without
/// any standing work already claiming stock.
/// </para>
/// </summary>
public class DefaultVesselPlanningTests
{
    [Test]
    public void AGoalOfFourRobotFrames_PlansWorkRatherThanBeingMetFromStock()
    {
        var engine = Shipped.Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(DefaultVessel.RobotFrame, 4), engine);

        // No frame exists aboard at tick zero, so a plan that proposes nothing has satisfied the
        // goal from stock that is not there.
        Assert.That(
            engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.RobotFrame),
            Is.Zero,
            "the vessel starts with robot frames, so this test no longer proves what it claims");

        Assert.That(plan.Runs, Is.Not.Empty, "planning four robot frames proposed no work");
        Assert.That(plan.Transfers, Is.Not.Empty, "no material is routed to any facility");

        // The hold-star gives every factory a line home, so a frame goal is fully routable from
        // opening stock. Shortages here would mean Uncommitted or unlock logic regressing again.
        Assert.That(plan.Shortages, Is.Empty, "the hold-star should make four frames plannable");
    }

    [Test]
    public void MatterMixAboard_IsSpendable_RatherThanReportedAsAShortage()
    {
        var engine = Shipped.Engine();
        var aboard = engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.MatterMix);

        Assert.That(aboard, Is.GreaterThan(0), "the opening stock is gone before the first tick");
        Assert.That(
            engine.Uncommitted(DefaultVessel.MatterMix),
            Is.GreaterThan(0),
            "the hold holds Matter Mix the vessel cannot spend");

        // Nothing produces Matter Mix — missions do not exist — so asking for what is already
        // aboard is the case where the planner must spend it rather than send the player mining.
        var plan = ProductionPlanner.Plan(new ItemAmount(DefaultVessel.MatterMix, 1_000), engine);

        Assert.That(
            plan.Shortages,
            Is.Empty,
            "a goal smaller than the opening stock came back short");
    }
}
