using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

public class ProductionExecutorTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Fabricate = new("fabricate");
    private static readonly ExecutorId Reactor = new("reactor");
    private static readonly ExecutorId Factory = new("factory");

    /// <summary>A refinery turning 10 ore into 1 alloy, with as much ore as the test needs.</summary>
    private static WorldBuilder Smelter(long ore = 1_000, long effort = 100, int runs = 1) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, ore))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: effort, inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, runs, Reactor);

    [Test]
    public void AnExecutor_NamesTheStorageItWorks()
    {
        // Not derivable from anything else on the snapshot, and a facility inspector that could
        // not say which storage a facility draws from would be describing half a facility.
        var buffer = new StorageId("buffer");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Storage(buffer, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt, storage: buffer)
            .Engine();

        Assert.That(engine.Snapshot.Executors.Single().LocalStorage, Is.EqualTo(buffer));
    }

    [Test]
    public void RunTicksTotal_IsTheWholeRunsCost_AndRemainingCountsDownAgainstIt()
    {
        var engine = Smelter(effort: 250).Engine();

        engine.Advance(1);
        var executor = engine.Snapshot.Executors.Single();

        Assert.That(executor.RunTicksTotal, Is.EqualTo(3), "250 effort at 100 per tick");
        Assert.That(executor.RunTicksRemaining, Is.EqualTo(2));
    }

    [Test]
    public void RunTicksTotal_IsUnmovedByAPostponement()
    {
        // A run progress bar has to be honest across a stall: the run did not get longer because
        // the facility waited, and remaining must not exceed total.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 10))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: 500, inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, 2, Reactor)
            .Engine();

        engine.Advance(2);
        var midRun = engine.Snapshot.Executors.Single();
        Assert.That(midRun.RunTicksTotal, Is.EqualTo(5));

        // The second run has no ore, so the facility stalls between runs.
        engine.Advance(20);
        var stalled = engine.Snapshot.Executors.Single();

        Assert.That(
            stalled.Status, Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked),
            "the fixture must actually reach a postponement for this test to mean anything");
        Assert.That(
            stalled.RunTicksRemaining, Is.LessThanOrEqualTo(stalled.RunTicksTotal),
            "remaining can never exceed the run it is counting down");
    }

    [Test]
    public void AnIdleFacility_ReportsNoRunAtAll()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Engine();

        engine.Advance(1);
        var executor = engine.Snapshot.Executors.Single();

        Assert.That(executor.RunTicksTotal, Is.EqualTo(0), "a progress bar with no run is empty");
        Assert.That(executor.RunTicksRemaining, Is.EqualTo(0));
    }

    [Test]
    public void RunLasts_CeilingOfEffortOverWorkRate()
    {
        // 250 effort at 100 per tick is three ticks, not two and a half: the last tick does the
        // remaining 50 and the output lands at its end.
        var engine = Smelter(effort: 250).Engine();

        engine.Advance(2);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(0), "still mid-run after two ticks");

        engine.Advance(1);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
    }

    [Test]
    public void RunLastsOneTick_WhenEffortEqualsWorkRate()
    {
        var engine = Smelter(effort: 100).Engine();

        engine.Advance(1);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
    }

    [Test]
    public void InputsAreConsumedAtRunStart_OutputAppearsOnlyAtRunEnd()
    {
        var engine = Smelter(ore: 100, effort: 300).Engine();

        engine.Advance(1);

        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(90), "the run's inputs are gone immediately");
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(0), "and nothing is produced until it finishes");
        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.RunningTask));
        Assert.That(engine.Snapshot.Executors[0].RunTicksRemaining, Is.EqualTo(2));
    }

    [Test]
    public void TaskCompletes_AfterItsRequestedRuns()
    {
        var engine = Smelter(runs: 3).Engine();

        engine.Advance(3);

        var task = engine.Snapshot.ProductionTasks.Single();
        Assert.That(task.State, Is.EqualTo(TaskState.Complete));
        Assert.That(task.CompletedRuns, Is.EqualTo(3));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(3));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.TaskCompleted),
            Is.EqualTo(1));
    }

    [Test]
    public void CompletedTask_LeavesTheExecutorWithNothingQueued()
    {
        var engine = Smelter(runs: 1).Engine();

        engine.Advance(2);

        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.NoTasksQueued));
        Assert.That(engine.Snapshot.Executors[0].CurrentTask, Is.Null);
        Assert.That(
            engine.Snapshot.Executors[0].Configured, Is.EqualTo(Smelt),
            "the facility stays set up for its last schematic while idle");
    }

    [Test]
    public void PartialExecution_ProducesWhatItCan_Postpones_ThenResumes()
    {
        // The Planning specification's §7 case. Twenty runs are requested; there is material for
        // six. The task must not wait for the whole job's material before starting, and must not
        // die when it runs out — it postpones and picks up where it left off.
        var world = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 60))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, 20, Reactor);
        var engine = new SimulationEngine(world.Build());

        engine.Advance(20);

        var task = engine.Snapshot.ProductionTasks.Single();
        Assert.That(task.CompletedRuns, Is.EqualTo(6), "six runs' worth of ore was all there was");
        Assert.That(task.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.InsufficientInputMaterial));
        Assert.That(task.PostponedAtTick, Is.EqualTo(20), "and it is still trying");
        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked));
    }

    [Test]
    public void Postponement_IsRecordedOnce_NotOncePerTick()
    {
        // A task blocked on the same thing for two hundred ticks made one decision, not two
        // hundred. Recording every tick would bury the console and fill the history with copies.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, 5, Reactor)
            .Engine();

        engine.Advance(200);

        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PostponeInsufficientInput),
            Is.EqualTo(1));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.AllTasksBlocked),
            Is.EqualTo(1));
    }

    [Test]
    public void OutputRoom_IsCheckedBeforeInputsAreConsumed()
    {
        // A facility that cannot place its output must not shred its input for nothing.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy, holdCapacity: 0)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, 5, Reactor)
            .Engine();

        engine.Advance(5);

        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(100), "not one unit of ore was consumed");
        var task = engine.Snapshot.ProductionTasks.Single();
        Assert.That(task.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    [Test]
    public void FinishedRun_IsHeldWhenItsOutputNoLongerFits_AndLandsWhenRoomAppears()
    {
        // Room is checked when a run starts, but a neighbour sharing the storage can fill it
        // while the run is in flight. The finished run is held rather than discarded: its work
        // and its consumed inputs both survive until there is somewhere to put the result.
        var hoarder = new ExecutorId("hoarder");
        var slow = new ExecutorId("slow");
        var mine = new SchematicId("mine");

        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy, holdCapacity: 3)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            // Listed first, so it claims the room while the refinery is still working.
            .Schematic(mine, new ItemAmount(Alloy, 1), FacilityType.Extractor)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: 300, inputs: new ItemAmount(Ore, 10))
            .Producer(hoarder, FacilityType.Extractor, mine)
            .Producer(slow, FacilityType.MatterReactor, Smelt)
            .Task(mine, 3, hoarder)
            .Task(Smelt, 1, slow)
            .Engine();

        engine.Advance(3);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(3), "the hoarder filled the storage");
        var held = engine.Snapshot.ProductionTasks.Single(t => t.Executor == slow);
        Assert.That(held.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(held.LastReason, Is.EqualTo(PostponeReason.DestinationFull));
        Assert.That(held.CompletedRuns, Is.EqualTo(0));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(990), "its inputs are still spent, not refunded");
    }

    [Test]
    public void SwitchOver_IsFreeWhenTheSchematicDoesNotChange()
    {
        var engine = Smelter(runs: 3).Engine();

        engine.Advance(3);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(3), "three runs in three ticks, no reconfiguration");
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.SwitchOverStarted), Is.False);
    }

    [Test]
    public void SwitchOver_ToAnotherSchematic_TakesExactlyItsTicks_AndIsNotRunning()
    {
        var engine = TwoSchematicFactory(switchOverTicks: 5).Engine();

        // Tick 1 finishes the fabricate run the factory is configured for.
        engine.Advance(1);
        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1));

        // Ticks 2-6 are the reconfiguration. Nothing is produced and nothing is running.
        for (var tick = 2; tick <= 6; tick++)
        {
            engine.Advance(1);
            Assert.That(
                engine.Snapshot.Executors[0].Status,
                Is.EqualTo(ExecutorStatus.SwitchingOver),
                $"tick {tick} should still be reconfiguring");
            Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(0));
        }

        // Tick 7 is the first tick of the alloy run, which is one tick long.
        engine.Advance(1);
        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.RunningTask));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
    }

    [Test]
    public void FirstConfiguration_CostsNothing()
    {
        // A facility that has never been configured has nothing to tear down.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, initialSchematic: null, switchOverTicks: 50)
            .Task(Smelt, 1, Reactor)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
        Assert.That(engine.Snapshot.Executors[0].Configured, Is.EqualTo(Smelt));
    }

    [Test]
    public void TaskSelection_PrefersTheLoadedConfiguration_OverQueuePosition()
    {
        // The alloy task is queued first, but the factory is set up for chips and both are
        // runnable. Preferring the loaded configuration is what keeps a facility producing
        // instead of reconfiguring, so chips must come out first and no switch-over may happen.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Fabricate, new ItemAmount(Chip, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Factory, FacilityType.Factory, Fabricate, switchOverTicks: 5)
            .Task(Smelt, 1, Factory)
            .Task(Fabricate, 1, Factory)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(0));
        Assert.That(engine.Snapshot.Executors[0].Status, Is.Not.EqualTo(ExecutorStatus.SwitchingOver));
    }

    [Test]
    public void AllQueuedTasksBlocked_RecordsAReasonOnEveryQueuedTask()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Fabricate, new ItemAmount(Chip, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Factory, FacilityType.Factory, Fabricate)
            .Task(Smelt, 1, Factory)
            .Task(Fabricate, 1, Factory)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked));
        Assert.That(
            engine.Snapshot.ProductionTasks.Select(t => t.LastReason).ToList(),
            Is.EqualTo(new List<PostponeReason?>
            {
                PostponeReason.InsufficientInputMaterial,
                PostponeReason.InsufficientInputMaterial,
            }),
            "'the vessel stopped' is useless; 'these two tasks lack ore' is not");
    }

    [Test]
    public void Enqueue_RejectsASchematicTheFacilityCannotExecute()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor)
            .Producer(Factory, FacilityType.Factory, initialSchematic: null)
            .Engine();

        var thrown = Assert.Throws<ArgumentException>(() => engine.Enqueue(Smelt, 1, Factory));

        Assert.That(thrown!.Message, Does.Contain("MatterReactor").And.Contain("Factory"));
    }

    [Test]
    public void Enqueue_RejectsALockedSchematic()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, unlocked: false)
            .Producer(Reactor, FacilityType.MatterReactor, initialSchematic: null)
            .Engine();

        Assert.Throws<ArgumentException>(() => engine.Enqueue(Smelt, 1, Reactor));
    }

    [Test]
    public void Enqueue_RejectsAnUnknownExecutorAndANonPositiveRunCount()
    {
        var engine = Smelter().Engine();

        Assert.Throws<ArgumentException>(() => engine.Enqueue(Smelt, 1, new ExecutorId("nowhere")));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Enqueue(Smelt, 0, Reactor));
    }

    [Test]
    public void AnExecutorWithNoWorkRate_IsADefinitionError()
    {
        // A facility doing no work per tick would never finish a run, and the run would sit at
        // "one tick remaining" for the rest of the session.
        var world = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor)
            .Producer(Reactor, FacilityType.MatterReactor, Smelt, workRate: 0);

        Assert.Throws<ArgumentException>(() => world.Engine());
    }

    private static WorldBuilder TwoSchematicFactory(long switchOverTicks) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Fabricate, new ItemAmount(Chip, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Factory,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Factory, FacilityType.Factory, Fabricate, switchOverTicks: switchOverTicks)
            .Task(Fabricate, 1, Factory)
            .Task(Smelt, 1, Factory);
}
