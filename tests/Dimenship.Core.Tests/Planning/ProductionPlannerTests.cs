using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

public class ProductionPlannerTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId RefineryB = new("refinery_b");

    // A line runs one route, so every buffer the planner routes through needs a pair: one line
    // carrying inputs out to it, one carrying finished output back to the hold.
    private static readonly ExecutorId FeedA = new("feed_a");
    private static readonly ExecutorId ReturnA = new("return_a");
    private static readonly ExecutorId FeedB = new("feed_b");
    private static readonly ExecutorId ReturnB = new("return_b");

    /// <summary>Ten ore makes one alloy, on one refinery, with one transport line.</summary>
    private static WorldBuilder Reactor(long oreOnHand, bool unlocked = true) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, oreOnHand))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                unlocked: unlocked, inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, unlocked ? Smelt : null, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000);

    [Test]
    public void WhatIsAlreadyAboard_IsUsedBeforeAnythingIsProduced()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100), new ItemAmount(Alloy, 4))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 10), engine);

        Assert.That(plan.Runs().Single().Runs, Is.EqualTo(6), "four were already aboard");
        Assert.That(plan.IsComplete, Is.True);
    }

    [Test]
    public void AGoalAlreadySatisfied_PlansNothingAtAll()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Alloy, 50))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 10), engine);

        Assert.That(plan.Tasks, Is.Empty);
        Assert.That(plan.IsComplete, Is.True);
    }

    [Test]
    public void ALockedSchematic_IsUnplannable_NotAnException()
    {
        // A mission fixes this, not hauling. Reporting it any other way would send the player
        // looking for something a mission has to unlock instead.
        var engine = Reactor(oreOnHand: 100, unlocked: false).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 3), engine);

        Assert.That(plan.Runs(), Is.Empty, "the branch is not built at all");
        Assert.That(plan.Unplannable.Single().Reason, Is.EqualTo(UnplannableReason.LockedSchematic));
        Assert.That(plan.Unplannable.Single().Quantity, Is.EqualTo(3));
    }

    [Test]
    public void APlanWithNoProducerForAnInput_StillEmitsTheTransfer()
    {
        // Ore has no schematic. It is not unplannable: the transfer still moves the full 20 that
        // two runs need, and the engine's transport phase postpones it on
        // InsufficientSourceMaterial until enough ore actually arrives.
        var engine = Reactor(oreOnHand: 5).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(plan.Unplannable, Is.Empty, "an unproducible raw material is not unplannable");
        var transfer = plan.Transfers().Single(t => t.Item == Ore);
        Assert.That(transfer.Quantity, Is.EqualTo(20), "two runs need 20 ore regardless of what's aboard");
        Assert.That(transfer.AvailableAtSource, Is.EqualTo(5), "only the five aboard were ever available");
    }

    [Test]
    public void CyclicSchematics_TerminateAsUnplannable()
    {
        // Alloy is made from chips and chips from alloy. A visited set is the difference between
        // a diagnosable content error and a stack overflow.
        var makeChip = new SchematicId("make_chip");
        var engine = new WorldBuilder()
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold)
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Chip, 1))
            .Schematic(makeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Alloy, 1))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 1), engine);

        Assert.That(plan.Unplannable.Any(s => s.Reason == UnplannableReason.CyclicSchematic), Is.True);
    }

    [Test]
    public void NoFacilityOfTheRequiredType_IsUnplannable()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(new ExecutorId("factory"), FacilityType.Factory, initialSchematic: null,
                storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(plan.Unplannable.Single().Reason, Is.EqualTo(UnplannableReason.NoExecutorOrLine));
    }

    [Test]
    public void Overproduction_IsCreditedToLaterBranches()
    {
        // The schematic makes five at a time. Asking for three makes five, and the surplus two
        // are real: a second demand for two must not plan another run.
        var makeChip = new SchematicId("make_chip");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(makeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new[] { new ItemAmount(Alloy, 3), new ItemAmount(Ore, 1) })
            .Schematic(Smelt, new ItemAmount(Alloy, 5), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Chip, 1), engine);

        var alloyRuns = plan.Runs().Single(r => r.Schematic == Smelt).Runs;
        Assert.That(alloyRuns, Is.EqualTo(1), "one run covers the three needed, with two spare");
    }

    [Test]
    public void PlanningTheSameGoalTwice_DoesNotSpendTheSameStockTwice()
    {
        // Availability nets out what committed tasks have already claimed. Without that, a
        // second plan would happily promise the first plan's ore all over again.
        var engine = Reactor(oreOnHand: 60).Engine();

        var first = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        Assert.That(first.IsComplete, Is.True, "50 of the 60 ore covers five runs");
        engine.Commit(first);

        var second = ProductionPlanner.Plan(new ItemAmount(Alloy, 8), engine);

        Assert.That(
            second.Runs().Single().Runs, Is.EqualTo(3),
            "five alloy are already on their way, so only three more are needed");

        var oreTransfer = second.Transfers().Single(t => t.Item == Ore);
        Assert.That(oreTransfer.Quantity, Is.EqualTo(30), "three more runs need 30 ore");
        Assert.That(
            oreTransfer.AvailableAtSource, Is.EqualTo(10),
            "the ore is spoken for too: 10 of the 60 is left, against the 30 those runs need");
    }

    [Test]
    public void EachLeg_GoesToTheLineThatActuallyRunsIt()
    {
        var engine = Reactor(oreOnHand: 100).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        var carriers = plan.Transfers().ToDictionary(t => (t.From, t.To), t => t.Executor);
        Assert.That(carriers[(Hold, BufferA)], Is.EqualTo(FeedA));
        Assert.That(carriers[(BufferA, Hold)], Is.EqualTo(ReturnA));
    }

    [Test]
    public void AnUnroutedLeg_IsUnplannable_NotAMisassignedLine()
    {
        // Only the outbound line exists, so the finished alloy has no way back to the hold.
        // Choosing by load alone would hand that leg to the feed line, which cannot make it.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(
            plan.Unplannable.Any(s => s.Reason == UnplannableReason.NoExecutorOrLine), Is.True,
            "no line runs buffer to hold");
        Assert.That(
            plan.Transfers().All(t => t.To != Hold), Is.True,
            "and none of the planned transfers pretends otherwise");
    }

    [Test]
    public void AmongLinesThatRunTheSameLeg_TheLeastLoadedIsStillChosen()
    {
        var second = new ExecutorId("feed_a_2");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(second, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 5, Hold, BufferA)), FeedA);

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(
            plan.Transfers().Single(t => t.To == BufferA).Executor, Is.EqualTo(second),
            "route first, then load — the first line already has a transfer queued");
    }

    [Test]
    public void TheLeastLoadedCompatibleFacility_IsChosen_TieBrokenByDefinitionOrder()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 10_000))
            .Storage(BufferA, 100)
            .Storage(BufferB, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Producer(RefineryB, FacilityType.MatterReactor, Smelt, storage: BufferB)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Transport(FeedB, Hold, BufferB, 1_000)
            .Transport(ReturnB, BufferB, Hold, 1_000)
            .Engine();

        Assert.That(
            ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine).Runs().Single().Executor,
            Is.EqualTo(RefineryA),
            "both idle, so the earlier definition wins the tie");

        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 50)), RefineryA);

        // Sixty, not two: fifty runs are already queued and their output counts as available,
        // so a smaller goal would correctly plan no runs at all and prove nothing about choice.
        Assert.That(
            ProductionPlanner.Plan(new ItemAmount(Alloy, 60), engine).Runs().Single().Executor,
            Is.EqualTo(RefineryB),
            "refinery A is now fifty runs deep, so the work goes to the idle one");
    }

    [Test]
    public void Planning_ChangesNothingAboutTheWorld()
    {
        // The specification is explicit that planning data is not runtime tasks. Producing a plan
        // ten times must leave the vessel exactly as it was.
        var engine = Reactor(oreOnHand: 100).Engine();
        var before = engine.Snapshot;

        for (var i = 0; i < 10; i++)
        {
            ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        }

        Assert.That(engine.Snapshot, Is.SameAs(before), "not even a new snapshot");
        Assert.That(engine.Snapshot.Tasks.Where(t => t.Action is Produce), Is.Empty);
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(100));
    }

    [Test]
    public void PlanningTheSameGoalTwiceWithoutCommitting_GivesTheSamePlan()
    {
        var engine = Reactor(oreOnHand: 100).Engine();

        var first = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        var second = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);

        Assert.That(second.Tasks, Is.EqualTo(first.Tasks));
        Assert.That(second.Unplannable, Is.EqualTo(first.Unplannable));
        Assert.That(second.EstimatedTicks, Is.EqualTo(first.EstimatedTicks));
    }

    [Test]
    public void AGoalOfNothing_IsRejected()
    {
        var engine = Reactor(oreOnHand: 10).Engine();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProductionPlanner.Plan(new ItemAmount(Alloy, 0), engine));
    }

    [Test]
    public void Commit_QueuesEveryTask_AndReturnsTheirIds()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);

        var created = engine.Commit(plan);

        Assert.That(created, Has.Count.EqualTo(plan.Tasks.Count));
        Assert.That(created.Distinct().Count(), Is.EqualTo(created.Count), "ids are unique");
        Assert.That(engine.Snapshot.Tasks.Count(t => t.Action is Produce), Is.EqualTo(plan.Runs().Count));
        Assert.That(engine.Snapshot.Tasks.Count(t => t.Action is Transfer), Is.EqualTo(plan.Transfers().Count));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PlanCommitted),
            Is.EqualTo(1));
    }

    [Test]
    public void PlanTasks_KeepTheOrderCommitWouldEnqueueThem()
    {
        // Before Stage 5 merged them, Commit enqueued every transfer first, in the order the
        // recursion discovered them, then every run the same way. Tasks must read exactly the
        // same way, or the merge silently reordered a determinism-sensitive queue.
        var engine = Reactor(oreOnHand: 100).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        var actionKinds = plan.Tasks.Select(t => t.Script.Action is Transfer ? "transfer" : "run").ToList();
        var lastTransfer = actionKinds.LastIndexOf("transfer");
        var firstRun = actionKinds.IndexOf("run");

        Assert.That(firstRun, Is.GreaterThan(lastTransfer), "every transfer precedes every run");
    }

    [Test]
    public void Estimate_IsTheBusiestExecutorsTotal()
    {
        // Ten effort a run at one a tick is ten ticks of work; two runs make the refinery cost
        // twenty. The lines move twenty ore and two alloy at ten a tick — two ticks and one tick,
        // both dwarfed by the refinery. Twenty is the estimate: the busiest executor's total, and
        // the plan cannot beat it because the estimate does not know about switch-over, queueing
        // behind existing work, or energy contention.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: 10, inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA, workRate: 1)
            .Transport(FeedA, Hold, BufferA, throughputPerTick: 10)
            .Transport(ReturnA, BufferA, Hold, throughputPerTick: 10)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(plan.EstimatedTicks, Is.EqualTo(20));
    }

    [Test]
    public void APlanCompletes_WhenItsLastTaskRetires()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        engine.Commit(plan);
        Assert.That(engine.State.Plans.Plans.Single().State, Is.EqualTo(Dimenship.Core.State.PlanState.Active));

        engine.Advance(300);

        Assert.That(
            engine.State.Plans.Plans.Single().State, Is.EqualTo(Dimenship.Core.State.PlanState.Complete));
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.PlanCompleted),
            Is.True);
    }
}
