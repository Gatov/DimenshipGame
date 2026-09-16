using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

/// <summary>
/// The draft keeps the requirement graph; flattening must reproduce the flat
/// <see cref="ProductionPlanner"/> output byte-for-byte so every existing planner test stays a
/// regression proof. See <c>2026-09-16-editable-production-plans-design.md</c>.
/// </summary>
public class PlanDraftTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId MakeChip = new("make_chip");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId RefineryB = new("refinery_b");
    private static readonly ExecutorId FeedA = new("feed_a");
    private static readonly ExecutorId ReturnA = new("return_a");
    private static readonly ExecutorId FeedB = new("feed_b");
    private static readonly ExecutorId ReturnB = new("return_b");

    private static WorldBuilder Reactor(long oreOnHand = 100) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, oreOnHand))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000);

    private static WorldBuilder TwinReactors(long oreOnHand = 10_000) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, oreOnHand))
            .Storage(BufferA, 100)
            .Storage(BufferB, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Producer(RefineryB, FacilityType.MatterReactor, Smelt, storage: BufferB)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Transport(FeedB, Hold, BufferB, 1_000)
            .Transport(ReturnB, BufferB, Hold, 1_000);

    private static DraftStep ProduceStep(PlanDraft draft) =>
        draft.Steps.Single(s => s.Work is DraftProduce);

    private static DraftStep OreFeed(PlanDraft draft) =>
        draft.Steps.Single(s => s.Work is DraftMove move && move.Item == Ore && move.To == BufferA);

    [Test]
    public void AnUnadjustedDraft_FlattensToThePlanTheFlatPlannerEmitted()
    {
        var engine = Reactor().Engine();
        var goal = new ItemAmount(Alloy, 5);
        var destination = BufferA;

        var expected = ProductionPlanner.Plan(goal, engine, destination);
        var draft = PlanDraftEditor.Create(goal, engine, destination);
        var flattened = draft.Flatten();

        Assert.That(flattened.Goal, Is.EqualTo(expected.Goal));
        Assert.That(flattened.Destination, Is.EqualTo(expected.Destination));
        Assert.That(flattened.EstimatedTicks, Is.EqualTo(expected.EstimatedTicks));
        Assert.That(flattened.Unplannable, Is.EqualTo(expected.Unplannable));
        Assert.That(flattened.Tasks, Is.EqualTo(expected.Tasks));
    }

    [Test]
    public void ADraftStepId_SurvivesTheInsertionOfANeighbour()
    {
        var engine = new WorldBuilder()
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold)
            .Storage(BufferA, 100)
            .Schematic(MakeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Alloy, 3))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Item(Ore)
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Chip, 1);
        var draft = PlanDraftEditor.Create(goal, engine);
        var alloyStep = draft.Steps.Single(s => s.Work is DraftProduce p && p.Schematic == Smelt);
        var alloyId = alloyStep.Id;

        var chipProduce = draft.Steps.Single(s => s.Work is DraftProduce p && p.Schematic == MakeChip);
        var adjusted = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(chipProduce.Id, 1));

        var sameStep = adjusted.Steps.Single(s => s.Id == alloyId);
        Assert.That(sameStep.Work, Is.TypeOf<DraftProduce>());
        Assert.That(
            adjusted.Steps.Count(s => s.Work is DraftProduce p && p.Schematic == Smelt),
            Is.EqualTo(1),
            "a neighbour insertion must not mint a second alloy step");
    }

    [Test]
    public void ALockedQuantity_SurvivesAdjustment_ReAdjustment_AndAWorldChange()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 5);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var locked = PlanDraftEditor.Adjust(
            draft, engine,
            new SetQuantity(produce.Id, 2));
        locked = PlanDraftEditor.Adjust(locked, engine, new SetLock(produce.Id, DraftField.Quantity, true));

        var readjusted = PlanDraftEditor.Adjust(locked, engine, new ReAdjust());
        var produceAfter = readjusted.Steps.Single(s => s.Id == produce.Id);
        Assert.That(((DraftProduce)produceAfter.Work).Runs, Is.EqualTo(2));
        Assert.That(produceAfter.QuantityLocked, Is.True);

        engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 80, Hold, BufferA)), FeedA);
        var afterWorld = PlanDraftEditor.Adjust(readjusted, engine, new ReAdjust());
        produceAfter = afterWorld.Steps.Single(s => s.Id == produce.Id);
        Assert.That(((DraftProduce)produceAfter.Work).Runs, Is.EqualTo(2));
    }

    [Test]
    public void ALockedExecutor_SurvivesAQueueDepthChange()
    {
        var engine = TwinReactors().Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var locked = PlanDraftEditor.Adjust(
            draft, engine, new SetExecutor(produce.Id, RefineryB));
        locked = PlanDraftEditor.Adjust(
            locked, engine, new SetLock(produce.Id, DraftField.Executor, true));

        engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 50)), RefineryA);

        var adjusted = PlanDraftEditor.Adjust(locked, engine, new ReAdjust());
        var produceAfter = adjusted.Steps.Single(s => s.Id == produce.Id);
        Assert.That(produceAfter.Executor, Is.EqualTo(RefineryB));
        Assert.That(produceAfter.ExecutorLocked, Is.True);
    }

    [Test]
    public void AnUnlockedChoice_IsRetainedWhileValid_AndReoptimisedOnlyByReAdjust()
    {
        var engine = TwinReactors().Engine();
        var goal = new ItemAmount(Alloy, 60);
        var draft = PlanDraftEditor.Create(goal, engine);
        Assert.That(ProduceStep(draft).Executor, Is.EqualTo(RefineryA));

        engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 50)), RefineryA);

        var produce = ProduceStep(draft);
        var retained = PlanDraftEditor.Adjust(
            draft, engine, new SetLock(produce.Id, DraftField.Executor, false));
        Assert.That(ProduceStep(retained).Executor, Is.EqualTo(RefineryA), "plain adjust keeps the choice");

        var reoptimised = PlanDraftEditor.Adjust(draft, engine, new ReAdjust());
        reoptimised = PlanDraftEditor.Adjust(reoptimised, engine, new ReAdjust());
        Assert.That(ProduceStep(reoptimised).Executor, Is.EqualTo(RefineryB));
    }

    [Test]
    public void AnInvalidLock_GainsAPreciseIssue_RatherThanBeingReplaced()
    {
        var engine = Reactor().Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);
        var wrong = new ExecutorId("feed_a");

        var locked = PlanDraftEditor.Adjust(
            draft, engine, new SetExecutor(produce.Id, wrong));
        locked = PlanDraftEditor.Adjust(
            locked, engine, new SetLock(produce.Id, DraftField.Executor, true));

        var produceAfter = ProduceStep(locked);
        Assert.That(produceAfter.Executor, Is.EqualTo(wrong));
        Assert.That(
            locked.Issues.Any(i => i.Kind == DraftIssueKind.IncompatibleExecutor && i.Step == produce.Id),
            Is.True);
        Assert.That(locked.IsCommittable, Is.False);
    }

    [Test]
    public void ReducingAProductionQuantity_RecalculatesOnlyItsOwnUnlockedDependencies()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(MakeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new[] { new ItemAmount(Alloy, 3), new ItemAmount(Ore, 1) })
            .Schematic(Smelt, new ItemAmount(Alloy, 5), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Chip, 1);
        var draft = PlanDraftEditor.Create(goal, engine);
        var alloyProduce = draft.Steps.Single(s => s.Work is DraftProduce p && p.Schematic == Smelt);
        var chipProduce = draft.Steps.Single(s => s.Work is DraftProduce p && p.Schematic == MakeChip);
        var chipOreFeedBefore = draft.Steps.Single(
            s => s.Key.Parent == chipProduce.Id && s.Work is DraftMove m && m.Item == Ore).Id;

        var reduced = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(alloyProduce.Id, 3));

        var alloyOre = reduced.Steps.Single(
            s => s.Key.Parent == alloyProduce.Id && s.Work is DraftMove m && m.Item == Ore);
        Assert.That(((DraftMove)alloyOre.Work).Quantity, Is.EqualTo(10), "one batch run, not two");
        Assert.That(
            reduced.Steps.Single(s => s.Id == chipOreFeedBefore).Key,
            Is.EqualTo(draft.Steps.Single(s => s.Id == chipOreFeedBefore).Key),
            "the sibling branch's ore feed is untouched");
    }

    [Test]
    public void ReducingAContribution_DoesNotRecreateAnIdenticalUnlockedStepOnTheSameExecutor()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 5);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var reduced = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(produce.Id, 2));

        var runs = reduced.Steps.Where(s => s.Work is DraftProduce p && p.Schematic == Smelt).ToList();
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(((DraftProduce)runs[0].Work).Runs, Is.EqualTo(2));
        Assert.That(runs[0].Executor, Is.EqualTo(RefineryA));
    }

    [Test]
    public void ChangingAFacility_RebuildsTheUnlockedLegsAroundItsLocalStorage()
    {
        var engine = TwinReactors().Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var moved = PlanDraftEditor.Adjust(
            draft, engine, new SetExecutor(produce.Id, RefineryB));

        var oreFeed = moved.Steps.Single(s => s.Work is DraftMove m && m.Item == Ore);
        Assert.That(((DraftMove)oreFeed.Work).To, Is.EqualTo(BufferB));
        Assert.That(oreFeed.Executor, Is.EqualTo(FeedB));
    }

    [Test]
    public void AnUnroutableFacilityChoice_IsAnIssue_OnAdjust()
    {
        // Task 4 owns Approve; here we only assert the structural issue is emitted on Adjust.
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

        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var broken = PlanDraftEditor.Adjust(
            draft, engine, new SetExecutor(produce.Id, RefineryA));
        broken = PlanDraftEditor.Adjust(broken, engine, new SetLock(produce.Id, DraftField.Executor, true));

        Assert.That(
            broken.Issues.Any(i => i.Kind == DraftIssueKind.NoExecutorOrLine || i.Kind == DraftIssueKind.NoSuchRoute),
            Is.True);
        Assert.That(broken.IsCommittable, Is.False);
    }

    [Test]
    public void ABatchRecipe_RoundsUpToWholeRuns_AndShowsItsSurplus()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 5), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Alloy, 3);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var locked = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(produce.Id, 3));
        locked = PlanDraftEditor.Adjust(locked, engine, new SetLock(produce.Id, DraftField.Quantity, true));

        var runs = ((DraftProduce)ProduceStep(locked).Work).Runs;
        Assert.That(runs, Is.EqualTo(1), "three needed from a five-batch recipe is still one run");
        Assert.That(locked.Covered, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void ALockedSurplus_SatisfiesACompatibleRequirement_InsteadOfBeingRemoved()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(MakeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Alloy, 3))
            .Schematic(Smelt, new ItemAmount(Alloy, 5), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Chip, 1);
        var draft = PlanDraftEditor.Create(goal, engine);
        var alloyProduce = draft.Steps.Single(s => s.Work is DraftProduce p && p.Schematic == Smelt);

        var locked = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(alloyProduce.Id, 3));
        locked = PlanDraftEditor.Adjust(
            locked, engine, new SetLock(alloyProduce.Id, DraftField.Quantity, true));

        var alloyRuns = locked.Steps.Count(s => s.Work is DraftProduce p && p.Schematic == Smelt);
        Assert.That(alloyRuns, Is.EqualTo(1), "surplus from the locked batch covers the chip's three alloy");
    }

    [Test]
    public void AManualMove_ReducesTheHoldMediatedLegsByWhatItSupplies()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var oreBefore = ((DraftMove)OreFeed(draft).Work).Quantity;

        var withManual = PlanDraftEditor.Adjust(
            draft, engine, new AddMove(Ore, Hold, BufferA, 10, FeedA));

        var oreAfter = ((DraftMove)OreFeed(withManual).Work).Quantity;
        Assert.That(oreAfter, Is.EqualTo(oreBefore - 10));
        Assert.That(withManual.Steps.Any(s => s.Origin == DraftOrigin.Manual), Is.True);
    }

    [Test]
    public void RemovingAManualMove_RestoresTheAutomaticallySelectedRoute()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var oreBefore = ((DraftMove)OreFeed(draft).Work).Quantity;

        var withManual = PlanDraftEditor.Adjust(
            draft, engine, new AddMove(Ore, Hold, BufferA, 10, FeedA));
        var manual = withManual.Steps.Single(s => s.Origin == DraftOrigin.Manual);

        var restored = PlanDraftEditor.Adjust(
            withManual, engine, new RemoveStep(manual.Id));

        var oreAfter = ((DraftMove)OreFeed(restored).Work).Quantity;
        Assert.That(oreAfter, Is.EqualTo(oreBefore));
        Assert.That(restored.Steps.Any(s => s.Origin == DraftOrigin.Manual), Is.False);
    }

    [Test]
    public void AManualMoveWithNoDirectLine_IsRejected()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var bogus = new ExecutorId("feed_a_2");

        var rejected = PlanDraftEditor.Adjust(
            draft, engine, new AddMove(Ore, Hold, BufferA, 10, bogus));

        Assert.That(rejected.Steps, Is.EqualTo(draft.Steps));
        Assert.That(
            rejected.Issues.Any(i => i.Kind == DraftIssueKind.NoSuchRoute),
            Is.True);
    }

    [Test]
    public void UnlockAll_ClearsLocks_AndLeavesManualRowsStanding()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var edited = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(produce.Id, 1));
        edited = PlanDraftEditor.Adjust(edited, engine, new SetLock(produce.Id, DraftField.Quantity, true));
        edited = PlanDraftEditor.Adjust(
            edited, engine, new AddMove(Ore, Hold, BufferA, 5, FeedA));
        var manual = edited.Steps.Single(s => s.Origin == DraftOrigin.Manual);

        var unlocked = PlanDraftEditor.Adjust(edited, engine, new UnlockAll());

        Assert.That(ProduceStep(unlocked).QuantityLocked, Is.False);
        Assert.That(unlocked.Steps.Any(s => s.Id == manual.Id), Is.True);
    }

    [Test]
    public void AShortfallThePlayerCreated_IsCommittable_AndNamesWhatIsUncovered()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 5);
        var draft = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(draft);

        var shortfall = PlanDraftEditor.Adjust(
            draft, engine, new SetQuantity(produce.Id, 2));
        shortfall = PlanDraftEditor.Adjust(
            shortfall, engine, new SetLock(produce.Id, DraftField.Quantity, true));

        Assert.That(shortfall.IsCommittable, Is.True);
        Assert.That(shortfall.IsComplete, Is.False);
        Assert.That(
            shortfall.Issues.Any(i => i.Kind == DraftIssueKind.GoalShortfall && i.Item == Alloy),
            Is.True);
    }

    [Test]
    public void TwoLegsOnOneRoute_CommitAsOneTask_WithTheirAvailabilitySummed()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(MakeChip, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                inputs: new[] { new ItemAmount(Alloy, 2), new ItemAmount(Ore, 5) })
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Chip, 2);
        var draft = PlanDraftEditor.Create(goal, engine);
        var plan = draft.Flatten();

        var oreLegs = draft.Steps.Where(s => s.Work is DraftMove m && m.Item == Ore && m.To == BufferA).ToList();
        Assert.That(oreLegs, Has.Count.GreaterThan(1));

        var merged = plan.Transfers().Single(t => t.Item == Ore && t.To == BufferA);
        Assert.That(
            merged.Quantity,
            Is.EqualTo(oreLegs.Sum(s => ((DraftMove)s.Work).Quantity)));
        Assert.That(
            merged.AvailableAtSource,
            Is.EqualTo(oreLegs.Sum(s => s.AvailableAtSource)));
    }

    [Test]
    public void Adjusting_ChangesNothingAboutTheWorld()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var before = engine.Snapshot;
        var draft = PlanDraftEditor.Create(new ItemAmount(Alloy, 5), engine);
        var produce = ProduceStep(draft);

        for (var i = 0; i < 5; i++)
        {
            draft = PlanDraftEditor.Adjust(
                draft, engine, new SetQuantity(produce.Id, 2 + i));
        }

        Assert.That(engine.Snapshot, Is.SameAs(before));
        Assert.That(engine.Snapshot.Tasks.Where(t => t.Action is Produce), Is.Empty);
    }

    [Test]
    public void TwoIdenticalDraftsAgainstOneWorld_AdjustIdentically()
    {
        var engine = Reactor(oreOnHand: 100).Engine();
        var goal = new ItemAmount(Alloy, 5);
        var left = PlanDraftEditor.Create(goal, engine);
        var right = PlanDraftEditor.Create(goal, engine);
        var produce = ProduceStep(left);

        var edit = new SetQuantity(produce.Id, 2);
        var adjustedLeft = PlanDraftEditor.Adjust(left, engine, edit with { Step = ProduceStep(left).Id });
        var adjustedRight = PlanDraftEditor.Adjust(
            right, engine, edit with { Step = ProduceStep(right).Id });

        Assert.That(
            adjustedRight.Steps.Select(s => (s.Key, s.Work, s.Executor, s.QuantityLocked)),
            Is.EqualTo(adjustedLeft.Steps.Select(s => (s.Key, s.Work, s.Executor, s.QuantityLocked))));
        Assert.That(adjustedRight.Issues, Is.EqualTo(adjustedLeft.Issues));
        Assert.That(adjustedRight.Covered, Is.EqualTo(adjustedLeft.Covered));
    }
}
