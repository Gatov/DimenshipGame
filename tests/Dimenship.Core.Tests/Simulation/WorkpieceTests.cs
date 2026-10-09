using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// K3 (D2 Decisions 2 and 4): a workpiece is accepted only by the buffer of a facility whose type
/// works it. A misplaced one is refused at <c>Enqueue</c> and in the draft, never at the belt head,
/// and a save holding one where it is not accepted is reported as drift.
/// </summary>
public class WorkpieceTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Blank = new("blank");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId FactoryBuffer = new("factory_buffer");
    private static readonly StorageId ReactorBuffer = new("reactor_buffer");
    private static readonly ExecutorId Factory = new("factory");
    private static readonly ExecutorId Reactor = new("reactor");
    private static readonly ExecutorId Feed = new("feed");
    private static readonly ExecutorId Treat = new("treat");
    private static readonly ExecutorId FactoryReturn = new("factory_return");
    private static readonly ExecutorId ReactorFeed = new("reactor_feed");
    private static readonly ExecutorId ReactorReturn = new("reactor_return");
    private static readonly SchematicId Form = new("form");
    private static readonly SchematicId Harden = new("harden");

    /// <summary>
    /// Form a blank at the factory, harden it into alloy at the reactor. Every buffer has a feed
    /// from the hold and a return to it, and one treatment line joins the factory to the reactor.
    /// </summary>
    private static WorldBuilder Chain(bool blankIsWorkpiece = true) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Blank, workpiece: blankIsWorkpiece)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(FactoryBuffer, StorageArchetype.FullHold, new ItemAmount(Ore, 20))
            .Storage(ReactorBuffer)
            .Schematic(Form, new ItemAmount(Blank, 1), FacilityType.Factory, inputs: new ItemAmount(Ore, 10))
            .Schematic(Harden, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, inputs: new ItemAmount(Blank, 1))
            .Producer(Factory, FacilityType.Factory, Form, storage: FactoryBuffer)
            .Producer(Reactor, FacilityType.MatterReactor, Harden, storage: ReactorBuffer)
            .Transport(Feed, Hold, FactoryBuffer, throughputPerTick: 10)
            .Transport(Treat, FactoryBuffer, ReactorBuffer, throughputPerTick: 10)
            .Transport(FactoryReturn, FactoryBuffer, Hold, throughputPerTick: 10)
            .Transport(ReactorFeed, Hold, ReactorBuffer, throughputPerTick: 10)
            .Transport(ReactorReturn, ReactorBuffer, Hold, throughputPerTick: 10);

    private static TaskScript Move(ItemId item, long quantity, StorageId from, StorageId to) =>
        new(Array.Empty<Condition>(), new Transfer(item, quantity, from, to));

    private static TaskScript Make(SchematicId schematic, int runs) =>
        new(Array.Empty<Condition>(), new Produce(schematic, runs));

    [Test]
    public void NoShippedItemIsAWorkpiece_SoEveryStorageAboardStillAcceptsEverything()
    {
        // D2 Decision 1: K3 lands behaviour-neutral, so the M3 baseline stays valid until K4 adds
        // the chain. The replay reports were compared byte for byte when K3 landed; this pins why.
        var engine = SimulationEngine.NewGame(Shipped.Catalog, Shipped.DefaultVessel);

        Assert.That(Shipped.Catalog.Items.Where(i => i.Workpiece), Is.Empty);
        foreach (var storage in engine.State.Vessel.Storages)
        {
            foreach (var item in Shipped.Catalog.Items)
            {
                Assert.That(engine.Acceptance.Accepts(storage.Id, item.Id), Is.True, $"{item.Id} at {storage.Id}");
            }
        }
    }

    [Test]
    public void AWorkpiece_IsAcceptedOnlyByTheBuffersOfTheFacilityTypesThatWorkIt()
    {
        var engine = Chain().Engine();

        Assert.That(engine.Acceptance.Accepts(FactoryBuffer, Blank), Is.True, "a factory schematic makes it");
        Assert.That(engine.Acceptance.Accepts(ReactorBuffer, Blank), Is.True, "a reactor schematic consumes it");
        Assert.That(engine.Acceptance.Accepts(Hold, Blank), Is.False, "the hold is no facility's buffer");
        Assert.That(engine.Acceptance.Accepts(Hold, Ore), Is.True, "an ordinary item goes anywhere");
    }

    [Test]
    public void ATransferOfAWorkpieceToTheHold_IsRefusedAtEnqueue_AndNothingIsQueued()
    {
        var engine = Chain().Engine();
        var before = engine.Snapshot.Tasks.Count;

        var refusal = Assert.Throws<ArgumentException>(
            () => engine.Enqueue(Move(Blank, 1, FactoryBuffer, Hold), FactoryReturn));

        Assert.That(refusal!.Message, Does.Contain("is a workpiece"));
        Assert.That(engine.Snapshot.Tasks, Has.Count.EqualTo(before));
        Assert.That(
            () => engine.Enqueue(Move(Blank, 1, FactoryBuffer, ReactorBuffer), Treat),
            Throws.Nothing, "the treatment line goes to a buffer that works it");
    }

    [Test]
    public void ThereIsNoRoomForAWorkpiece_WhereItIsNotAccepted()
    {
        var engine = Chain().Engine();

        Assert.That(engine.Room(Hold, Blank), Is.Zero);
        Assert.That(engine.RoomForDelivery(Hold, Blank), Is.Zero);
        Assert.That(engine.Room(ReactorBuffer, Blank), Is.GreaterThan(0));
        Assert.That(engine.Room(Hold, Ore), Is.GreaterThan(0));
    }

    [Test]
    public void TheChainRuns_BufferToBuffer_WhenQueuedByHand()
    {
        var engine = Chain().Engine();
        engine.Enqueue(Make(Form, 1), Factory);
        engine.Enqueue(Move(Blank, 1, FactoryBuffer, ReactorBuffer), Treat);
        engine.Enqueue(Make(Harden, 1), Reactor);

        engine.Advance(20);

        Assert.That(engine.Available(ReactorBuffer, Alloy), Is.EqualTo(1));
        Assert.That(engine.Available(FactoryBuffer, Blank) + engine.Available(ReactorBuffer, Blank), Is.Zero);
    }

    [Test]
    public void ADraftThatWouldRouteAWorkpieceThroughTheHold_CannotBeApproved()
    {
        var engine = Chain().Engine();

        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 1), engine);

        var misplaced = draft.Issues.Where(i => i.Kind == DraftIssueKind.WorkpieceNotAccepted).ToList();
        Assert.That(misplaced, Is.Not.Empty, "the hold-routed legs carry the blank through the hold");
        Assert.That(misplaced.All(i => i.Item == Blank && i.Step is not null), Is.True);
        Assert.That(draft.IsCommittable, Is.False);
    }

    [Test]
    public void AGoalThatWouldLeaveAWorkpieceInTheHold_CannotBeApproved()
    {
        var engine = Chain().Engine();

        var draft = PlanDraftEditor.Create(new ItemAmount(Blank, 1), engine);

        Assert.That(
            draft.Issues.Any(i => i.Kind == DraftIssueKind.WorkpieceNotAccepted && i.Step is null),
            Is.True, "the goal itself, not only its legs");
        Assert.That(draft.IsCommittable, Is.False);
    }

    [Test]
    public void TheSameDraft_WithTheItemOrdinary_HasNoSuchIssue()
    {
        var engine = Chain(blankIsWorkpiece: false).Engine();

        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 1), engine);

        Assert.That(draft.Issues.Any(i => i.Kind == DraftIssueKind.WorkpieceNotAccepted), Is.False);
        Assert.That(draft.IsCommittable, Is.True);
    }

    [Test]
    public void ASaveHoldingAWorkpieceWhereItIsNotAccepted_IsReportedAsDrift_EveryReference()
    {
        // Written while the blank was ordinary: one in the hold, and a transfer still queued to it.
        var builder = Chain(blankIsWorkpiece: false);
        var engine = builder.Engine();
        engine.Enqueue(Make(Form, 2), Factory);
        engine.Enqueue(Move(Blank, 1, FactoryBuffer, Hold), FactoryReturn);
        engine.Advance(10);
        Assert.That(engine.Available(Hold, Blank), Is.EqualTo(1), "the blank reached the hold while it was ordinary");
        engine.Enqueue(Move(Blank, 1, FactoryBuffer, Hold), FactoryReturn);
        var written = WorldSave.Write(engine.Catalog, engine.State);

        // Then the catalog made it a workpiece.
        var catalog = engine.Catalog with
        {
            Items = engine.Catalog.Items.Select(i => i.Id == Blank ? i with { Workpiece = true } : i).ToList(),
        };

        var result = WorldSave.Read(written, catalog, new[] { builder.Scenario() });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(
            result.Errors.Count(e => e.Path.StartsWith("vessel.storages")), Is.EqualTo(1), "the stock in the hold");
        Assert.That(
            result.Errors.Count(e => e.Path.StartsWith("tasks.tasks")), Is.EqualTo(2), "both transfers to the hold");
        Assert.That(result.Errors.All(e => e.Message.Contains("is a workpiece")), Is.True,
            string.Join("\n", result.Errors.Select(e => e.ToString())));
    }
}
