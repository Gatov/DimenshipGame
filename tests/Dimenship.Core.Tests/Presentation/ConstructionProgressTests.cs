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
/// <see cref="ConstructionProgress"/> against hand-composed plans rather than the real
/// <see cref="ProductionPlanner"/> output. A committed plan's tasks are what the projection reads;
/// the planner is only one way to produce that shape, and composing the tasks directly lets each
/// test hold every other task fixed and move exactly the one thing its name describes.
/// <para>
/// In particular, a plan here often carries only a <see cref="Produce"/> task, or only a
/// <see cref="Transfer"/>, rather than the full three-or-four-task shape a real Build plan
/// commits. A realistic plan's own delivery leg starts attempting — and reporting
/// <see cref="PostponeReason.InsufficientSourceMaterial"/> — from the very first tick, before the
/// unit exists to move; <see cref="BlockedOutranksProgress_BecauseTheCauseIsWhatTheCardOwes"/>
/// exercises exactly that combination on purpose. The other tests isolate one phase at a time by
/// giving the plan only the task that phase reads from.
/// </para>
/// </summary>
public class ConstructionProgressTests
{
    private static readonly ItemId Unit = new("unit");
    private static readonly ItemId Metal = new("metal");
    private static readonly ItemId FillerItem = new("filler_item");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId SlotStorage = new("slot_storage");
    private static readonly StorageId SlotBStorage = new("slot_b_storage");
    private static readonly ExecutorId Factory = new("factory");
    private static readonly ExecutorId Slot = new("slot");
    private static readonly ExecutorId SlotB = new("slot_b");
    private static readonly ExecutorId Supply = new("supply");
    private static readonly ExecutorId SupplyB = new("supply_b");
    private static readonly ExecutorId Filler = new("filler");
    private static readonly SchematicId Assemble = new("assemble_unit");
    private static readonly SchematicId FillerSchematic = new("filler_schematic");

    private static PlannedTask ProduceTask(SchematicId schematic, int runs, ExecutorId executor) =>
        new(new TaskScript(Array.Empty<Condition>(), new Produce(schematic, runs)), executor, 0);

    private static PlannedTask TransferTask(
        ItemId item, long quantity, StorageId from, StorageId to, ExecutorId executor) =>
        new(new TaskScript(Array.Empty<Condition>(), new Transfer(item, quantity, from, to)), executor, 0);

    /// <summary>Commits a plan built from exactly the tasks given, and returns its minted id.</summary>
    private static PlanId Commit(SimulationEngine engine, StorageId destination, params PlannedTask[] tasks)
    {
        engine.Commit(new ProductionPlan(
            new ItemAmount(Unit, 1000), destination, tasks, Array.Empty<Unplannable>(), EstimatedTicks: 1));
        return engine.Snapshot.Plans[^1].Id;
    }

    [Test]
    public void ASlotWithNoPlan_ReadsUnplanned()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Engine();

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Unplanned));
        Assert.That(progress.Plan, Is.Null);
        Assert.That(progress.BlockedReason, Is.Null);
    }

    [Test]
    public void AnUnstartedPlan_ReadsQueued()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory)
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(Supply, Hold, SlotStorage, throughputPerTick: 1000)
            .Engine();

        var plan = Commit(
            engine, SlotStorage,
            ProduceTask(Assemble, 1, Factory),
            TransferTask(Unit, 1000, Hold, SlotStorage, Supply));

        // No tick has run: nothing has been attempted yet, which is what "queued" means.
        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Queued));
        Assert.That(progress.Plan, Is.EqualTo(plan));
        Assert.That(progress.BlockedReason, Is.Null);
    }

    [Test]
    public void TheFactoryRunning_ReadsProducingTheUnit()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory, effort: 500)
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Engine();

        // No delivery task yet: this plan is only the run, so the run in progress is the only
        // thing that can be read back from it.
        var plan = Commit(engine, SlotStorage, ProduceTask(Assemble, 1, Factory));
        engine.Advance(1);

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.ProducingUnit));
        Assert.That(progress.Plan, Is.EqualTo(plan));
        Assert.That(progress.BlockedReason, Is.Null);
    }

    [Test]
    public void TheUnitOnTheBelt_ReadsInTransit()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold, initial: new ItemAmount(Unit, 1000))
            .Storage(SlotStorage)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(Supply, Hold, SlotStorage, throughputPerTick: 1000)
            .Engine();

        var plan = Commit(engine, SlotStorage, TransferTask(Unit, 1000, Hold, SlotStorage, Supply));
        engine.Advance(1);

        // One tick is unload-then-advance-then-load: the whole unit is picked up onto the belt
        // this tick and cannot land before the next one, so Loaded is ahead of Moved right now.
        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.InTransit));
        Assert.That(progress.Plan, Is.EqualTo(plan));
        Assert.That(progress.BlockedReason, Is.Null);
    }

    [Test]
    public void ACommissionedSlot_ReadsComplete()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage, initial: new ItemAmount(Unit, 1000))
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Engine();

        // No plan was ever committed for this slot: commissioning reads straight from local
        // storage, so Built can go true with nothing in Plans naming it.
        engine.Advance(1);

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Complete));
        Assert.That(progress.Plan, Is.Null);
        Assert.That(progress.BlockedReason, Is.Null);
    }

    [Test]
    public void ABlockedPlan_NamesItsRootCause_AndItsPlan()
    {
        var needsMetal = new SchematicId("needs_metal");
        var engine = new WorldBuilder()
            .Item(Unit)
            .Item(Metal)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Schematic(
                needsMetal, new ItemAmount(Unit, 1000), FacilityType.Factory,
                inputs: new ItemAmount(Metal, 500))
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Engine();

        // Metal never arrives, so the run can never start: a single, specific, checkable cause.
        var plan = Commit(engine, SlotStorage, ProduceTask(needsMetal, 1, Factory));
        engine.Advance(1);

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Blocked));
        Assert.That(progress.BlockedReason, Is.EqualTo(PostponeReason.InsufficientInputMaterial));
        Assert.That(progress.Plan, Is.EqualTo(plan));
    }

    [Test]
    public void BlockedOutranksProgress_BecauseTheCauseIsWhatTheCardOwes()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory, effort: 500)
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(Supply, Hold, SlotStorage, throughputPerTick: 1000)
            .Engine();

        // The realistic shape: a run in progress *and* the plan's own delivery leg, queued from
        // the start, finding nothing yet to carry. The run is genuinely running; the schematic
        // must still report the delivery's postponement, because that is what the card owes the
        // player before it owes them a progress reading.
        var plan = Commit(
            engine, SlotStorage,
            ProduceTask(Assemble, 1, Factory),
            TransferTask(Unit, 1000, Hold, SlotStorage, Supply));
        engine.Advance(1);

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Blocked));
        Assert.That(progress.BlockedReason, Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
        Assert.That(progress.Plan, Is.EqualTo(plan));
    }

    [Test]
    public void TwoSlotsWithTwoPlans_DoNotReadEachOthersPhase()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Storage(SlotBStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory, effort: 500)
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Producer(
                SlotB, FacilityType.MissionDock, null,
                storage: SlotBStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(SupplyB, Hold, SlotBStorage, throughputPerTick: 1000)
            .Engine();

        var planA = Commit(engine, SlotStorage, ProduceTask(Assemble, 1, Factory));
        var planB = Commit(engine, SlotBStorage, TransferTask(Unit, 1000, Hold, SlotBStorage, SupplyB));
        engine.Advance(1);

        var progressA = ConstructionProgress.For(engine.Snapshot, Slot);
        var progressB = ConstructionProgress.For(engine.Snapshot, SlotB);

        Assert.That(progressA.Phase, Is.EqualTo(ConstructionPhase.ProducingUnit));
        Assert.That(progressA.Plan, Is.EqualTo(planA));

        // Slot B's line has nothing to carry (nothing has landed in Hold yet), which is a fact
        // about slot B's own plan and must not leak into, or borrow from, slot A's reading.
        Assert.That(progressB.Phase, Is.EqualTo(ConstructionPhase.Blocked));
        Assert.That(progressB.BlockedReason, Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
        Assert.That(progressB.Plan, Is.EqualTo(planB));
    }

    [Test]
    public void ARetiredTask_ReadsComplete_NotMissing()
    {
        var engine = new WorldBuilder()
            .Item(Unit)
            .Item(FillerItem)
            .Storage(Hold)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory)
            .Schematic(FillerSchematic, new ItemAmount(FillerItem, 1), FacilityType.Factory)
            .Producer(Factory, FacilityType.Factory, null)
            .Producer(Filler, FacilityType.Factory, null)
            .Producer(
                Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Engine();

        // This plan's only task finishes in one tick (default effort/work rate both 100) and
        // retires, without ever delivering into the slot: the slot stays unbuilt, but the plan's
        // own task list now names an id the registry will, eventually, forget.
        var plan = Commit(engine, SlotStorage, ProduceTask(Assemble, 1, Factory));
        var originalTask = engine.Snapshot.Plans[^1].SpawnedTasks[0];
        engine.Advance(1);

        // Retire enough unrelated tasks — well past the registry's 512-entry window — that the
        // plan's own task ages out and is forgotten. Every one of these completes in a single
        // tick too, so 520 of them take 520 ticks, sequentially, on a facility of their own.
        for (var i = 0; i < 520; i++)
        {
            engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(FillerSchematic, 1)), Filler);
        }

        engine.Advance(600);

        // The precondition this test is actually about: proves the task is gone, not merely
        // resolvable-and-Complete, so the assertions below exercise the "missing id" branch and
        // not the ordinary one.
        Assert.That(
            engine.Snapshot.Tasks.Any(t => t.Id == originalTask), Is.False,
            "the original task should have aged out of the retired window by now");

        var progress = ConstructionProgress.For(engine.Snapshot, Slot);

        Assert.That(progress.Phase, Is.EqualTo(ConstructionPhase.Complete));
        Assert.That(progress.Plan, Is.EqualTo(plan));
        Assert.That(progress.BlockedReason, Is.Null);
    }
}
