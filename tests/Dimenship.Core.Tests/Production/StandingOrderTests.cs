using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// A task with no requested count: run for as long as the inputs keep arriving. It is what an
/// executor does absent instruction, which is why it is explicit state rather than a preset
/// program — a vessel whose baseline behaviour depends on a program being installed is the
/// fragility the recovery invariant exists to rule out.
/// <para>
/// The count being absent rather than very large is the whole point, and each of these pins one
/// place where the difference is arithmetic.
/// </para>
/// </summary>
public class StandingOrderTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Refinery = new("refinery");
    private static readonly ExecutorId Line = new("line");

    /// <summary>Ten ore makes one alloy, on one refinery working out of the hold.</summary>
    private static WorldBuilder Reactor(long oreOnHand) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, oreOnHand))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Refinery, FacilityType.MatterReactor, Smelt);

    [Test]
    public void AStandingOrder_KeepsRunning_PastAnyCountItCouldHaveBeenGiven()
    {
        var engine = Reactor(oreOnHand: 1_000).Task(Smelt, null, Refinery).Engine();

        engine.Advance(500);

        var task = engine.Snapshot.ProductionTasks.Single();
        Assert.That(task.RequestedRuns, Is.Null);
        Assert.That(task.CompletedRuns, Is.GreaterThan(1), "the order stopped after its first run");
        Assert.That(task.State, Is.Not.EqualTo(TaskState.Complete), "an indefinite task completed");
    }

    [Test]
    public void AStandingOrder_CommitsTheVesselToNothingBeyondTheRunInFlight()
    {
        // The defect this replaced: a million-run stand-in charged a million runs of input
        // against the hold, so everything else planned against it saw a deficit of billions.
        var engine = Reactor(oreOnHand: 1_000).Task(Smelt, null, Refinery).Engine();

        Assert.That(
            engine.Uncommitted(Ore),
            Is.EqualTo(1_000),
            "an unstarted standing order claimed ore it has not consumed");

        engine.Advance(1);

        // One run is in flight: its ten ore are already out of storage, and its alloy is owed.
        Assert.That(engine.Uncommitted(Ore), Is.EqualTo(990));
        Assert.That(engine.Uncommitted(Alloy), Is.EqualTo(1));
    }

    [Test]
    public void AStandingOrder_MakesItsFacilityOccupied_RatherThanDeeplyQueued()
    {
        IWorldView world = Reactor(oreOnHand: 1_000).Task(Smelt, null, Refinery).Engine();

        var facility = world.Facilities.Single();

        // Not a large number standing in for "busy": that was the placeholder, and expressing it
        // as a queue depth is the placeholder again in a new place.
        Assert.That(facility.QueuedRuns, Is.Zero);
        Assert.That(facility.Occupied, Is.True);
    }

    [Test]
    public void ThePlanner_PrefersAFreeFacility_ToOneRunningAStandingOrder()
    {
        var second = new ExecutorId("refinery_b");
        var engine = Reactor(oreOnHand: 1_000)
            .Producer(second, FacilityType.MatterReactor, Smelt)
            .Task(Smelt, null, Refinery)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 1), engine);

        // The occupied facility is declared first, so definition order alone would have picked it.
        Assert.That(plan.Runs.Single().Executor, Is.EqualTo(second));
    }

    [Test]
    public void AStandingTransfer_HaulsWhatIsThere_AndNeverCompletes()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 50))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10)
            .Transfer(Ore, null, Hold, Buffer, Line)
            .Engine();

        engine.Advance(10);

        var transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.RequestedQuantity, Is.Null);
        Assert.That(transfer.MovedQuantity, Is.EqualTo(50), "the line stopped short of the source");
        Assert.That(transfer.State, Is.Not.EqualTo(TaskState.Complete), "an indefinite transfer completed");

        // Nothing left to haul is a postponement, not a completion: put more in the source and it
        // resumes, which is the difference between a standing order and a finished job.
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
    }
}
