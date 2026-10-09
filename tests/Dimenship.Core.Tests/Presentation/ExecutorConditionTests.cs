using Dimenship.Core.Content;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// U3: a facility is blocked only by output it cannot put down. Missing input is waiting, a hold is
/// held, and a changeover says what it is loading and how long is left.
/// </summary>
public class ExecutorConditionTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly ExecutorId Reactor = new("reactor");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Cast = new("cast");

    private static WorldBuilder Reactors(long alloyRoom, long ore, long switchOverTicks = 0) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy, holdCapacity: alloyRoom)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Cast, new ItemAmount(Ore, 1), FacilityType.MatterReactor, effort: 300)
            .Producer(Reactor, FacilityType.MatterReactor, Smelt, switchOverTicks: switchOverTicks);

    [Test]
    public void AFacilityShortOfInput_IsWaiting_NotBlocked()
    {
        var engine = Reactors(alloyRoom: 1_000, ore: 0).Task(Smelt, 1, Reactor).Engine();
        engine.Advance(1);

        var condition = ExecutorCondition.For(engine.Snapshot, Reactor);

        Assert.That(engine.Snapshot.Executors.Single().Status, Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked));
        Assert.That(condition.Standing, Is.EqualTo(ExecutorStanding.Waiting));
        Assert.That(condition.Reason, Is.EqualTo(PostponeReason.InsufficientInputMaterial));
        Assert.That(condition.Setup, Is.EqualTo(Smelt));
    }

    [Test]
    public void AFacilityWithNowhereToPutItsOutput_IsBlocked()
    {
        var engine = Reactors(alloyRoom: 0, ore: 100).Task(Smelt, 1, Reactor).Engine();
        engine.Advance(1);

        var condition = ExecutorCondition.For(engine.Snapshot, Reactor);

        Assert.That(condition.Standing, Is.EqualTo(ExecutorStanding.Blocked));
        Assert.That(condition.Reason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    [Test]
    public void ATaskThatCannotDeposit_OutranksAHeldTask_ThatTheRootCauseRanksFirst()
    {
        var engine = Reactors(alloyRoom: 0, ore: 100)
            .Task(Smelt, 1, Reactor)
            .Task(Cast, 1, Reactor)
            .Engine();
        var cast = engine.Snapshot.Tasks.Single(t => ((Produce)t.Action).Schematic == Cast).Id;
        Assert.That(engine.Execute(new HoldTask(cast)), Is.InstanceOf<CommandAccepted>());

        engine.Advance(1);

        Assert.That(
            engine.Snapshot.Executors.Single().BlockReason,
            Is.EqualTo(PostponeReason.SafetyLock), "the engine's root cause is the hold");

        var condition = ExecutorCondition.For(engine.Snapshot, Reactor);
        Assert.That(condition.Standing, Is.EqualTo(ExecutorStanding.Blocked));
        Assert.That(condition.Reason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    [Test]
    public void AHeldTaskAlone_IsHeld()
    {
        var engine = Reactors(alloyRoom: 1_000, ore: 100).Task(Cast, 1, Reactor).Engine();
        var cast = engine.Snapshot.Tasks.Single().Id;
        engine.Execute(new HoldTask(cast));

        engine.Advance(1);

        var condition = ExecutorCondition.For(engine.Snapshot, Reactor);
        Assert.That(condition.Standing, Is.EqualTo(ExecutorStanding.Held));
        Assert.That(condition.Reason, Is.EqualTo(PostponeReason.SafetyLock));
    }

    [Test]
    public void AChangeover_NamesWhatItLoads_AndCountsDown_ThenWorks()
    {
        var engine = Reactors(alloyRoom: 1_000, ore: 100, switchOverTicks: 5).Task(Cast, 1, Reactor).Engine();
        engine.Advance(1);

        var changing = ExecutorCondition.For(engine.Snapshot, Reactor);
        Assert.That(changing.Standing, Is.EqualTo(ExecutorStanding.ChangingOver));
        Assert.That(changing.Setup, Is.EqualTo(Smelt), "the setup does not change until the changeover completes");
        Assert.That(changing.SwitchingTo, Is.EqualTo(Cast));
        Assert.That(changing.ChangeoverTicksRemaining, Is.EqualTo(4), "the deciding tick is the first tick of it");
        Assert.That(changing.ChangeoverTicksTotal, Is.EqualTo(5));

        engine.Advance(5);

        var working = ExecutorCondition.For(engine.Snapshot, Reactor);
        Assert.That(working.Standing, Is.EqualTo(ExecutorStanding.Working));
        Assert.That(working.Setup, Is.EqualTo(Cast));
        Assert.That(working.SwitchingTo, Is.Null);
        Assert.That(working.ChangeoverTicksRemaining, Is.Zero);
    }
}
