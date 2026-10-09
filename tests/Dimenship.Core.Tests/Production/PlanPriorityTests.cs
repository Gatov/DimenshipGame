using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// The committed plan as the runtime objective (K6a; D3, Decisions 1 and 4): its priority is the
/// one its tasks read, its held flag stops its unstarted work, and power on a starved tick goes by
/// priority rather than by where a facility happens to sit in the declaration.
/// </summary>
public class PlanPriorityTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Forge = new("forge");
    private static readonly ExecutorId First = new("first");
    private static readonly ExecutorId Second = new("second");

    private static WorldBuilder Smelter(long effort = 100, long energy = 0, long capacity = 1_000_000) =>
        new WorldBuilder()
            .Energy(capacity)
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 10_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: effort, energy: energy, inputs: new ItemAmount(Ore, 10))
            .Schematic(Forge, new ItemAmount(Chip, 1), FacilityType.MatterReactor,
                effort: effort, energy: energy, inputs: new ItemAmount(Ore, 10))
            .Producer(First, FacilityType.MatterReactor, null);

    private static CommittedPlan CommitAlloy(SimulationEngine engine, long quantity)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, quantity), engine));
        return engine.State.Plans.Plans[^1];
    }

    [Test]
    public void APlanTask_ReadsItsPlansPriority_Live()
    {
        var engine = Smelter().Engine();
        var plan = CommitAlloy(engine, 3);

        engine.SetPriority(plan.Id, Priority.High);

        Assert.That(plan.Priority, Is.EqualTo(Priority.High));
        Assert.That(engine.Snapshot.Plans.Single().Priority, Is.EqualTo(Priority.High));
        foreach (var id in plan.SpawnedTasks)
        {
            Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == id).Priority, Is.EqualTo(Priority.High));
        }

        engine.SetPriority(plan.Id, Priority.Low);

        Assert.That(
            engine.Snapshot.Tasks.Where(t => plan.SpawnedTasks.Contains(t.Id)).Select(t => t.Priority),
            Is.All.EqualTo(Priority.Low),
            "lowering the plan did not reach its tasks");
    }

    [Test]
    public void SettingAPlanTasksPriorityDirectly_IsRefused_NamingThePlan()
    {
        // A per-task override inside a plan is exactly the inadequate promotion the design names:
        // raise the final assembly and leave its prerequisites behind.
        var engine = Smelter().Engine();
        var plan = CommitAlloy(engine, 3);

        var refused = Assert.Throws<ArgumentException>(
            () => engine.SetPriority(plan.SpawnedTasks[0], Priority.Critical));

        Assert.That(refused!.Message, Does.Contain(plan.Id.ToString()));
    }

    [Test]
    public void ATaskWithNoPlan_StillCarriesItsOwnPriority()
    {
        var engine = Smelter().Engine();
        var loose = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Forge, 1)), First);

        engine.SetPriority(loose, Priority.High);

        Assert.That(engine.Snapshot.Tasks.Single(t => t.Id == loose).Priority, Is.EqualTo(Priority.High));
    }

    [Test]
    public void AHeldPlan_StartsNothing_AndSaysItWasStopped()
    {
        var engine = Smelter().Engine();
        var plan = CommitAlloy(engine, 3);
        plan.Held = true;

        engine.Advance(5);

        Assert.That(engine.Available(Hold, Alloy), Is.Zero, "a held plan started work");
        var task = engine.Snapshot.Tasks.Single(t => t.Id == plan.SpawnedTasks[0]);
        Assert.That(task.LastReason, Is.EqualTo(PostponeReason.SafetyLock));
        Assert.That(engine.Snapshot.Executors.Single().Utilization.Held, Is.EqualTo(5), "a held facility is Held, not idle");
    }

    [Test]
    public void AHeldPlansRunInProgress_FinishesAndDeposits_AndStartsNoOther()
    {
        // D1's boundary: inputs already consumed are a run that finishes. Holding stops the next
        // run, not this one.
        var engine = Smelter(effort: 300).Engine();
        var plan = CommitAlloy(engine, 3);
        engine.Advance(1);

        plan.Held = true;
        engine.Advance(10);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1), "the run in progress must finish, and no other start");
    }

    [Test]
    public void ReleasingAPlan_ResumesItAtTheNextBoundary()
    {
        var engine = Smelter().Engine();
        var plan = CommitAlloy(engine, 3);
        plan.Held = true;
        engine.Advance(3);

        plan.Held = false;
        engine.Advance(3);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(3));
    }

    [Test]
    public void AHeldPlansTransfer_LoadsNothingMore_ButCargoAboardArrives()
    {
        var buffer = new StorageId("buffer");
        var line = new ExecutorId("line");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 100))
            .Producer(First, FacilityType.MatterReactor, null, storage: buffer)
            .Transport(line, Hold, buffer, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("back"), buffer, Hold, throughputPerTick: 10)
            .Engine();
        var plan = CommitAlloy(engine, 1);
        engine.Advance(2);

        plan.Held = true;
        engine.Advance(10);

        Assert.That(engine.Available(buffer, Ore), Is.EqualTo(20), "two ticks were loaded; both must land, and nothing more");
    }

    [Test]
    public void AStarvedTick_PowersTheHigherPriorityRun_WhicheverFacilityIsDeclaredFirst()
    {
        // Two runs of three ticks, power for one at a time. Visit order would always feed the
        // facility declared first; priority must feed the urgent one from its second tick on.
        var engine = Smelter(effort: 300, energy: 300, capacity: 100)
            .Producer(Second, FacilityType.MatterReactor, null)
            .Engine();
        var routine = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), First);
        var urgent = engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Forge, 1)), Second);
        engine.SetPriority(urgent, Priority.High);

        engine.Advance(4);

        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1), "the urgent run was not powered first");
        Assert.That(engine.Available(Hold, Alloy), Is.Zero, "the routine run took power it was outranked for");
        Assert.That(
            engine.Snapshot.Tasks.Single(t => t.Id == routine).LastReason,
            Is.EqualTo(PostponeReason.InsufficientEnergy));
    }

    [Test]
    public void AtEqualPriority_TheOlderTaskIsPowered_NotTheFirstDeclaredFacility()
    {
        // Age is a rule the player can see and control; declaration order is neither.
        var engine = Smelter(effort: 300, energy: 300, capacity: 100)
            .Producer(Second, FacilityType.MatterReactor, null)
            .Engine();
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Forge, 1)), Second);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), First);

        engine.Advance(4);

        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1), "the older task, on the later facility, waited");
        Assert.That(engine.Available(Hold, Alloy), Is.Zero);
    }

    [Test]
    public void WithEnoughPower_EveryRunIsPowered()
    {
        var engine = Smelter(effort: 300, energy: 300, capacity: 200)
            .Producer(Second, FacilityType.MatterReactor, null)
            .Engine();
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Forge, 1)), Second);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), First);

        engine.Advance(3);

        Assert.That(engine.Available(Hold, Chip), Is.EqualTo(1));
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));
        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.Zero);
    }

    [Test]
    public void APlanWithNoTasks_IsStillRecorded()
    {
        // D3's open item, decided: Commit keeps recording it. The goal was already in the hold, and
        // a plan the player committed and can see complete is what the Operations list and the
        // replay harness both read. A controller re-scanning an unplannable goal is C0's concern.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Alloy, 10))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, inputs: new ItemAmount(Ore, 10))
            .Producer(First, FacilityType.MatterReactor, null)
            .Engine();

        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine));

        Assert.That(engine.State.Plans.Plans, Has.Count.EqualTo(1));
        Assert.That(engine.State.Plans.Plans.Single().SpawnedTasks, Is.Empty);
    }
}
