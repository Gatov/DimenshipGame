using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Replay.Tests;

/// <summary>
/// The policy hook and the reference controllers (E2). A controller acts only through the door the
/// player uses, so what is pinned here is that the door counts what passes through it, that the
/// baseline is still the baseline, and that each policy does the one thing that distinguishes it.
/// </summary>
public class ControllerTests
{
    private static readonly ContentLoadResult Content =
        new JsonContentSource(new DirectoryContentFileSystem(Path.Combine(AppContext.BaseDirectory, "content")))
            .Load();

    private static ContentCatalog Catalog =>
        Content.Catalog
        ?? throw new InvalidOperationException(
            "the shipped content does not load:\n" + string.Join("\n", Content.Errors));

    private static ReplayScript Script(string json)
    {
        var result = ReplayScript.Parse(json, Catalog, Content.Scenarios);
        Assert.That(result.Errors, Is.Empty, "the fixture script does not parse");
        return result.Script!;
    }

    private static ReplayScript Shipped(string file) =>
        Script(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", file)));

    private static ReplayResult Run(ReplayScript script, IController? controller = null) =>
        Replay.Run(Catalog, Content.Scenarios.Single(s => s.Id == script.Scenario), script, controller);

    private static ReplayScript OneDemand(string item, long quantity, long endTick, string? destination = null) =>
        Script($$"""
            {
              "scenario": "default_vessel",
              "endTick": {{endTick}},
              "demands": [ { "id": "parts", "tick": 0, "item": "{{item}}", "quantity": {{quantity}}{{(destination is null ? "" : $", \"destination\": \"{destination}\"")}} } ]
            }
            """);

    /// <summary>Orders once at tick 0, and tries to hold a plan that does not exist.</summary>
    private sealed class OrderOnce : IController
    {
        public string Name => "order once";

        public void Decide(ControllerContext context)
        {
            if (context.Now != 0)
            {
                return;
            }

            context.Order(new ItemAmount(new ItemId("component"), 1000));
            context.Execute(new HoldPlan(new PlanId(999)));
        }
    }

    [Test]
    public void QueueOrder_IsTheRunWithNoController_ByteForByte()
    {
        var script = Shipped("smoke.json");

        Assert.That(
            ReplayReport.Format(Run(script, new QueueOrder())), Is.EqualTo(ReplayReport.Format(Run(script))));
    }

    [TestCaseSource(typeof(Policies), nameof(Policies.Names))]
    public void EveryPolicy_RunTwice_GivesByteIdenticalReports(string policy)
    {
        var script = Shipped("e1-b-urgent.json");

        var first = ReplayReport.Format(Run(script, Policies.Create(policy)));
        var second = ReplayReport.Format(Run(script, Policies.Create(policy)));

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void EveryPolicyName_CreatesAController_AndNoOtherNameDoes()
    {
        Assert.That(Policies.Names.Select(Policies.Create), Has.All.Not.Null);
        Assert.That(Policies.Create("fifo"), Is.Null);
    }

    [Test]
    public void AControllersOrder_IsCommittedLikeADemand_AndARefusedCommandCountsForNothing()
    {
        var result = Run(OneDemand("component", 1000, 400), new OrderOnce());

        var order = result.ControllerOrders.Single();
        Assert.That(order.Demand.Id, Is.EqualTo("component#1"));
        Assert.That(order.CommittedAtTick, Is.EqualTo(0));
        Assert.That(order.ReadyAtTick, Is.Not.Null, "the order should be made like any demand");
        Assert.That(result.ControllerCommands.Single(), Is.EqualTo(new ControllerCommands("HoldPlan", 0, 1)));
        Assert.That(result.Interventions, Is.EqualTo(2), "the scripted demand and the order; not the refusal");
        Assert.That(ReplayReport.Format(result), Does.Contain("- Policy: order once"));
    }

    [Test]
    public void SimpleReplenishment_OrdersAgainAtEveryReading_WhileItsFirstOrderIsStillBeingMade()
    {
        // A unit of frames outlasts two readings, and a reading counts only what is in the hold
        // free, so it orders again while its first order is still being made.
        const long reading = SimpleReplenishment.ReadingTicks;
        var result = Run(OneDemand("robot_frame", 1000, 2 * reading), new SimpleReplenishment());

        Assert.That(
            result.ControllerOrders.Select(o => (o.Demand.Tick, o.Demand.Goal.Quantity)),
            Is.EqualTo(new[] { (0L, 1000L), (reading, 1000L), (2 * reading, 1000L) }));
        Assert.That(
            result.ControllerOrders[0].ReadyAtTick ?? long.MaxValue, Is.GreaterThan(2 * reading),
            "the first order should still be in production at the last reading");
    }

    [Test]
    public void TheImprovedController_CountsWhatIsComing_AndHoldsItsRestockWhileDepartingWorkWantsTheFactory()
    {
        var result = Run(
            OneDemand("robot_frame", 250, 8 * SimpleReplenishment.ReadingTicks, "dock_a_hold"), new ImprovedController());

        Assert.That(
            result.ControllerOrders.Select(o => (o.Demand.Tick, o.Demand.Goal.Quantity)),
            Is.EqualTo(new[] { (0L, ImprovedController.Campaign * 250) }),
            "one campaign, and no reading after it orders what is already coming");
        Assert.That(
            result.ControllerCommands,
            Is.EqualTo(new[]
            {
                new ControllerCommands("SetPlanPriority", 1, 0),
                new ControllerCommands("HoldPlan", 1, 0),
                new ControllerCommands("ReleasePlan", 1, 0),
            }));
        Assert.That(result.Demands.Single().ReadyAtTick, Is.Not.Null);
        Assert.That(result.ControllerOrders.Single().ReadyAtTick, Is.Not.Null, "released, the restock finishes");
    }

    [Test]
    public void InSituationB_TheImprovedController_ReadiesTheExpeditionFasterThanQueueOrder()
    {
        // E2's headline on its tuning set. The expedition's plan is raised, and the upgrade's work
        // at the factory and reactor the expedition needs is held until the expedition is made.
        var script = Shipped("e1-b-urgent.json");
        long Expedition(ReplayResult r) => r.Demands.Single(d => d.Demand.Id == "expedition_bulkheads").Readiness!.Value;

        var queue = Run(script);
        var improved = Run(script, new ImprovedController());

        Assert.That(Expedition(improved), Is.LessThan(Expedition(queue)));
        Assert.That(improved.Demands.All(d => d.ReadyAtTick is not null), Is.True, "the upgrade must still finish");
    }

    [TestCase("e1-a-sustained.json")]
    [TestCase("e1-b-urgent.json")]
    [TestCase("e1-c-held-out.json")]
    public void UnderTheImprovedController_EveryScriptedDemandOfTheExperiment_IsDelivered(string file)
    {
        var result = Run(Shipped(file), new ImprovedController());

        Assert.That(result.Demands.Where(d => d.ReadyAtTick is null).Select(d => d.Demand.Id), Is.Empty);
    }
}
