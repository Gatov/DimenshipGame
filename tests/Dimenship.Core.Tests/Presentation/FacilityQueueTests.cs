using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="FacilityQueue"/>: the inspector's QUEUE section (U4). The current task first, then
/// the queue in the order selection reads it, each row naming the plan it is for.
/// </summary>
public class FacilityQueueTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Refinery = new("refinery");

    /// <summary>
    /// One refinery whose buffer already holds ore, so every task can start at once. That is what
    /// lets one plan's priority outrank another's on the first tick.
    /// </summary>
    private static SimulationEngine Vessel() =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 200))
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(Ore, 200))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Refinery, FacilityType.MatterReactor, null, storage: Buffer)
            .Transport(new ExecutorId("feed"), Hold, Buffer, throughputPerTick: 10)
            .Transport(new ExecutorId("return"), Buffer, Hold, throughputPerTick: 10)
            .Engine();

    private static CommittedPlan Commit(SimulationEngine engine, long alloy)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, alloy), engine));
        return engine.State.Plans.Plans[^1];
    }

    private static TaskId RunsOf(SimulationEngine engine, CommittedPlan plan) =>
        plan.SpawnedTasks.First(id => engine.State.Tasks.Task(id)!.IsProduce);

    [Test]
    public void TheCurrentTaskComesFirst_ThenQueueOrder_AndFinishedWorkIsLeftOut()
    {
        var engine = Vessel();
        var first = Commit(engine, 3);
        var second = Commit(engine, 3);
        engine.SetPriority(second.Id, Priority.High);
        engine.Advance(1);

        var rows = FacilityQueue.For(engine.Snapshot, Refinery);

        Assert.That(rows.Select(r => r.Task), Is.EqualTo(new[] { RunsOf(engine, second), RunsOf(engine, first) }));
        Assert.That(rows.Select(r => r.Current), Is.EqualTo(new[] { true, false }));

        engine.Advance(200);

        Assert.That(FacilityQueue.For(engine.Snapshot, Refinery), Is.Empty);
    }

    [Test]
    public void ARow_NamesItsPlansGoal_PriorityAndHold_AndWhoItWaitsBehind()
    {
        var engine = Vessel();
        var waiting = Commit(engine, 3);
        var urgent = Commit(engine, 3);
        engine.SetPriority(urgent.Id, Priority.High);
        engine.Advance(2);

        var row = FacilityQueue.For(engine.Snapshot, Refinery).Single(r => r.Task == RunsOf(engine, waiting));

        Assert.That(row.Plan, Is.EqualTo(waiting.Id));
        Assert.That(row.PlanGoal, Is.EqualTo(new ItemAmount(Alloy, 3)));
        Assert.That(row.Priority, Is.EqualTo(Priority.Normal));
        Assert.That(row.Held, Is.False);
        Assert.That(row.Reason, Is.EqualTo(PostponeReason.Outranked));
        Assert.That(row.Waiting?.Plan, Is.EqualTo(urgent.Id));

        engine.Hold(waiting.Id);
        Assert.That(FacilityQueue.For(engine.Snapshot, Refinery).Single(r => r.Plan == waiting.Id).Held, Is.True);
    }

    [Test]
    public void AStandingOrderQueuedByHand_HasNoPlan_AndNoRunTotal()
    {
        var engine = Vessel();
        var task = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, null)), Refinery);

        // Enqueue, unlike a command, does not rebuild the snapshot; the next tick does.
        engine.Advance(1);

        var row = FacilityQueue.For(engine.Snapshot, Refinery).Single();

        Assert.That(row.Task, Is.EqualTo(task));
        Assert.That(row.Plan, Is.Null);
        Assert.That(row.PlanGoal, Is.Null);
        Assert.That(row.Runs, Is.Null);
    }

    [Test]
    public void AFacilityTheSnapshotDoesNotHave_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => FacilityQueue.For(Vessel().Snapshot, new ExecutorId("nowhere")));
    }
}
