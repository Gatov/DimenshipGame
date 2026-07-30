using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

public class ProductionPlannerTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId RefineryB = new("refinery_b");
    private static readonly ExecutorId Line = new("line");

    /// <summary>Ten ore makes one alloy, on one refinery, with one transport line.</summary>
    private static WorldBuilder Refinery(long oreOnHand, bool unlocked = true) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, oreOnHand))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                unlocked: unlocked, inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.Refinery, unlocked ? Smelt : null, storage: BufferA)
            .Transport(Line, 1_000);

    [Test]
    public void WhatIsAlreadyAboard_IsUsedBeforeAnythingIsProduced()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100), new ItemAmount(Alloy, 4))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.Refinery, Smelt, storage: BufferA)
            .Transport(Line, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 10), engine);

        Assert.That(plan.Runs.Single().Runs, Is.EqualTo(6), "four were already aboard");
        Assert.That(plan.IsComplete, Is.True);
    }

    [Test]
    public void AGoalAlreadySatisfied_PlansNothingAtAll()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Alloy, 50))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.Refinery, Smelt, storage: BufferA)
            .Transport(Line, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 10), engine);

        Assert.That(plan.Runs, Is.Empty);
        Assert.That(plan.Transfers, Is.Empty);
        Assert.That(plan.IsComplete, Is.True);
    }

    [Test]
    public void ALockedSchematic_IsAShortage_NotAnException_AndNotAnExpedition()
    {
        // A mission fixes this, not hauling. Reporting it as a raw-resource shortage would send
        // the player out to look for something that was never out there.
        var engine = Refinery(oreOnHand: 100, unlocked: false).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 3), engine);

        Assert.That(plan.Runs, Is.Empty, "the branch is not built at all");
        Assert.That(plan.Shortages.Single().Kind, Is.EqualTo(ShortageKind.LockedSchematic));
        Assert.That(plan.Shortages.Single().Missing, Is.EqualTo(3));
        Assert.That(plan.NeedsAcquisition, Is.False, "so no expedition is suggested");
    }

    [Test]
    public void AnItemNothingProduces_IsARawResourceShortage()
    {
        var engine = Refinery(oreOnHand: 5).Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        var shortage = plan.Shortages.Single();
        Assert.That(shortage.Item, Is.EqualTo(Ore));
        Assert.That(shortage.Missing, Is.EqualTo(15), "20 needed for two runs, 5 aboard");
        Assert.That(shortage.Kind, Is.EqualTo(ShortageKind.RawResource));
        Assert.That(plan.NeedsAcquisition, Is.True);
    }

    [Test]
    public void CyclicSchematics_TerminateWithACyclicShortage()
    {
        // Alloy is made from chips and chips from alloy. A visited set is the difference between
        // a diagnosable content error and a stack overflow.
        var makeChip = new SchematicId("make_chip");
        var engine = new WorldBuilder()
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold)
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Chip, 1))
            .Schematic(makeChip, new ItemAmount(Chip, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Alloy, 1))
            .Producer(RefineryA, FacilityType.Refinery, Smelt, storage: BufferA)
            .Transport(Line, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 1), engine);

        Assert.That(plan.Shortages.Any(s => s.Kind == ShortageKind.CyclicSchematic), Is.True);
    }

    [Test]
    public void NoFacilityOfTheRequiredType_IsAShortage()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(new ExecutorId("factory"), FacilityType.Factory, initialSchematic: null,
                storage: BufferA)
            .Transport(Line, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(plan.Shortages.Single().Kind, Is.EqualTo(ShortageKind.NoCompatibleExecutor));
    }

    [Test]
    public void Overproduction_IsCreditedToLaterBranches()
    {
        // The schematic makes five at a time. Asking for three makes five, and the surplus two
        // are real: a second demand for two must not plan another run.
        var makeChip = new SchematicId("make_chip");
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(BufferA, 100)
            .Schematic(makeChip, new ItemAmount(Chip, 1), FacilityType.Refinery,
                inputs: new[] { new ItemAmount(Alloy, 3), new ItemAmount(Ore, 1) })
            .Schematic(Smelt, new ItemAmount(Alloy, 5), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.Refinery, Smelt, storage: BufferA)
            .Transport(Line, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Chip, 1), engine);

        var alloyRuns = plan.Runs.Single(r => r.Schematic == Smelt).Runs;
        Assert.That(alloyRuns, Is.EqualTo(1), "one run covers the three needed, with two spare");
    }

    [Test]
    public void PlanningTheSameGoalTwice_DoesNotSpendTheSameStockTwice()
    {
        // Availability nets out what committed tasks have already claimed. Without that, a
        // second plan would happily promise the first plan's ore all over again.
        var engine = Refinery(oreOnHand: 60).Engine();

        var first = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        Assert.That(first.IsComplete, Is.True, "50 of the 60 ore covers five runs");
        engine.Commit(first);

        var second = ProductionPlanner.Plan(new ItemAmount(Alloy, 8), engine);

        Assert.That(
            second.Runs.Single().Runs, Is.EqualTo(3),
            "five alloy are already on their way, so only three more are needed");
        Assert.That(
            second.Shortages.Single().Missing, Is.EqualTo(20),
            "and the ore is spoken for too: 10 of the 60 is left, against the 30 those runs need");
    }

    [Test]
    public void TheLeastLoadedCompatibleFacility_IsChosen_TieBrokenByDefinitionOrder()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 10_000))
            .Storage(BufferA, 100)
            .Storage(BufferB, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.Refinery, Smelt, storage: BufferA)
            .Producer(RefineryB, FacilityType.Refinery, Smelt, storage: BufferB)
            .Transport(Line, 1_000)
            .Engine();

        Assert.That(
            ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine).Runs.Single().Executor,
            Is.EqualTo(RefineryA),
            "both idle, so the earlier definition wins the tie");

        engine.Enqueue(Smelt, 50, RefineryA);

        // Sixty, not two: fifty runs are already queued and their output counts as available,
        // so a smaller goal would correctly plan no runs at all and prove nothing about choice.
        Assert.That(
            ProductionPlanner.Plan(new ItemAmount(Alloy, 60), engine).Runs.Single().Executor,
            Is.EqualTo(RefineryB),
            "refinery A is now fifty runs deep, so the work goes to the idle one");
    }

    [Test]
    public void Planning_ChangesNothingAboutTheWorld()
    {
        // The specification is explicit that planning data is not runtime tasks. Producing a plan
        // ten times must leave the vessel exactly as it was.
        var engine = Refinery(oreOnHand: 100).Engine();
        var before = engine.Snapshot;

        for (var i = 0; i < 10; i++)
        {
            ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        }

        Assert.That(engine.Snapshot, Is.SameAs(before), "not even a new snapshot");
        Assert.That(engine.Snapshot.ProductionTasks, Is.Empty);
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(100));
    }

    [Test]
    public void PlanningTheSameGoalTwiceWithoutCommitting_GivesTheSamePlan()
    {
        var engine = Refinery(oreOnHand: 100).Engine();

        var first = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);
        var second = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);

        Assert.That(second.Runs, Is.EqualTo(first.Runs));
        Assert.That(second.Transfers, Is.EqualTo(first.Transfers));
        Assert.That(second.Shortages, Is.EqualTo(first.Shortages));
    }

    [Test]
    public void AGoalOfNothing_IsRejected()
    {
        var engine = Refinery(oreOnHand: 10).Engine();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProductionPlanner.Plan(new ItemAmount(Alloy, 0), engine));
    }

    [Test]
    public void Commit_QueuesEveryRunAndTransfer_AndReturnsTheirIds()
    {
        var engine = Refinery(oreOnHand: 100).Engine();
        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 5), engine);

        var created = engine.Commit(plan);

        Assert.That(created, Has.Count.EqualTo(plan.Runs.Count + plan.Transfers.Count));
        Assert.That(created.Distinct().Count(), Is.EqualTo(created.Count), "ids are unique");
        Assert.That(engine.Snapshot.ProductionTasks, Has.Count.EqualTo(plan.Runs.Count));
        Assert.That(engine.Snapshot.TransportTasks, Has.Count.EqualTo(plan.Transfers.Count));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PlanCommitted),
            Is.EqualTo(1));
    }
}
