using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// Scheduling telemetry (plan M1): the utilization window a facility fills every tick, the three
/// lifecycle ticks a task carries, and the material tied up in unfinished work, which is projected
/// on the snapshot rather than stored.
/// </summary>
public class TelemetryTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Forge = new("forge");
    private static readonly ExecutorId Reactor = new("reactor");

    private static WorldBuilder Smelter(
        long ore = 1_000, long effort = 100, int? runs = null, long energy = 0, long switchOverTicks = 0) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: effort, energy: energy, inputs: new ItemAmount(Ore, 10))
            .Schematic(Forge, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null, switchOverTicks: switchOverTicks)
            .Task(Smelt, runs, Reactor);

    private static UtilizationReading Reading(SimulationEngine engine) =>
        engine.Snapshot.Executors.Single(e => e.Id == Reactor).Utilization;

    private static long Sum(UtilizationReading r) =>
        r.Working + r.Idle + r.WaitingInput + r.WaitingOutput + r.Throttled + r.SwitchingOver + r.Held;

    [Test]
    public void AFacilityWorkingEveryTick_CountsEveryTickAsWorking()
    {
        var engine = Smelter().Engine();

        engine.Advance(25);

        var reading = Reading(engine);
        Assert.That(reading.Working, Is.EqualTo(25));
        Assert.That(reading.Measured, Is.EqualTo(25));
    }

    [Test]
    public void AStarvedFacility_CountsWaitingInput_NotIdle()
    {
        // One run's worth of ore. The second run has a task and no ore, which is a shortage and
        // not an idle machine — the distinction the GDD's inspector line exists to make.
        var engine = Smelter(ore: 10, runs: 2).Engine();

        engine.Advance(5);

        var reading = Reading(engine);
        Assert.That(reading.Working, Is.EqualTo(1));
        Assert.That(reading.WaitingInput, Is.EqualTo(4));
        Assert.That(reading.Idle, Is.EqualTo(0));
    }

    [Test]
    public void AFacilityWithNothingQueued_CountsIdle()
    {
        var engine = Smelter(runs: 1).Engine();

        engine.Advance(4);

        var reading = Reading(engine);
        Assert.That(reading.Working, Is.EqualTo(1));
        Assert.That(reading.Idle, Is.EqualTo(3));
    }

    [Test]
    public void AFullBuffer_CountsWaitingOutput()
    {
        // Room for one alloy and not a second (the ore takes a sliver of the shared volume), so the
        // second run is refused for want of somewhere to put it.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy, holdCapacity: 2)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, null, Reactor)
            .Engine();

        engine.Advance(5);

        var reading = Reading(engine);
        Assert.That(reading.Working, Is.EqualTo(1));
        Assert.That(reading.WaitingOutput, Is.EqualTo(4));
    }

    [Test]
    public void ARefusedEnergyCharge_CountsThrottled()
    {
        var engine = Smelter(energy: 1_000).Energy(10).Engine();

        engine.Advance(3);

        Assert.That(Reading(engine).Throttled, Is.EqualTo(3));
    }

    [Test]
    public void ASwitchOver_CountsExactlyItsTicks()
    {
        var engine = Smelter(runs: 1, switchOverTicks: 30).Task(Forge, 1, Reactor).Engine();

        engine.Advance(40);

        var reading = Reading(engine);
        Assert.That(reading.SwitchingOver, Is.EqualTo(30));
        Assert.That(reading.Working, Is.EqualTo(2), "one smelt run, then one forge run");
    }

    [Test]
    public void AShutGate_CountsHeld_NotWaitingInput()
    {
        // A facility gated by a condition is neither short of input nor idle. Folding it into
        // either would name the wrong cause, and a percentage without the right cause is the
        // thing the GDD says is not enough.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null)
            .Engine();

        var never = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Alloy.Value),
            },
            Comparison.GreaterOrEqual,
            new Literal(1_000_000));
        engine.Enqueue(new TaskScript(new[] { never }, new Produce(Smelt, 1)), Reactor);

        engine.Advance(6);

        var reading = Reading(engine);
        Assert.That(reading.Held, Is.EqualTo(6));
        Assert.That(reading.WaitingInput, Is.EqualTo(0));
    }

    [Test]
    public void AnUnbuiltFacility_MeasuresNothing()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null, builtAtStart: false)
            .Engine();

        engine.Advance(10);

        Assert.That(Reading(engine).Measured, Is.EqualTo(0));
    }

    [Test]
    public void TheCategories_SumToMeasured_AfterTheRingHasWrapped()
    {
        // Working, then starved, across more ticks than the window is wide, so buckets have been
        // recycled. Measured is the divisor, and it is only honest if it is the sum.
        var engine = Smelter(ore: 1_000, effort: 300).Engine();

        engine.Advance(UtilizationWindow.DefaultWindowTicks + 137);

        var reading = Reading(engine);
        Assert.That(Sum(reading), Is.EqualTo(reading.Measured));
        Assert.That(reading.Measured, Is.LessThanOrEqualTo(UtilizationWindow.DefaultWindowTicks));
        Assert.That(
            reading.Measured,
            Is.GreaterThan(UtilizationWindow.DefaultWindowTicks - UtilizationWindow.DefaultBucketTicks),
            "a wrapped ring is short of the window by at most the bucket it is filling");
        Assert.That(reading.Working, Is.GreaterThan(0));
        Assert.That(reading.WaitingInput, Is.GreaterThan(0));
    }

    [Test]
    public void TheWindow_ForgetsWhatFellOutOfIt()
    {
        // 300 ticks of work and then nothing queued for far longer than the window: the window
        // must read idle, not remember the work.
        var engine = Smelter(ore: 3_000, runs: 300).Engine();

        engine.Advance(300 + UtilizationWindow.DefaultWindowTicks + UtilizationWindow.DefaultBucketTicks);

        var reading = Reading(engine);
        Assert.That(reading.Working, Is.EqualTo(0));
        Assert.That(reading.Idle, Is.EqualTo(reading.Measured));
    }

    [Test]
    public void EveryStatusAndReason_MapsToExactlyOneCategory()
    {
        // The engine never sets a blocked status without a reason, but every reason it could set
        // has to land somewhere: an unmapped one would throw mid-tick.
        foreach (var reason in Enum.GetValues<PostponeReason>())
        {
            Assert.DoesNotThrow(() =>
                UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, reason));
        }

        Assert.That(
            UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, PostponeReason.SafetyLock),
            Is.EqualTo(UtilizationCategory.Held));
        Assert.That(
            UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, PostponeReason.ConditionNotMet),
            Is.EqualTo(UtilizationCategory.Held));
        Assert.That(
            UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, PostponeReason.DestinationFull),
            Is.EqualTo(UtilizationCategory.WaitingOutput));
    }

    [Test]
    public void AProductionTask_CarriesItsQueuedStartedAndCompletedTicks()
    {
        var engine = Smelter(runs: 2).Engine();
        engine.Advance(3);

        var late = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), Reactor);
        engine.Advance(3);

        var seeded = engine.Snapshot.Tasks.First();
        Assert.That(seeded.EnqueuedAtTick, Is.EqualTo(0), "a scenario task is queued before the first tick");
        Assert.That(seeded.FirstStartedAtTick, Is.EqualTo(1));
        Assert.That(seeded.CompletedAtTick, Is.EqualTo(2));

        var queued = engine.Snapshot.Tasks.Single(t => t.Id == late);
        Assert.That(queued.EnqueuedAtTick, Is.EqualTo(3));
        Assert.That(queued.FirstStartedAtTick, Is.EqualTo(4));
        Assert.That(queued.CompletedAtTick, Is.EqualTo(4));
    }

    [Test]
    public void AFirstStart_IsNotMovedByALaterRun()
    {
        var engine = Smelter(runs: 3).Engine();

        engine.Advance(2);

        var task = engine.Snapshot.Tasks.Single();
        Assert.That(task.FirstStartedAtTick, Is.EqualTo(1));
        Assert.That(task.CompletedAtTick, Is.Null, "two runs of three");
    }

    [Test]
    public void ATransfer_StartsAtPickup_AndCompletesAtDelivery()
    {
        var buffer = new StorageId("buffer");
        var line = new ExecutorId("line");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(buffer)
            .Transport(line, Hold, buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, 10, Hold, buffer, line)
            .Engine();

        engine.Advance(10);

        var task = engine.Snapshot.Tasks.Single();
        var completed = engine.Snapshot.RecentEvents.Single(e => e.Code == EventCode.TransferCompleted);
        Assert.That(task.EnqueuedAtTick, Is.EqualTo(0));
        Assert.That(task.FirstStartedAtTick, Is.EqualTo(1));
        Assert.That(task.CompletedAtTick, Is.EqualTo(completed.Tick));
        Assert.That(task.CompletedAtTick, Is.GreaterThan(task.FirstStartedAtTick), "a belt takes time");
    }

    [Test]
    public void InputsHeldByARun_AreMaterialInProcess_UntilItDeposits()
    {
        var engine = Smelter(effort: 300, runs: 1).Engine();

        engine.Advance(1);
        Assert.That(InProcess(engine, Ore).InRuns, Is.EqualTo(10), "consumed at run start, not yet output");

        engine.Advance(3);
        Assert.That(InProcess(engine, Ore).InRuns, Is.EqualTo(0), "the run deposited");
    }

    [Test]
    public void CargoOnABelt_IsMaterialInProcess()
    {
        var buffer = new StorageId("buffer");
        var line = new ExecutorId("line");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(buffer)
            .Transport(line, Hold, buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, 30, Hold, buffer, line)
            .Engine();

        engine.Advance(2);

        Assert.That(
            InProcess(engine, Ore).OnBelts,
            Is.EqualTo(engine.Snapshot.Transports.Single().Cargo.Single().Amount));
        Assert.That(InProcess(engine, Ore).OnBelts, Is.EqualTo(20));
    }

    [Test]
    public void MaterialInProcess_ListsEveryItem_InCatalogOrder()
    {
        var engine = Smelter().Engine();

        Assert.That(
            engine.Snapshot.InProcess.Select(i => i.Id),
            Is.EqualTo(new[] { Ore, Alloy, Chip }));
    }

    private static ItemInProcess InProcess(SimulationEngine engine, ItemId item) =>
        engine.Snapshot.InProcess.Single(i => i.Id == item);
}
