using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// Moving committed work (K6d; <c>2026-10-10-moving-committed-work-design.md</c>). A plan's
/// unstarted runs go to another facility with their legs, and what is physically committed stays:
/// a run in progress, and input already loaded toward the old buffer.
/// </summary>
public class MoveWorkTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Alpha = new("alpha");
    private static readonly ExecutorId Beta = new("beta");
    private static readonly ExecutorId Press = new("press");
    private static readonly ExecutorId FeedA = new("feed_a");

    /// <summary>
    /// Two refineries, each on its own buffer, fed from the hold and returning to it. A feed loads
    /// one run's ore a tick, so a move can land with some of the plan's input aboard and the rest
    /// still in the hold. <paramref name="betaFed"/> false leaves Beta with no line in, and
    /// <paramref name="betaBuilt"/> false leaves it an empty slot.
    /// </summary>
    private static WorldBuilder Builder(bool betaFed = true, bool betaBuilt = true, long bufferAOre = 0) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 200))
            .Storage(BufferA, StorageArchetype.FullHold,
                bufferAOre > 0 ? new[] { new ItemAmount(Ore, bufferAOre) } : Array.Empty<ItemAmount>())
            .Storage(BufferB)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Alpha, FacilityType.MatterReactor, null, storage: BufferA)
            .Producer(Beta, FacilityType.MatterReactor, null, storage: BufferB, builtAtStart: betaBuilt)
            .Producer(Press, FacilityType.Factory, null)
            .Transport(FeedA, Hold, BufferA, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Transport(new ExecutorId("feed_b"), Hold, BufferB, throughputPerTick: 10, lengthTicks: 3,
                builtAtStart: betaFed)
            .Transport(new ExecutorId("return_b"), BufferB, Hold, throughputPerTick: 10);

    /// <summary>Commits ten alloy, which the planner puts on Alpha: free, and declared first.</summary>
    private static CommittedPlan Commit(SimulationEngine engine)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, 10), engine));
        var plan = engine.State.Plans.Plans[^1];
        Assert.That(Tasks(engine, plan).Single(t => t.IsProduce).ExecutorId, Is.EqualTo(Alpha), "fixture");
        return plan;
    }

    private static List<TaskInstance> Tasks(SimulationEngine engine, CommittedPlan plan) =>
        plan.SpawnedTasks.Select(id => engine.State.Tasks.Task(id)).OfType<TaskInstance>().ToList();

    private static void AdvanceUntil(SimulationEngine engine, Func<bool> condition, int limit = 200)
    {
        for (var i = 0; i < limit && !condition(); i++)
        {
            engine.Advance(1);
        }

        Assert.That(condition(), "fixture: the condition never came true");
    }

    /// <summary>Runs a plan out, checking the claim invariant on every tick of the way.</summary>
    private static void RunOut(SimulationEngine engine, CommittedPlan plan)
    {
        for (var i = 0; i < 400 && plan.State == PlanState.Active; i++)
        {
            engine.Advance(1);
            Assert.That(engine.ClaimInvariantViolations(), Is.Empty, $"tick {engine.State.Clock.Tick}");
        }

        Assert.That(plan.State, Is.EqualTo(PlanState.Complete));
    }

    [Test]
    public void Move_SendsTheUnstartedRuns_AndTheirLegs_ToTheOtherFacility_AndThePlanMakesTheSameGoal()
    {
        var engine = Builder().Engine();
        var plan = Commit(engine);
        var runs = Tasks(engine, plan).Single(t => t.IsProduce);
        var feed = Tasks(engine, plan).Single(t => t.ExecutorId == FeedA);
        AdvanceUntil(engine, () => runs.RunActive && feed.LoadedQuantity >= 30);
        var before = plan.SpawnedTasks.Count;

        var result = engine.Execute(new MoveWork(plan.Id, Smelt, Beta));

        var accepted = (CommandAccepted)result;
        Assert.That(accepted.Plan, Is.EqualTo(plan.Id));
        Assert.That(accepted.Tasks, Is.EqualTo(plan.SpawnedTasks.Skip(before)), "the tasks it appended");
        var moved = accepted.Tasks.Select(id => engine.State.Tasks.Task(id)!).ToList();
        var there = moved.Single(t => t.IsProduce);
        Assert.That(there.ExecutorId, Is.EqualTo(Beta));
        Assert.That(moved.Where(t => t.IsTransfer).Select(t => (t.Transfer.From, t.Transfer.To)),
            Is.EqualTo(new[] { (Hold, BufferB), (BufferB, Hold) }), "inbound, then the runs, then outbound");

        // What stays at Alpha is exactly what its loaded ore and its run in progress pay for.
        Assert.That(runs.Produce.Runs * 10, Is.EqualTo(feed.Transfer.Quantity));
        Assert.That(feed.Transfer.Quantity, Is.EqualTo(feed.LoadedQuantity));
        Assert.That(runs.Produce.Runs + there.Produce.Runs, Is.EqualTo(10));
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
        Assert.That(engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.WorkMoved && e.Subject == Beta.Value));

        RunOut(engine, plan);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(10));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(100), "a move spent ore twice, or left some stranded");
        Assert.That(there.CompletedRuns, Is.EqualTo(there.Produce.Runs));
        Assert.That(there.CompletedRuns, Is.GreaterThan(0));
    }

    [Test]
    public void FreeOreInTheOldBuffer_LeavesNoHoldingBeyondItsNeed_AfterAMove()
    {
        // Alpha's first runs draw the free ore already in its buffer, so its deliveries run ahead
        // of its runs when the rest move. A move lowers need and inbound there by the same
        // quantity, so no holding is left beyond its need, and the move settles nothing.
        var engine = Builder(bufferAOre: 30).Engine();
        var plan = Commit(engine);
        var feed = Tasks(engine, plan).Single(t => t.ExecutorId == FeedA);
        AdvanceUntil(engine, () => feed.MovedQuantity >= 40);

        engine.Move(plan.Id, Smelt, Beta);

        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
        RunOut(engine, plan);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(10));
    }

    [Test]
    public void ARunInProgress_FinishesWhereItIs_EvenWhenEveryOtherRunMoves()
    {
        var engine = Builder().Engine();
        var plan = Commit(engine);
        var runs = Tasks(engine, plan).Single(t => t.IsProduce);
        var feed = Tasks(engine, plan).Single(t => t.ExecutorId == FeedA);
        AdvanceUntil(engine, () => runs.RunActive);
        var inProgress = runs.CompletedRuns + 1;

        engine.Move(plan.Id, Smelt, Beta);

        Assert.That(runs.Produce.Runs, Is.GreaterThanOrEqualTo(inProgress));
        Assert.That(runs.IsFinished, Is.False, "the run in progress was cut away");

        RunOut(engine, plan);

        Assert.That(runs.CompletedRuns, Is.EqualTo(runs.Produce.Runs));
        Assert.That(feed.MovedQuantity, Is.EqualTo(feed.Transfer.Quantity), "cargo aboard was stranded");
    }

    [Test]
    public void WorkWhoseInputIsAllAboard_CannotMove_AndTheRefusalSaysWhy_AndChangesNothing()
    {
        var engine = Builder().Engine();
        var plan = Commit(engine);
        var feed = Tasks(engine, plan).Single(t => t.ExecutorId == FeedA);
        AdvanceUntil(engine, () => feed.LoadedQuantity == feed.Transfer.Quantity);
        var written = WorldSave.Write(engine.Catalog, engine.State);

        var result = engine.Execute(new MoveWork(plan.Id, Smelt, Beta));

        Assert.That(result, Is.InstanceOf<CommandRefused>());
        Assert.That(((CommandRefused)result).Reason, Does.Contain("can move").And.Contain("already been sent"));
        Assert.That(WorldSave.Write(engine.Catalog, engine.State), Is.EqualTo(written));
    }

    [Test]
    public void EachRefusal_NamesItsReason_AndChangesNothing()
    {
        string Refused(SimulationEngine engine, Command command)
        {
            var written = WorldSave.Write(engine.Catalog, engine.State);
            var result = engine.Execute(command);
            Assert.That(result, Is.InstanceOf<CommandRefused>(), command.ToString());
            Assert.That(WorldSave.Write(engine.Catalog, engine.State), Is.EqualTo(written), command.ToString());
            return ((CommandRefused)result).Reason;
        }

        var engine = Builder().Engine();
        var plan = Commit(engine);
        Assert.That(Refused(engine, new MoveWork(plan.Id, Smelt, Alpha)),
            Does.Contain("no unstarted runs of 'smelt' anywhere but 'alpha'"));
        Assert.That(Refused(engine, new MoveWork(plan.Id, Smelt, Press)), Does.Contain("needs a MatterReactor, but 'press' is a Factory"));
        Assert.That(Refused(engine, new MoveWork(new PlanId(99), Smelt, Beta)), Does.Contain("No active plan"));
        Assert.That(Refused(engine, new MoveWork(plan.Id, new SchematicId("melt"), Beta)), Does.Contain("No schematic"));

        var unfed = Builder(betaFed: false).Engine();
        var stranded = Commit(unfed);
        Assert.That(Refused(unfed, new MoveWork(stranded.Id, Smelt, Beta)),
            Does.Contain("No built line runs 'hold' to 'buffer_b'"));

        var empty = Builder(betaBuilt: false).Engine();
        var waiting = Commit(empty);
        Assert.That(Refused(empty, new MoveWork(waiting.Id, Smelt, Beta)), Does.Contain("unbuilt"));
    }

    [Test]
    public void AMove_SurvivesASave_ByteForByte()
    {
        var straight = Builder().Engine();
        var plan = Commit(straight);
        straight.Advance(4);
        straight.Move(plan.Id, Smelt, Beta);
        straight.Advance(10);

        var catalog = straight.Catalog;
        var written = WorldSave.Write(catalog, straight.State);
        var loaded = WorldSave.Read(written, catalog, new[] { Builder().Scenario() });
        Assert.That(loaded.Errors, Is.Empty);
        var resumed = new SimulationEngine(catalog, loaded.State!);

        straight.Advance(200);
        resumed.Advance(200);

        Assert.That(WorldSave.Write(catalog, resumed.State), Is.EqualTo(WorldSave.Write(catalog, straight.State)));
    }
}
