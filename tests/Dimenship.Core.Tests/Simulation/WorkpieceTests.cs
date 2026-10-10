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
/// and a save holding one where it is not accepted is reported as drift. The shipped vessel's tests
/// are K4's: its chain, its treatment lines and where the blanks may be.
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
    public void OnTheShippedVessel_TheBlanksAreAcceptedByFactoryAndReactorBuffers_AndNowhereElse()
    {
        // D2 Decision 2's table: the hold, the extractor and the pads refuse both workpieces, and
        // every factory and reactor buffer takes both, because acceptance is by facility type.
        var engine = Shipped.Engine();
        var workpieces = Shipped.Catalog.Items.Where(i => i.Workpiece).Select(i => i.Id).ToList();
        var treating = new HashSet<string>
        {
            "reactor_a_buffer", "reactor_b_buffer", "factory_a_buffer", "factory_b_buffer", "factory_c_buffer",
        };

        Assert.That(workpieces, Is.EqualTo(new[] { new ItemId("plate_blank"), new ItemId("hardened_blank") }));
        foreach (var storage in engine.State.Vessel.Storages)
        {
            foreach (var item in Shipped.Catalog.Items)
            {
                var expected = !item.Workpiece || treating.Contains(storage.Id.Value);
                Assert.That(engine.Acceptance.Accepts(storage.Id, item.Id), Is.EqualTo(expected), $"{item.Id} at {storage.Id}");
            }
        }
    }

    [Test]
    public void OnTheShippedVessel_EveryWorkpieceMade_HasABuiltRouteFromAProducerToAConsumer()
    {
        // D2's K4 test. A workpiece with no built line from where it is made to where it is used
        // would be made and then stranded in a buffer for good.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();
        var unlocked = state.Progress.UnlockedSchematics.Select(s => catalog.Schematics.Get(s)).ToList();

        IEnumerable<StorageId> BuffersOf(FacilityType type) =>
            state.Vessel.Facilities.Where(f => catalog.Facility(f.Archetype)!.Type == type).Select(f => f.LocalStorage);

        foreach (var producer in unlocked.Where(s => catalog.Item(s.Output.Item)!.Workpiece))
        {
            var item = producer.Output.Item;
            var from = BuffersOf(producer.RequiredFacilityType).ToHashSet();
            var to = unlocked.Where(s => s.Inputs.Any(i => i.Item == item))
                .SelectMany(s => BuffersOf(s.RequiredFacilityType))
                .ToHashSet();

            Assert.That(
                state.Vessel.Transports.Any(t => t.Built && from.Contains(t.From) && to.Contains(t.To)),
                Is.True, $"{item} has no built line from a buffer that makes it to one that uses it");
        }
    }

    [Test]
    public void OnTheShippedVessel_TheChainRuns_FormTreatFinish_WhenQueuedByHand()
    {
        // The planner still routes every leg through the hold, so the draft refuses a bulkhead
        // (WorkpieceNotAccepted) until buffer-to-buffer planning exists. Queued by hand, the whole
        // revisit runs: Factory Alpha presses and forms, Reactor Alpha hardens, Alpha finishes.
        var engine = Shipped.Engine();
        var hold = engine.State.Vessel.Hold;
        var factory = new StorageId("factory_a_buffer");
        var reactor = new StorageId("reactor_a_buffer");
        var plate = new ItemId("plate_blank");
        var hardened = new ItemId("hardened_blank");
        var bulkhead = new ItemId("bulkhead");

        Assert.That(PlanDraftEditor.Create(new ItemAmount(bulkhead, 50), engine).IsCommittable, Is.False);

        engine.Enqueue(Move(new ItemId("basic_metals"), 400, hold, factory), new ExecutorId("factory_a_feed"));
        engine.Enqueue(Make(new SchematicId("press_components"), 1), new ExecutorId("factory_a"));
        engine.Enqueue(Make(new SchematicId("form_blanks"), 1), new ExecutorId("factory_a"));
        engine.Enqueue(Move(plate, 100, factory, reactor), new ExecutorId("reactor_a_treat_feed"));
        engine.Enqueue(Make(new SchematicId("harden_blanks"), 1), new ExecutorId("reactor_a"));
        engine.Enqueue(Move(hardened, 100, reactor, factory), new ExecutorId("reactor_a_treat_return"));
        engine.Enqueue(Make(new SchematicId("assemble_bulkheads"), 1), new ExecutorId("factory_a"));
        engine.Enqueue(Move(bulkhead, 50, factory, hold), new ExecutorId("factory_a_return"));

        engine.Advance(1500);

        Assert.That(engine.Available(hold, bulkhead), Is.EqualTo(50));
        Assert.That(
            engine.State.Vessel.Storages.Sum(s => engine.Available(s.Id, plate) + engine.Available(s.Id, hardened)),
            Is.Zero, "every blank became the bulkhead");
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
