using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// Hold, release, cancel and amend (K6c; D3, Decisions 6 and 7). Each respects what is physically
/// committed: a run in progress finishes, a switch-over completes, cargo aboard arrives, and no
/// command destroys or creates material.
/// </summary>
public class PlanCommandTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Sinter = new("sinter");
    private static readonly ExecutorId Refinery = new("refinery");
    private static readonly ExecutorId Feed = new("feed");

    /// <summary>
    /// One refinery on its own buffer, fed from the hold and returning to it. Three-tick runs, so a
    /// command can land while one is in progress, and a feed belt three ticks long, so one can land
    /// while cargo is aboard.
    /// </summary>
    private static SimulationEngine Vessel(
        long ore = 60, long switchOverTicks = 0, SchematicId? configured = null, long bufferOre = 0) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(WorldBuilder.Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Storage(Buffer, StorageArchetype.FullHold,
                bufferOre > 0 ? new[] { new ItemAmount(Ore, bufferOre) } : Array.Empty<ItemAmount>())
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Sinter, new ItemAmount(WorldBuilder.Chip, 1), FacilityType.MatterReactor, effort: 300)
            .Producer(Refinery, FacilityType.MatterReactor, configured, switchOverTicks: switchOverTicks,
                storage: Buffer)
            .Transport(Feed, Hold, Buffer, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return"), Buffer, Hold, throughputPerTick: 10)
            .Engine();

    private static CommittedPlan Commit(SimulationEngine engine, long alloy)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, alloy), engine));
        return engine.State.Plans.Plans[^1];
    }

    private static TaskInstance RunsOf(SimulationEngine engine, CommittedPlan plan) =>
        plan.SpawnedTasks.Select(id => engine.State.Tasks.Task(id)!).First(t => t.IsProduce);

    private static TaskInstance FeedOf(SimulationEngine engine, CommittedPlan plan) =>
        plan.SpawnedTasks.Select(id => engine.State.Tasks.Task(id)!).First(t => t.ExecutorId == Feed);

    private static void AdvanceUntil(SimulationEngine engine, Func<bool> condition, int limit = 200)
    {
        for (var i = 0; i < limit && !condition(); i++)
        {
            engine.Advance(1);
        }

        Assert.That(condition(), "fixture: the condition never came true");
    }

    /// <summary>Every unit aboard: in every storage, and on every belt.</summary>
    private static long Material(SimulationEngine engine, ItemId item) =>
        engine.State.Vessel.Storages.Sum(s => engine.Available(s.Id, item))
        + engine.State.Vessel.Transports.Sum(l => l.Belt.Where(slot => slot?.Item == item).Sum(slot => slot!.Quantity));

    [Test]
    public void Hold_LetsTheRunInProgressFinish_AndStartsNoOther_UntilReleased()
    {
        var engine = Vessel();
        var plan = Commit(engine, 5);
        var runs = RunsOf(engine, plan);
        AdvanceUntil(engine, () => runs.RunActive);
        var started = runs.CompletedRuns;

        engine.Hold(plan.Id);
        engine.Advance(20);

        Assert.That(runs.CompletedRuns, Is.EqualTo(started + 1), "the run in progress was stopped, or another started");
        Assert.That(runs.RunActive, Is.False);
        Assert.That(runs.LastReason, Is.EqualTo(PostponeReason.SafetyLock));
        var feed = FeedOf(engine, plan);
        Assert.That(feed.MovedQuantity, Is.EqualTo(feed.LoadedQuantity), "cargo aboard was stranded");
        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == runs.Id).Held, Is.True);

        engine.Release(plan.Id);
        engine.Advance(100);

        Assert.That(plan.State, Is.EqualTo(PlanState.Complete));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(5));
    }

    [Test]
    public void Hold_KeepsClaims_AndRelease_OffersFreeStockAtOnce()
    {
        var engine = Vessel();
        var older = Commit(engine, 5);
        var younger = Commit(engine, 5);
        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(10), "fixture");

        engine.Hold(younger.Id);
        engine.Cancel(older.Id);

        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(10), "a hold gave up its claims");
        Assert.That(engine.Free(Hold, Ore), Is.EqualTo(50), "a held plan received new stock");
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);

        engine.Release(younger.Id);

        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(50));
        Assert.That(engine.Free(Hold, Ore), Is.EqualTo(10));
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
    }

    [Test]
    public void Cancel_CutsEveryTaskBackToWhatHasStarted_AndCreatesOrDestroysNothing()
    {
        var engine = Vessel();
        var plan = Commit(engine, 5);
        var runs = RunsOf(engine, plan);
        var feed = FeedOf(engine, plan);
        AdvanceUntil(engine, () => runs.RunActive && feed.LoadedQuantity > feed.MovedQuantity);
        var ore = Material(engine, Ore);
        var alloy = Material(engine, Alloy);
        var inRun = runs.CompletedRuns + 1;

        engine.Cancel(plan.Id);

        Assert.That(Material(engine, Ore), Is.EqualTo(ore));
        Assert.That(Material(engine, Alloy), Is.EqualTo(alloy));
        Assert.That(plan.State, Is.EqualTo(PlanState.Abandoned));
        Assert.That(engine.State.Claims.Entries, Is.Empty);
        Assert.That(runs.Produce.Runs, Is.EqualTo(inRun));
        Assert.That(feed.Transfer.Quantity, Is.EqualTo(feed.LoadedQuantity));

        engine.Advance(20);

        Assert.That(plan.SpawnedTasks.Select(id => engine.State.Tasks.Task(id)!), Has.All.Matches<TaskInstance>(t => t.IsFinished));
        Assert.That(runs.CompletedRuns, Is.EqualTo(inRun), "the run in progress did not deposit");
        Assert.That(engine.Free(Buffer, Ore), Is.EqualTo(engine.Available(Buffer, Ore)), "the arrived cargo was held for a cancelled plan");
        Assert.That(Material(engine, Ore), Is.EqualTo(ore), "ore was consumed after the cancel");
        Assert.That(Material(engine, Alloy), Is.EqualTo(alloy + 1), "only the run in progress may add alloy");
        Assert.That(plan.State, Is.EqualTo(PlanState.Abandoned), "a cancelled plan finished as complete");
    }

    [Test]
    public void Cancel_DuringASwitchOverTowardIt_LetsTheSwitchOverComplete_AndRunsNothing()
    {
        var engine = Vessel(switchOverTicks: 5, configured: Sinter);
        var plan = Commit(engine, 2);
        var runs = RunsOf(engine, plan);
        var refinery = engine.State.Vessel.Facilities.Single(f => f.Id == Refinery);
        AdvanceUntil(engine, () => refinery.SwitchOverRemaining > 0);

        engine.Cancel(plan.Id);

        Assert.That(runs.IsFinished, Is.False, "the switch-over's target vanished under it");
        Assert.That(refinery.SwitchTarget, Is.EqualTo(runs.Id));

        engine.Advance(10);

        Assert.That(refinery.Configured, Is.EqualTo(Smelt), "the switch-over was abandoned");
        Assert.That(runs.IsFinished, Is.True);
        Assert.That(runs.CompletedRuns, Is.Zero);
        Assert.That(refinery.Current, Is.Null);
        Assert.That(engine.Available(Buffer, Alloy), Is.Zero);
    }

    [Test]
    public void Amend_KeepsTheIdPriorityAndHold_AndReplansTheGoal()
    {
        var engine = Vessel(ore: 200);
        var plan = Commit(engine, 5);
        engine.SetPriority(plan.Id, Priority.High);
        var runs = RunsOf(engine, plan);
        AdvanceUntil(engine, () => runs.RunActive);
        var before = plan.SpawnedTasks.Count;

        var approval = engine.Amend(plan.Id, 3);

        Assert.That(approval, Is.InstanceOf<PlanApprovalCommitted>());
        Assert.That(engine.State.Plans.Plans, Has.Count.EqualTo(1), "an amend recorded a second plan");
        Assert.That(plan.Goal, Is.EqualTo(new ItemAmount(Alloy, 3)));
        Assert.That(plan.Priority, Is.EqualTo(Priority.High));
        Assert.That(plan.SpawnedTasks, Has.Count.GreaterThan(before));
        Assert.That(runs.Produce.Runs, Is.EqualTo(runs.CompletedRuns + 1), "the old work was not cut back");
        foreach (var id in plan.SpawnedTasks.Skip(before))
        {
            Assert.That(engine.State.Plans.Owning(id), Is.SameAs(plan));
        }

        for (var i = 0; i < 200 && plan.State == PlanState.Active; i++)
        {
            engine.Advance(1);
            Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
        }

        Assert.That(plan.State, Is.EqualTo(PlanState.Complete));
        Assert.That(engine.Available(Hold, Alloy), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void TheInvariantHolds_ThroughHoldsCancelsAndAmends_OnTheShippedVessel()
    {
        // Every command, interleaved with overlapping plans on the vessel the game ships. Free stock
        // beside an eligible outstanding claim, a holding beyond its need, or a task of a cancelled
        // plan left unfinished, is a bug in the commands.
        var engine = Shipped.Engine();
        var plans = new List<PlanId>();
        void Order(ItemId item, long quantity, StorageId? to = null)
        {
            var approval = PlanDraftEditor.Approve(PlanDraftEditor.Create(new ItemAmount(item, quantity), engine, to), engine);
            engine.Commit(((PlanApprovalCommitted)approval).Plan);
            plans.Add(engine.State.Plans.Plans[^1].Id);
        }

        for (var tick = 0L; tick < 2_500; tick++)
        {
            switch (tick)
            {
                case 0: Order(DefaultVessel.Component, 2_000); break;
                case 50: Order(DefaultVessel.RobotFrame, 250, DefaultVessel.DockAHold); break;
                case 120: engine.Hold(plans[0]); break;
                case 300: Order(DefaultVessel.Module, 500, DefaultVessel.DockBHold); break;
                case 400: engine.Amend(plans[1], 500); break;
                case 600: engine.Release(plans[0]); break;
                case 700: engine.Cancel(plans[2]); break;
            }

            engine.Advance(1);
            Assert.That(engine.ClaimInvariantViolations(), Is.Empty, $"tick {engine.State.Clock.Tick}");
        }

        var cancelled = engine.State.Plans.Plans.Single(p => p.Id == plans[2]);
        Assert.That(cancelled.State, Is.EqualTo(PlanState.Abandoned));
        Assert.That(
            cancelled.SpawnedTasks.Select(id => engine.State.Tasks.Task(id)).OfType<TaskInstance>(),
            Has.All.Matches<TaskInstance>(t => t.IsFinished),
            "a cancelled plan's work never finished");
        Assert.That(engine.State.Plans.Plans.Single(p => p.Id == plans[1]).Goal.Quantity, Is.EqualTo(500));
    }

    [Test]
    public void Amend_TrimsHoldingsToTheNewNeed_AndTheSurplusGoesBackToAllocation()
    {
        var engine = Vessel();
        var older = Commit(engine, 5);
        var younger = Commit(engine, 5);

        engine.Amend(older.Id, 2);

        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.EqualTo(20));
        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(40),
            "the younger plan held ten of the fifty it needs, and takes all thirty that were freed");
        Assert.That(engine.Free(Hold, Ore), Is.Zero);
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
    }

    [Test]
    public void AHeldPlan_IsAmendedHeld()
    {
        var engine = Vessel();
        var plan = Commit(engine, 5);
        engine.Hold(plan.Id);

        engine.Amend(plan.Id, 3);
        engine.Advance(20);

        Assert.That(plan.Held, Is.True);
        Assert.That(engine.Available(Buffer, Ore), Is.Zero, "the appended work ignored the hold");
    }

    [Test]
    public void AHandQueuedTask_IsHeldReleasedAndCancelledOnItsOwn()
    {
        var engine = Vessel(bufferOre: 50);
        var task = engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 5)), Refinery);
        var instance = engine.State.Tasks.Task(task)!;

        engine.Hold(task);
        engine.Advance(5);
        Assert.That(instance.CompletedRuns, Is.Zero);
        Assert.That(instance.LastReason, Is.EqualTo(PostponeReason.SafetyLock));

        engine.Release(task);
        AdvanceUntil(engine, () => instance.CompletedRuns == 1 && instance.RunActive);
        engine.Cancel(task);
        engine.Advance(10);

        Assert.That(instance.IsFinished, Is.True);
        Assert.That(instance.CompletedRuns, Is.EqualTo(2));
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(30), "a cancel destroyed or created ore");
    }

    [Test]
    public void APlansTask_IsNeverCommandedAlone_AndTheRefusalNamesThePlan()
    {
        var engine = Vessel();
        var plan = Commit(engine, 5);
        var task = plan.SpawnedTasks[0];

        foreach (var command in new Action[] { () => engine.Hold(task), () => engine.Release(task), () => engine.Cancel(task) })
        {
            var refusal = Assert.Throws<ArgumentException>(() => command());
            Assert.That(refusal!.Message, Does.Contain($"plan '{plan.Id}'"));
        }
    }

    [Test]
    public void AHeldHandQueuedTask_SurvivesASave_AndAMissingFlagIsReported()
    {
        var catalog = Shipped.Catalog;
        var engine = Shipped.Engine();
        var queued = engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Produce(DefaultVessel.PressComponents, 3)), DefaultVessel.FactoryA);
        engine.Advance(3);
        var standing = engine.State.Tasks.Task(queued)!;
        engine.Hold(standing.Id);

        var written = WorldSave.Write(catalog, engine.State);
        var loaded = WorldSave.Read(written, catalog, new[] { Shipped.DefaultVessel });
        Assert.That(loaded.Errors, Is.Empty);
        Assert.That(loaded.State!.Tasks.Task(standing.Id)!.Held, Is.True);
        Assert.That(WorldSave.Write(catalog, loaded.State), Is.EqualTo(written));

        var tree = System.Text.Json.Nodes.JsonNode.Parse(written)!;
        foreach (var task in tree["state"]!["tasks"]!["tasks"]!.AsArray())
        {
            task!.AsObject().Remove("held");
        }

        var missing = WorldSave.Read(tree.ToJsonString(), catalog, new[] { Shipped.DefaultVessel });
        Assert.That(missing.Errors.Select(e => e.ToString()), Has.Some.Contains("needs a held flag"));

        // A version 3 save had no flag at all, and nothing could be held; it loads released.
        tree["saveVersion"] = 3;
        var upgraded = WorldSave.Read(tree.ToJsonString(), catalog, new[] { Shipped.DefaultVessel });
        Assert.That(upgraded.Errors, Is.Empty);
        Assert.That(upgraded.State!.Tasks.Task(standing.Id)!.Held, Is.False);
    }
}
