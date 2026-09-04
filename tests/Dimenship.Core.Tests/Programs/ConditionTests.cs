using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Programs;

/// <summary>
/// Conditions gate starting only. Empty conditions stay byte-identical to today's unconditioned
/// tasks; a false gate postpones with ConditionNotMet and retries next tick.
/// </summary>
public class ConditionTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly ExecutorId Reactor = new("reactor");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly StorageId Dock = new("dock");
    private static readonly ExecutorId Hauler = new("hauler");

    [Test]
    public void AConditionThatIsFalse_PostponesWithConditionNotMet_AndRetriesNextTick()
    {
        var engine = Smelter(ore: 1_000).Engine();
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.LessThan,
            new Literal(0));

        engine.Enqueue(new TaskScript(new[] { gate }, new Produce(Smelt, 1)), Reactor);
        engine.Advance(1);

        var task = engine.Snapshot.Tasks.Single();
        Assert.That(task.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.ConditionNotMet));
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.PostponeConditionNotMet),
            Is.True);

        engine.Advance(1);
        Assert.That(
            engine.State.Tasks.All.Single().PostponedAtTick,
            Is.EqualTo(2),
            "a false condition retries every tick rather than sticking on the first refusal");
    }

    [Test]
    public void AConditionNeverStops_ARunAlreadyInFlight()
    {
        var engine = Smelter(ore: 1_000, effort: 500).Engine();
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.GreaterThan,
            new Literal(0));

        engine.Enqueue(new TaskScript(new[] { gate }, new Produce(Smelt, 1)), Reactor);
        engine.Advance(1);

        Assert.That(engine.State.Tasks.All.Single().RunActive, Is.True, "the run should be in flight");

        // Flip the condition by withdrawing every remaining ore after the run consumed its input.
        // The in-flight run must finish; conditions never touch AdvanceRun.
        var remaining = engine.Available(Hold, Ore);
        if (remaining > 0)
        {
            engine.State.Vessel.Storages.Single(s => s.Id == Hold).Stock.Clear();
        }

        engine.Advance(10);

        Assert.That(engine.State.Tasks.All.Single().State, Is.EqualTo(TaskState.Complete));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
    }

    [Test]
    public void ConditionNotMet_LosesToEveryOtherReason()
    {
        // No ore: physical readiness fails with InsufficientInputMaterial. The gate is also false.
        // ConditionNotMet is last in declaration order, so RootCause must report the missing ore.
        var engine = Smelter(ore: 0).Engine();
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.GreaterThan,
            new Literal(10_000));

        engine.Enqueue(new TaskScript(new[] { gate }, new Produce(Smelt, 1)), Reactor);
        engine.Advance(1);

        Assert.That(
            engine.Snapshot.Tasks.Single().LastReason,
            Is.EqualTo(PostponeReason.InsufficientInputMaterial),
            "a false condition must not hide missing inputs");
    }

    [Test]
    public void Enqueue_RefusesAParameterRef()
    {
        var engine = Smelter(ore: 100).Engine();
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.GreaterThan,
            new ParameterRef("threshold"));

        var error = Assert.Throws<ArgumentException>(() =>
            engine.Enqueue(new TaskScript(new[] { gate }, new Produce(Smelt, 1)), Reactor));

        Assert.That(error!.Message, Does.Contain("parameter").IgnoreCase);
        Assert.That(engine.Snapshot.Tasks, Is.Empty);
    }

    [Test]
    public void Enqueue_RefusesAnUnknownTargetRef()
    {
        var engine = Smelter(ore: 100).Engine();
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, "no_such_storage"),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.GreaterThan,
            new Literal(0));

        var error = Assert.Throws<ArgumentException>(() =>
            engine.Enqueue(new TaskScript(new[] { gate }, new Produce(Smelt, 1)), Reactor));

        Assert.That(error!.Message, Does.Contain("no_such_storage"));
        Assert.That(
            engine.Snapshot.Tasks,
            Is.Empty,
            "a typo in a target is a planning mistake, not a task that never starts");
    }

    /// <summary>
    /// A transfer has nothing in flight between ticks — withdraw and deposit are the same tick — so
    /// every tick it moves is a fresh start and the gate applies to all of them. Gating only the
    /// first would leave the rest of a long haul unconditioned.
    /// </summary>
    [Test]
    public void AConditionedTransfer_IsGatedOnEveryTick_NotOnlyTheFirst()
    {
        var engine = Line(ore: 1_000, throughput: 10).Engine();

        // Move ore to the dock, but only while the dock holds less than 50.
        var gate = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Dock.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.LessThan,
            new Literal(50));

        engine.Enqueue(
            new TaskScript(new[] { gate }, new Transfer(Ore, 1_000, Hold, Dock)),
            Hauler);

        engine.Advance(20);

        Assert.That(
            engine.Available(Dock, Ore),
            Is.EqualTo(50),
            "the haul must stop the tick its gate goes false, not run to its full quantity");

        var task = engine.State.Tasks.All.Single();
        Assert.That(task.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.ConditionNotMet));
    }

    [Test]
    public void StorageItemAmount_ReadsLiveStock()
    {
        var state = Smelter(ore: 42).State();

        var condition = new Condition(
            ConditionKind.StorageItemAmount,
            new Operand[]
            {
                new TargetRef(TargetKind.Storage, Hold.Value),
                new TargetRef(TargetKind.Item, Ore.Value),
            },
            Comparison.Equal,
            new Literal(42));

        Assert.That(ConditionEvaluator.Met(condition, state), Is.True);

        state.Vessel.Storages.Single(s => s.Id == Hold).Stock.Clear();
        Assert.That(ConditionEvaluator.Met(condition, state), Is.False);
    }

    /// <summary>A hold, a dock and one line between them. No producer: this is about the gate.</summary>
    private static WorldBuilder Line(long ore, long throughput) =>
        new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Storage(Dock)
            .Transport(Hauler, Hold, Dock, throughput);

    private static WorldBuilder Smelter(long ore, long effort = 100) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: effort, inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, initialSchematic: null);
}
