using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// Priority in selection (K2), as D1 decides it
/// (<c>2026-09-24-setup-identity-and-interruption-boundary-design.md</c>, Decisions 2–4): the
/// highest-priority ready task wins, today's order breaks ties within a tier, a run is never
/// interrupted, and a switch-over yields only to a strictly higher priority.
/// </summary>
public class PriorityTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly ItemId Ingot = new("ingot");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Forge = new("forge");
    private static readonly SchematicId Cast = new("cast");
    private static readonly ExecutorId Reactor = new("reactor");

    /// <summary>One reactor, three processes all fed from ore, and ore enough for anything.</summary>
    private static WorldBuilder Machine(long switchOverTicks, long effort) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Item(Ingot)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: effort, inputs: new ItemAmount(Ore, 10))
            .Schematic(Forge, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                effort: effort, inputs: new ItemAmount(Ore, 10))
            .Schematic(Cast, new ItemAmount(Ingot, 1), FacilityType.MatterReactor,
                effort: effort, inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null, switchOverTicks: switchOverTicks);

    private static TaskId Queue(SimulationEngine engine, SchematicId schematic, int? runs, Priority? priority = null)
    {
        var id = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(schematic, runs)), Reactor);
        if (priority is { } p)
        {
            engine.SetPriority(id, p);
        }

        return id;
    }

    private static FacilityInstance Facility(SimulationEngine engine) => engine.State.Vessel.Facilities.Single();

    private static int Count(SimulationEngine engine, EventCode code) =>
        engine.Snapshot.RecentEvents.Count(e => e.Code == code);

    [Test]
    public void AnUrgentTask_DisplacesAContinuousOne_AtTheRunBoundary_NeverMidRun()
    {
        // A standing order is continuously supplied, so today's "continue the current task" would
        // keep it forever. Priority is what lets an urgent order through — but only once the run in
        // progress has deposited, because its inputs are already consumed.
        var engine = Machine(switchOverTicks: 5, effort: 300).Task(Smelt, null, Reactor).Engine();
        engine.Advance(1);

        var urgent = Queue(engine, Forge, 1, Priority.High);

        engine.Advance(2);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1), "the run in progress was interrupted");

        engine.Advance(1);
        Assert.That(Facility(engine).Status, Is.EqualTo(ExecutorStatus.SwitchingOver), "the boundary did not yield");
        Assert.That(Facility(engine).SwitchTarget, Is.EqualTo(urgent));

        engine.Advance(7);
        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1), "five ticks of switch-over, then a three-tick run");
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1), "the standing order ran while outranked");
    }

    [Test]
    public void EqualPriority_NeverPreempts()
    {
        var engine = Machine(switchOverTicks: 5, effort: 300).Task(Smelt, null, Reactor).Engine();
        engine.Advance(1);
        Queue(engine, Forge, 1);

        engine.Advance(50);

        Assert.That(engine.Available(Hold, Chip), Is.Zero, "an equal-priority order displaced a continuous one");
        Assert.That(Count(engine, EventCode.PostponeOutranked), Is.Zero);
    }

    [Test]
    public void AnUrgentTaskThatCannotStart_DoesNotHoldTheFacility()
    {
        // Idling a machine for material that has not arrived is a reservation, and reservations are
        // D3's. An urgent order missing its input must not stall the work that can run.
        var refine = new SchematicId("refine");
        var engine = Machine(switchOverTicks: 5, effort: 100)
            .Schematic(refine, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ingot, 10))
            .Task(Smelt, null, Reactor)
            .Engine();

        var urgent = Queue(engine, refine, 1, Priority.Critical);
        engine.Advance(10);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(10), "the unready urgent task held the facility");
        var task = engine.Snapshot.Tasks.Single(t => t.Id == urgent);
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.InsufficientInputMaterial), "it is short, not outranked");
    }

    [Test]
    public void AReadyTaskPassedOver_ForAHigherOne_RecordsOutranked_Once()
    {
        var engine = Machine(switchOverTicks: 5, effort: 300).Task(Smelt, null, Reactor).Engine();
        engine.Advance(1);
        Queue(engine, Forge, 1, Priority.High);

        engine.Advance(10);

        var standing = engine.Snapshot.Tasks.First();
        Assert.That(standing.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(standing.LastReason, Is.EqualTo(PostponeReason.Outranked));
        Assert.That(Count(engine, EventCode.PostponeOutranked), Is.EqualTo(1), "edge-triggered, as every postponement is");

        var reactor = engine.Snapshot.Executors.Single();
        Assert.That(reactor.BlockReason, Is.Null, "a facility running the winner is not blocked by the loser");
    }

    [Test]
    public void AHigherTaskOnTheConfiguredSchematic_RunsNext_WithNoSwitchOver()
    {
        var engine = Machine(switchOverTicks: 5, effort: 300).Task(Smelt, null, Reactor).Engine();
        engine.Advance(1);
        var urgent = Queue(engine, Smelt, 2, Priority.High);

        engine.Advance(8);

        Assert.That(Count(engine, EventCode.SwitchOverStarted), Is.Zero);
        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == urgent).CompletedAtTick, Is.EqualTo(9),
            "the standing run deposited at 3, then two urgent runs in ticks 4-9");
    }

    /// <summary>
    /// One smelt run in tick 1, then a Normal forge order starts a ten-tick switch-over at tick 2.
    /// After <c>Advance(4)</c> the countdown is at 7, three ticks spent.
    /// </summary>
    private static SimulationEngine Switching()
    {
        var engine = Machine(switchOverTicks: 10, effort: 100)
            .Task(Smelt, 1, Reactor)
            .Task(Forge, 1, Reactor)
            .Engine();
        engine.Advance(4);
        Assert.That(Facility(engine).SwitchOverRemaining, Is.EqualTo(7), "fixture: switching toward forge");
        return engine;
    }

    [Test]
    public void AHigherTaskOnAThirdSchematic_RestartsTheSwitchOver_InFull()
    {
        var engine = Switching();
        var urgent = Queue(engine, Cast, 1, Priority.High);

        engine.Advance(1);

        Assert.That(Facility(engine).SwitchTarget, Is.EqualTo(urgent));
        Assert.That(Facility(engine).SwitchOverRemaining, Is.EqualTo(9), "a full restart, of which this tick is the first");
        Assert.That(Count(engine, EventCode.SwitchOverAbandoned), Is.EqualTo(1));
        Assert.That(Count(engine, EventCode.SwitchOverStarted), Is.EqualTo(2));

        engine.Advance(10);
        Assert.That(engine.Available(Hold, Ingot), Is.EqualTo(1));
        Assert.That(engine.Available(Hold, Chip), Is.Zero, "the abandoned target ran first");
    }

    [Test]
    public void AHigherTaskOnTheLoadedSetup_CancelsTheSwitchOver_AndRunsThisTick()
    {
        var engine = Switching();
        Queue(engine, Smelt, 1, Priority.High);

        engine.Advance(1);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(2), "the cancelled switch-over's setup ran the urgent order at once");
        Assert.That(Facility(engine).Configured, Is.EqualTo(Smelt), "the setup never changed");
        Assert.That(Count(engine, EventCode.SwitchOverAbandoned), Is.EqualTo(1));
    }

    [Test]
    public void AHigherTaskOnTheTargetSchematic_Retargets_AndTheCountdownContinues()
    {
        var engine = Switching();
        var urgent = Queue(engine, Forge, 1, Priority.High);

        engine.Advance(1);

        Assert.That(Facility(engine).SwitchTarget, Is.EqualTo(urgent));
        Assert.That(Facility(engine).SwitchOverRemaining, Is.EqualTo(6), "two orders for one part never pay twice");
        Assert.That(Count(engine, EventCode.SwitchOverAbandoned), Is.Zero);

        engine.Advance(7);
        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == urgent).CompletedAtTick, Is.EqualTo(12));
    }

    [Test]
    public void AnEqualPriorityTask_DoesNotAbandonASwitchOver()
    {
        var engine = Switching();
        Queue(engine, Cast, 1);

        engine.Advance(1);

        Assert.That(Facility(engine).SwitchOverRemaining, Is.EqualTo(6));
        Assert.That(Count(engine, EventCode.SwitchOverAbandoned), Is.Zero);
    }

    [Test]
    public void ALineLoadsTheHigherTransferFirst_AndLeavesCargoAboardAlone()
    {
        var buffer = new StorageId("buffer");
        var line = new ExecutorId("line");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(buffer)
            .Transport(line, Hold, buffer, throughputPerTick: 10, lengthTicks: 3)
            .Transfer(Ore, 30, Hold, buffer, line)
            .Transfer(Ore, 10, Hold, buffer, line)
            .Engine();
        var first = engine.State.Tasks.All[0].Id;
        var urgent = engine.State.Tasks.All[1].Id;

        engine.Advance(1);
        engine.SetPriority(urgent, Priority.High);
        engine.Advance(1);

        var belt = engine.State.Vessel.Transports.Single().Belt;
        Assert.That(belt[^1]!.Task, Is.EqualTo(urgent), "the free tail slot went to the higher transfer");
        Assert.That(belt[^2]!.Task, Is.EqualTo(first), "cargo already aboard was moved");
        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == first).LastReason, Is.EqualTo(PostponeReason.Outranked));
    }

    [Test]
    public void APlanLendsItsPriority_ToEveryTaskItSpawned()
    {
        var engine = Machine(switchOverTicks: 5, effort: 100).Engine();
        var loose = Queue(engine, Cast, 1);
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, 3), engine));
        var plan = engine.State.Plans.Plans.Single();

        engine.SetPriority(plan.Id, Priority.Critical);

        foreach (var id in plan.SpawnedTasks)
        {
            Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == id).Priority, Is.EqualTo(Priority.Critical));
        }

        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == loose).Priority, Is.EqualTo(Priority.Normal));
        Assert.That(Count(engine, EventCode.PriorityChanged), Is.EqualTo(1), "one command, one event");
    }

    [Test]
    public void SettingThePriorityOfAnUnknownTaskOrPlan_IsRefused()
    {
        var engine = Machine(switchOverTicks: 5, effort: 100).Engine();

        Assert.Throws<ArgumentException>(() => engine.SetPriority(new TaskId(999), Priority.High));
        Assert.Throws<ArgumentException>(() => engine.SetPriority(new PlanId(999), Priority.High));
    }

    [Test]
    public void EveryTaskStartsAtNormal()
    {
        var engine = Machine(switchOverTicks: 5, effort: 100).Task(Smelt, 1, Reactor).Engine();
        Queue(engine, Forge, 1);

        Assert.That(engine.Snapshot.Tasks.Select(t => t.Priority), Is.All.EqualTo(Priority.Normal));
    }

    [Test]
    public void AtDefaultPriority_TheShippedVesselNeverOutranksOrAbandons()
    {
        // D1's behaviour-neutral rule, read off the journal: with every task at Normal there is
        // nothing to outrank and nothing to abandon for, so neither event can appear.
        var engine = Shipped.Engine();
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(DefaultVessel.RobotFrame, 100), engine));
        var seen = new List<EventCode>();
        var last = engine.Snapshot.TotalEventsEmitted;

        for (var i = 0; i < 2_000; i++)
        {
            engine.Advance(1);
            var fresh = (int)(engine.Snapshot.TotalEventsEmitted - last);
            seen.AddRange(engine.Snapshot.RecentEvents.Skip(engine.Snapshot.RecentEvents.Count - fresh).Select(e => e.Code));
            last = engine.Snapshot.TotalEventsEmitted;
        }

        Assert.That(seen, Does.Not.Contain(EventCode.PostponeOutranked));
        Assert.That(seen, Does.Not.Contain(EventCode.SwitchOverAbandoned));
        Assert.That(seen, Does.Contain(EventCode.SwitchOverStarted), "the fixture never switched, so it proves little");
    }
}
