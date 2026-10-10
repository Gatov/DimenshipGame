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
    private static readonly ExecutorId Stray = new("stray");
    private static readonly StorageId StrayBuffer = new("stray_buffer");

    /// <summary>
    /// Form a blank at the factory, harden it into alloy at the reactor. Every buffer has a feed
    /// from the hold and a return to it, and one treatment line joins the factory to the reactor.
    /// <paramref name="stray"/> declares a second factory first, on the hold-star but with no line
    /// to the reactor, so declaration order alone would pick it.
    /// </summary>
    private static WorldBuilder Chain(bool blankIsWorkpiece = true, bool treatment = true, bool stray = false)
    {
        var builder = new WorldBuilder()
            .Item(Ore)
            .Item(Blank, workpiece: blankIsWorkpiece)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(FactoryBuffer, StorageArchetype.FullHold, new ItemAmount(Ore, 20))
            .Storage(ReactorBuffer)
            .Schematic(Form, new ItemAmount(Blank, 1), FacilityType.Factory, inputs: new ItemAmount(Ore, 10))
            .Schematic(Harden, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, inputs: new ItemAmount(Blank, 1));

        if (stray)
        {
            builder
                .Storage(StrayBuffer)
                .Producer(Stray, FacilityType.Factory, Form, storage: StrayBuffer)
                .Transport(new ExecutorId("stray_feed"), Hold, StrayBuffer, throughputPerTick: 10)
                .Transport(new ExecutorId("stray_return"), StrayBuffer, Hold, throughputPerTick: 10);
        }

        builder
            .Producer(Factory, FacilityType.Factory, Form, storage: FactoryBuffer)
            .Producer(Reactor, FacilityType.MatterReactor, Harden, storage: ReactorBuffer)
            .Transport(Feed, Hold, FactoryBuffer, throughputPerTick: 10);

        if (treatment)
        {
            builder.Transport(Treat, FactoryBuffer, ReactorBuffer, throughputPerTick: 10);
        }

        return builder
            .Transport(FactoryReturn, FactoryBuffer, Hold, throughputPerTick: 10)
            .Transport(ReactorFeed, Hold, ReactorBuffer, throughputPerTick: 10)
            .Transport(ReactorReturn, ReactorBuffer, Hold, throughputPerTick: 10);
    }

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
    public void OnTheShippedVessel_ABulkheadOrder_FormsTreatsAndFinishes_OverTheTreatmentLines()
    {
        // The Operations composer's APPROVE path. Factory Alpha presses and forms, Reactor Alpha
        // hardens, Alpha finishes, and no blank ever touches the hold.
        var engine = Shipped.Engine();
        var hold = engine.State.Vessel.Hold;
        var plate = new ItemId("plate_blank");
        var hardened = new ItemId("hardened_blank");
        var bulkhead = new ItemId("bulkhead");

        var draft = PlanDraftEditor.Create(new ItemAmount(bulkhead, 50), engine);
        Assert.That(draft.IsCommittable, Is.True, string.Join(", ", draft.Issues));
        Assert.That(
            draft.Steps.Where(s => s.Work is DraftMove { Item: var item } && (item == plate || item == hardened))
                .Select(s => s.Executor!.Value.Value),
            Is.EquivalentTo(new[] { "reactor_a_treat_feed", "reactor_a_treat_return" }));

        var approval = PlanDraftEditor.Approve(draft, engine);
        engine.Execute(new CommitPlan(((PlanApprovalCommitted)approval).Plan));
        engine.Advance(1500);

        Assert.That(engine.Available(hold, bulkhead), Is.EqualTo(50));
        Assert.That(
            engine.State.Vessel.Storages.Sum(s => engine.Available(s.Id, plate) + engine.Available(s.Id, hardened)),
            Is.Zero, "every blank became the bulkhead");
    }

    [Test]
    public void OnTheShippedVessel_WithReactorAlphaOnAStandingOrder_ReactorBetaTreats_OverItsOwnLines()
    {
        // Both reactors are eligible once Beta is built. Alpha's standing order makes it occupied,
        // so Beta treats, and the blanks take Beta's treatment lines both ways.
        var state = Shipped.State();
        state.Vessel.Facilities.Single(f => f.Id.Value == "reactor_b").Built = true;
        var engine = new SimulationEngine(Shipped.Catalog, state);
        engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Produce(new SchematicId("separate_basic"), null)),
            new ExecutorId("reactor_a"));

        var draft = PlanDraftEditor.Create(new ItemAmount(new ItemId("bulkhead"), 50), engine);

        Assert.That(draft.IsCommittable, Is.True, string.Join(", ", draft.Issues));
        Assert.That(
            draft.Steps.Single(s => s.Work is DraftProduce { Schematic.Value: "harden_blanks" }).Executor,
            Is.EqualTo(new ExecutorId("reactor_b")));
        Assert.That(
            draft.Steps.Where(s => s.Work is DraftMove { Item.Value: "plate_blank" or "hardened_blank" })
                .Select(s => s.Executor!.Value.Value),
            Is.EquivalentTo(new[] { "reactor_b_treat_feed", "reactor_b_treat_return" }));
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
    public void ADraft_RoutesAWorkpieceBufferToBuffer_AndEveryOrdinaryLegThroughTheHold()
    {
        var engine = Chain().Engine();

        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 1), engine);

        var moves = draft.Steps
            .Where(s => s.Work is DraftMove)
            .Select(s => ((DraftMove)s.Work, s.Executor))
            .Select(m => (m.Item1.Item, m.Item1.From, m.Item1.To, m.Executor))
            .ToList();
        Assert.That(moves, Is.EquivalentTo(new[]
        {
            (Ore, Hold, FactoryBuffer, (ExecutorId?)Feed),
            (Blank, FactoryBuffer, ReactorBuffer, (ExecutorId?)Treat),
            (Alloy, ReactorBuffer, Hold, (ExecutorId?)ReactorReturn),
        }));
        Assert.That(draft.Issues, Is.Empty);
        Assert.That(draft.IsCommittable, Is.True);
    }

    [Test]
    public void AnOrderedChain_IsDelivered_WithNoBlankEverInTheHold()
    {
        var engine = Chain().Engine();
        var approval = PlanDraftEditor.Approve(PlanDraftEditor.Create(new ItemAmount(Alloy, 2), engine), engine);
        engine.Execute(new CommitPlan(((PlanApprovalCommitted)approval).Plan));

        for (var tick = 0; tick < 100; tick++)
        {
            engine.Advance(1);
            Assert.That(engine.Available(Hold, Blank), Is.Zero);
        }

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(2));
    }

    [Test]
    public void AFactoryWithNoLineToTheReactor_IsNeverChosenToFormTheBlank()
    {
        // The stray factory is declared first and free, and Factory has a standing order, so
        // "unoccupied first" alone would pick the stray. Ranking it last by estimated finish would
        // not be enough; it is skipped, because a blank formed there could never leave.
        var engine = Chain(stray: true).Engine();
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Form, null)), Factory);

        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 1), engine);

        var form = draft.Steps.Single(s => s.Work is DraftProduce { Schematic: var schematic } && schematic == Form);
        Assert.That(form.Executor, Is.EqualTo(Factory));
        Assert.That(draft.IsCommittable, Is.True);
    }

    [Test]
    public void WithNoTreatmentLine_TheBlankHasNoProducer_AndIsNeverSentThroughTheHold()
    {
        var engine = Chain(treatment: false).Engine();

        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 1), engine);

        Assert.That(draft.Issues.Any(i => i.Kind == DraftIssueKind.WorkpieceNotAccepted), Is.False);
        Assert.That(draft.Issues.Any(i => i.Kind == DraftIssueKind.NoExecutorOrLine), Is.True);
        Assert.That(
            draft.Steps.Any(s => s.Work is DraftMove move && move.Item == Blank), Is.False,
            "no leg for a blank nothing can deliver");
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
