using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// <see cref="FacilityInstance.Built"/> / <see cref="TransportInstance.Built"/> are enforced: an
/// unbuilt executor draws nothing, steps nothing, reserves no room, is invisible to the planner,
/// and is refused by enqueue. Authored unbuilt without these rules is a ghost that works.
/// </summary>
public class BuiltEnforcementTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Smelter = new("smelter");
    private static readonly ExecutorId Feed = new("feed");
    private static readonly ExecutorId Return = new("return");

    [Test]
    public void UnbuiltFacility_DrawsNothing_StepsNothing_AndReservesNoRoom()
    {
        // Task is seeded past Enqueue (WorldBuilder → ScenarioSeeder) so the step skip is what is
        // under test; the loader refuses authoring the same thing.
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 100)
            .Storage(Buffer, 100, new ItemAmount(Ore, 50))
            .Schematic(Smelt, new ItemAmount(Alloy, 2), FacilityType.MatterReactor,
                energy: 1_000, effort: 100, inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, standingDraw: 250,
                storage: Buffer, builtAtStart: false)
            .Task(Smelt, 1, Smelter)
            .Engine();

        engine.Advance(10);

        Assert.That(engine.Snapshot.Energy.Draw, Is.Zero, "unbuilt standing draw is skipped");
        Assert.That(
            engine.Snapshot.Executors.Single(e => e.Id == Smelter).PowerDraw, Is.Zero);
        Assert.That(engine.Available(Buffer, Alloy), Is.Zero, "unbuilt is never stepped");
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(50));
        Assert.That(
            engine.RoomForDelivery(Buffer, Ore),
            Is.EqualTo(engine.Room(Buffer, Ore)),
            "unbuilt reserves no room for an output it cannot produce");
    }

    [Test]
    public void UnbuiltLine_DrawsNothing_AndDoesNotMove()
    {
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Feed, Hold, Buffer, throughputPerTick: 50, standingDraw: 200,
                builtAtStart: false)
            .Transfer(Ore, 50, Hold, Buffer, Feed)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.Snapshot.Energy.Draw, Is.Zero);
        Assert.That(engine.Snapshot.Transports.Single().LoadedLastTick, Is.Zero);
        Assert.That(engine.Snapshot.Transports.Single().Cargo, Is.Empty, "an unbuilt line has an empty belt");
        Assert.That(engine.Available(Buffer, Ore), Is.Zero);
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(100));
    }

    [Test]
    public void Enqueue_RefusesAnUnbuiltExecutor()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, builtAtStart: false)
            .Engine();

        var error = Assert.Throws<ArgumentException>(() => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), Smelter));

        Assert.That(error!.Message, Does.Contain("unbuilt").IgnoreCase);
        Assert.That(engine.Snapshot.Tasks.Where(t => t.Action is Produce), Is.Empty);
    }

    [Test]
    public void EnqueueTransfer_RefusesAnUnbuiltLine()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Feed, Hold, Buffer, throughputPerTick: 50, builtAtStart: false)
            .Engine();

        var error = Assert.Throws<ArgumentException>(
            () => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Hold, Buffer)), Feed));

        Assert.That(error!.Message, Does.Contain("unbuilt").IgnoreCase);
        Assert.That(engine.Snapshot.Tasks.Where(t => t.Action is Transfer), Is.Empty);
    }

    [Test]
    public void Planner_DoesNotRouteThroughAnUnbuiltLine()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Transport(Feed, Hold, Buffer, 1_000, builtAtStart: false)
            .Transport(Return, Buffer, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(
            plan.Unplannable.Any(s => s.Reason == UnplannableReason.NoExecutorOrLine), Is.True,
            "unbuilt feed is invisible, so hold to buffer has no line");
        Assert.That(plan.Transfers().All(t => t.Executor != Feed), Is.True);
    }

    [Test]
    public void Planner_DoesNotOfferAnUnbuiltFacility()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer,
                builtAtStart: false)
            .Transport(Feed, Hold, Buffer, 1_000)
            .Transport(Return, Buffer, Hold, 1_000)
            .Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(Alloy, 2), engine);

        Assert.That(
            plan.Unplannable.Any(s => s.Reason == UnplannableReason.NoExecutorOrLine), Is.True);
        Assert.That(plan.Runs(), Is.Empty);
    }
}
