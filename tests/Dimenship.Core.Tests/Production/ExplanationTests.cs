using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// Explanations (K8; D3, Decision 4): a waiting task names who it waits behind, and a plan starving
/// behind another for an operational hour raises an alert that names it. Nothing is corrected.
/// </summary>
public class ExplanationTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Etch = new("etch");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId RefineryB = new("refinery_b");

    /// <summary>Two refineries, each fed from the hold by its own line, sharing fifty ore.</summary>
    private static SimulationEngine TwoRefineries() =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 50))
            .Storage(BufferA)
            .Storage(BufferB)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, null, storage: BufferA)
            .Producer(RefineryB, FacilityType.MatterReactor, null, storage: BufferB)
            .Transport(new ExecutorId("feed_a"), Hold, BufferA, throughputPerTick: 10)
            .Transport(new ExecutorId("feed_b"), Hold, BufferB, throughputPerTick: 10)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Transport(new ExecutorId("return_b"), BufferB, Hold, throughputPerTick: 10)
            .Engine();

    /// <summary>
    /// One etcher making chips from nothing, ten ticks a run, and a line home. Work that is always
    /// ready, so priority alone decides who runs.
    /// </summary>
    private static SimulationEngine Etcher() =>
        new WorldBuilder()
            .Item(Chip)
            .Storage(Hold)
            .Storage(BufferA)
            .Schematic(Etch, new ItemAmount(Chip, 1), FacilityType.Factory, effort: 1000)
            .Producer(RefineryA, FacilityType.Factory, Etch, storage: BufferA)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Engine();

    private static CommittedPlan Commit(SimulationEngine engine, ItemAmount goal)
    {
        engine.Commit(ProductionPlanner.Plan(goal, engine));
        return engine.State.Plans.Plans[^1];
    }

    [Test]
    public void ARepairWaitingOnComponentsClaimedByAnUpgrade_NamesTheUpgrade()
    {
        // The design's own example (§5): "a repair waited because its components were allocated
        // to an upgrade". The ore stands in for the components.
        var engine = TwoRefineries();
        var upgrade = Commit(engine, new ItemAmount(Alloy, 5));
        var repair = Commit(engine, new ItemAmount(Alloy, 5));
        Assert.That(engine.Free(Hold, Ore), Is.Zero, "fixture: the upgrade holds every unit");

        engine.Advance(1);

        var waiting = engine.Snapshot.Tasks.Single(t =>
            repair.SpawnedTasks.Contains(t.Id) && t.LastReason == PostponeReason.MaterialClaimed);
        Assert.That(waiting.WaitingOnPlan, Is.EqualTo(upgrade.Id));

        var cause = WaitCause.For(engine.Snapshot, waiting.Id)!;
        Assert.That(cause.Plan, Is.EqualTo(upgrade.Id));
        Assert.That(cause.Text, Does.Contain($"held for plan {upgrade.Id}"));

        var reported = engine.Snapshot.RecentEvents.Last(e => e.Code == EventCode.PostponeMaterialClaimed);
        Assert.That(reported.Data["plan"], Is.EqualTo(upgrade.Id.Value));
        Assert.That(reported.Data["task"], Is.EqualTo(waiting.Id.Value));
    }

    [Test]
    public void AnOutrankedTask_NamesTheTaskChosenInstead()
    {
        var engine = Etcher();
        var routine = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Etch, 5)), RefineryA);
        var urgent = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Etch, 5)), RefineryA);
        engine.SetPriority(urgent, Priority.High);

        engine.Advance(1);

        var waiting = engine.Snapshot.Tasks.Single(t => t.Id == routine);
        Assert.That(waiting.LastReason, Is.EqualTo(PostponeReason.Outranked));
        Assert.That(waiting.WaitingOnTask, Is.EqualTo(urgent));
        Assert.That(WaitCause.For(engine.Snapshot, routine)!.Text, Does.Contain($"task {urgent}, queued by hand at High"));
        Assert.That(engine.State.Tasks.Task(routine)!.History[^1].ByTask, Is.EqualTo(urgent));
    }

    [Test]
    public void AReasonWithNoOtherParty_ExplainsNothingFurther()
    {
        var engine = TwoRefineries();
        var queued = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), RefineryA);

        engine.Advance(1);

        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == queued).LastReason, Is.EqualTo(PostponeReason.InsufficientInputMaterial));
        Assert.That(WaitCause.For(engine.Snapshot, queued), Is.Null);
    }

    [Test]
    public void APlanStarvedBehindAnotherForAnHour_RaisesAnAlertNamingIt_AndItClearsWhenWorkResumes()
    {
        var engine = Etcher();
        var urgent = Commit(engine, new ItemAmount(Chip, 600));
        var routine = Commit(engine, new ItemAmount(Chip, 5));
        engine.SetPriority(urgent.Id, Priority.High);

        engine.Advance(SimulationEngine.WaitingPlanAlertTicks - 1);
        Assert.That(engine.Snapshot.Alerts, Is.Empty, "raised before the threshold");

        engine.Advance(1);
        var alert = engine.Snapshot.Alerts.Single();
        Assert.That(alert.Code, Is.EqualTo(AlertCode.PlanWaiting));
        Assert.That(alert.Severity, Is.EqualTo(AlertSeverity.Warning));
        Assert.That(alert.SubjectId, Is.EqualTo($"plan:{routine.Id}"));
        Assert.That(alert.RelatedSubjectId, Is.EqualTo($"plan:{urgent.Id}"));
        Assert.That(engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.AlertRaised));

        // Nothing is corrected: the urgent plan still runs, and the routine plan still waits.
        engine.Advance(100);
        Assert.That(routine.LastProgressAtTick, Is.EqualTo(routine.CommittedAtTick));

        engine.Cancel(urgent.Id);
        engine.Advance(20);

        Assert.That(engine.Snapshot.Alerts, Is.Empty);
        Assert.That(engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.AlertCleared));
    }

    [Test]
    public void AHeldPlan_RaisesNoWaitingAlert()
    {
        // The player stopped it; an alert that it is not moving would be noise.
        var engine = Etcher();
        var urgent = Commit(engine, new ItemAmount(Chip, 600));
        var routine = Commit(engine, new ItemAmount(Chip, 5));
        engine.SetPriority(urgent.Id, Priority.High);
        engine.Advance(SimulationEngine.WaitingPlanAlertTicks + 10);
        Assert.That(engine.Snapshot.Alerts, Has.Count.EqualTo(1), "fixture");

        engine.Hold(routine.Id);
        engine.Advance(1);

        Assert.That(engine.Snapshot.Alerts, Is.Empty);
    }

    [Test]
    public void CausesAndAlerts_SurviveASave_AndTheWorldRunsOnIdentically()
    {
        var catalog = new WorldBuilder()
            .Item(Chip)
            .Storage(Hold)
            .Storage(BufferA)
            .Schematic(Etch, new ItemAmount(Chip, 1), FacilityType.Factory, effort: 1000)
            .Producer(RefineryA, FacilityType.Factory, Etch, storage: BufferA)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10);
        var engine = catalog.Engine();
        var urgent = Commit(engine, new ItemAmount(Chip, 600));
        Commit(engine, new ItemAmount(Chip, 5));
        engine.SetPriority(urgent.Id, Priority.High);
        engine.Advance(SimulationEngine.WaitingPlanAlertTicks + 10);
        Assert.That(engine.State.Alerts.Alerts, Has.Count.EqualTo(1), "fixture");

        var written = WorldSave.Write(catalog.Catalog(), engine.State);
        var loaded = new SimulationEngine(
            catalog.Catalog(), WorldSave.Read(written, catalog.Catalog(), new[] { catalog.Scenario() }).State!);
        Assert.That(WorldSave.Write(catalog.Catalog(), loaded.State), Is.EqualTo(written));

        engine.Advance(500);
        loaded.Advance(500);
        Assert.That(WorldSave.Write(catalog.Catalog(), loaded.State), Is.EqualTo(WorldSave.Write(catalog.Catalog(), engine.State)));
    }
}
