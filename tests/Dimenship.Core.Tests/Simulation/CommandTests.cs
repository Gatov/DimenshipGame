using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The command surface (C0): one door, refusals as answers, and a refused command that changed
/// nothing.
/// </summary>
public class CommandTests
{
    private static string Save(SimulationEngine engine) => WorldSave.Write(Shipped.Catalog, engine.State);

    private static PlanId Order(SimulationEngine engine, ItemId item, long quantity, StorageId? to = null)
    {
        var result = engine.Execute(new OrderGoal(new ItemAmount(item, quantity), to));
        Assert.That(result, Is.InstanceOf<CommandAccepted>(), () => ((CommandRefused)result).Reason);
        return ((CommandAccepted)result).Plan!.Value;
    }

    [Test]
    public void AnOrder_ThroughExecute_IsTheComposersPath_ByteForByte()
    {
        // A controller and a player adding the same demand at the same tick must leave the same
        // world: the surface is a door, not a second planner.
        var composed = Shipped.Engine();
        var approval = PlanDraftEditor.Approve(
            PlanDraftEditor.Create(new ItemAmount(DefaultVessel.Component, 2_000), composed), composed);
        composed.Commit(((PlanApprovalCommitted)approval).Plan);

        var ordered = Shipped.Engine();
        Order(ordered, DefaultVessel.Component, 2_000);

        composed.Advance(500);
        ordered.Advance(500);
        Assert.That(Save(ordered), Is.EqualTo(Save(composed)));
    }

    [Test]
    public void EveryPlanCommand_ReachesItsPlan()
    {
        var engine = Shipped.Engine();
        var first = Order(engine, DefaultVessel.Component, 2_000);
        var second = Order(engine, DefaultVessel.Component, 1_000);
        engine.Advance(30);
        var plan = engine.State.Plans.Plans.Single(p => p.Id == first);

        Assert.That(engine.Execute(new SetPlanPriority(first, Priority.High)).Accepted);
        Assert.That(plan.Priority, Is.EqualTo(Priority.High));

        Assert.That(engine.Execute(new HoldPlan(first)).Accepted);
        Assert.That(plan.Held, Is.True);
        Assert.That(engine.Execute(new ReleasePlan(first)).Accepted);
        Assert.That(plan.Held, Is.False);

        var amended = (CommandAccepted)engine.Execute(new AmendPlan(first, 1_500));
        Assert.That(plan.Goal.Quantity, Is.EqualTo(1_500));
        Assert.That(amended.Tasks, Is.Not.Empty);
        Assert.That(amended.Tasks, Is.EqualTo(plan.SpawnedTasks.TakeLast(amended.Tasks.Count)));

        Assert.That(engine.Execute(new RelinquishStock(first, engine.State.Vessel.Hold, DefaultVessel.Component, 1)).Accepted);
        Assert.That(engine.Execute(new ReassignStock(first, second, engine.State.Vessel.Hold, DefaultVessel.Component, 1)).Accepted);

        Assert.That(engine.Execute(new CancelPlan(first)).Accepted);
        Assert.That(plan.State, Is.EqualTo(PlanState.Abandoned));
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
    }

    [Test]
    public void EveryTaskCommand_ReachesAHandQueuedTask()
    {
        var engine = Shipped.Engine();
        var queued = (CommandAccepted)engine.Execute(new QueueTask(
            new TaskScript(Array.Empty<Condition>(), new Produce(DefaultVessel.PressComponents, 3)), DefaultVessel.FactoryA));
        var task = queued.Tasks.Single();
        var instance = engine.State.Tasks.Task(task)!;

        Assert.That(engine.Execute(new SetTaskPriority(task, Priority.Critical)).Accepted);
        Assert.That(instance.Priority, Is.EqualTo(Priority.Critical));
        Assert.That(engine.Execute(new HoldTask(task)).Accepted);
        Assert.That(instance.Held, Is.True);
        Assert.That(engine.Execute(new ReleaseTask(task)).Accepted);
        Assert.That(engine.Execute(new CancelTask(task)).Accepted);
        Assert.That(instance.IsFinished, Is.True, "a task with nothing started finishes at once when cancelled");
    }

    [Test]
    public void AStaleCommand_IsRefused_AndChangesNothing()
    {
        // A controller acting on a world one tick old, or a player pressing cancel on a plan that
        // just finished, is ordinary. It must be an answer, not a fault.
        var engine = Shipped.Engine();
        var plan = Order(engine, DefaultVessel.Component, 1_000);
        engine.Advance(3_000);
        Assert.That(engine.State.Plans.Plans.Single(p => p.Id == plan).State, Is.EqualTo(PlanState.Complete), "fixture");
        var before = Save(engine);

        foreach (var command in new Command[]
                 {
                     new CancelPlan(plan),
                     new HoldPlan(plan),
                     new AmendPlan(plan, 500),
                     new RelinquishStock(plan, engine.State.Vessel.Hold, DefaultVessel.Component, 1),
                     new SetPlanPriority(new PlanId(999), Priority.High),
                     new CancelTask(new TaskId(999_999)),
                 })
        {
            var result = engine.Execute(command);
            Assert.That(result, Is.InstanceOf<CommandRefused>(), command.ToString());
            Assert.That(((CommandRefused)result).Reason, Is.Not.Empty);
        }

        Assert.That(Save(engine), Is.EqualTo(before));
    }

    [Test]
    public void APlansTask_IsRefused_AndTheRefusalNamesThePlan()
    {
        var engine = Shipped.Engine();
        var plan = Order(engine, DefaultVessel.Component, 1_000);
        var task = engine.State.Plans.Plans.Single(p => p.Id == plan).SpawnedTasks[0];

        var refusal = (CommandRefused)engine.Execute(new SetTaskPriority(task, Priority.High));

        Assert.That(refusal.Reason, Does.Contain($"plan '{plan}'"));
    }

    [Test]
    public void ACommitWhoseLastTaskIsInvalid_IsRefused_AndQueuesNothing()
    {
        // Commit used to queue each task as it checked it, so a plan whose last task was bad left
        // the rest queued with no plan. A refusal must leave the world as it found it.
        var engine = Shipped.Engine();
        var good = PlanDraftEditor.Approve(
            PlanDraftEditor.Create(new ItemAmount(DefaultVessel.Component, 1_000), engine), engine);
        var plan = ((PlanApprovalCommitted)good).Plan;
        var broken = plan with
        {
            Tasks = plan.Tasks
                .Append(new PlannedTask(
                    new TaskScript(Array.Empty<Condition>(), new Produce(DefaultVessel.PressComponents, 1)),
                    new ExecutorId("no_such_facility"), 0))
                .ToList(),
        };
        var before = Save(engine);

        var result = engine.Execute(new CommitPlan(broken));

        Assert.That(result, Is.InstanceOf<CommandRefused>());
        Assert.That(Save(engine), Is.EqualTo(before));
        Assert.That(engine.State.Plans.Plans, Is.Empty);
    }

    [Test]
    public void AnOrderForNothing_IsRefused()
    {
        var engine = Shipped.Engine();

        var result = engine.Execute(new OrderGoal(new ItemAmount(DefaultVessel.Component, 0)));

        Assert.That(result, Is.InstanceOf<CommandRefused>(), "a goal of nothing was accepted");
        Assert.That(engine.State.Plans.Plans, Is.Empty);
    }
}
