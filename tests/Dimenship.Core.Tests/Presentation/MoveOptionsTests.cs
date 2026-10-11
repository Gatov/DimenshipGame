using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="MoveOptions"/>: what Operations' MOVE row offers (U4). It offers where a stage could
/// go and nothing more. Whether its material has already been sent is the kernel's to refuse.
/// </summary>
public class MoveOptionsTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Alpha = new("alpha");
    private static readonly ExecutorId Beta = new("beta");
    private static readonly ExecutorId Press = new("press");

    /// <summary>Two refineries on their own buffers, and a press of another type that never qualifies.</summary>
    private static SimulationEngine World(bool betaBuilt = true) =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 400))
            .Storage(BufferA)
            .Storage(BufferB)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: 300,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Alpha, FacilityType.MatterReactor, null, storage: BufferA)
            .Producer(Beta, FacilityType.MatterReactor, null, storage: BufferB, builtAtStart: betaBuilt)
            .Producer(Press, FacilityType.Factory, null)
            .Transport(new ExecutorId("feed_a"), Hold, BufferA, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Transport(new ExecutorId("feed_b"), Hold, BufferB, throughputPerTick: 10, lengthTicks: 3)
            .Transport(new ExecutorId("return_b"), BufferB, Hold, throughputPerTick: 10)
            .Engine();

    private static PlanId Commit(SimulationEngine engine, long alloy)
    {
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(Alloy, alloy), engine));
        return engine.State.Plans.Plans[^1].Id;
    }

    private static IReadOnlyList<MoveOption> Options(SimulationEngine engine, PlanId plan) =>
        MoveOptions.For(engine.Snapshot, engine.Catalog.Schematics, plan);

    [Test]
    public void AStage_CanMoveToEveryBuiltFacilityOfItsType_ButTheOneHoldingIt()
    {
        var engine = World();
        var plan = Commit(engine, 10);

        Assert.That(Options(engine, plan), Has.Count.EqualTo(1));
        Assert.That(Options(engine, plan)[0].Schematic, Is.EqualTo(Smelt));
        Assert.That(Options(engine, plan)[0].To, Is.EqualTo(new[] { Beta }));
    }

    [Test]
    public void AStageSplitAcrossTwoFacilities_OffersEachToTheOther()
    {
        var engine = World();
        var plan = Commit(engine, 10);

        // Some ore aboard first, so Alpha keeps the runs it pays for and Beta takes the rest.
        engine.Advance(3);
        engine.Move(plan, Smelt, Beta);
        Assume.That(engine.Snapshot.Tasks.Count(t => t.Action is Produce && t.State != TaskState.Complete),
            Is.EqualTo(2), "fixture: the move left runs on both");

        Assert.That(Options(engine, plan).Single().To, Is.EqualTo(new[] { Alpha, Beta }));
    }

    [Test]
    public void AnUnbuiltFacility_IsNotOffered()
    {
        var engine = World(betaBuilt: false);
        var plan = Commit(engine, 10);

        Assert.That(Options(engine, plan).Single().To, Is.Empty);
    }

    [Test]
    public void AFinishedPlan_AndAnUnknownOne_OfferNothing()
    {
        var engine = World();
        var plan = Commit(engine, 2);
        engine.Advance(300);
        Assume.That(engine.Snapshot.Plans.Single(p => p.Id == plan).State, Is.EqualTo(PlanState.Complete));

        Assert.That(Options(engine, plan), Is.Empty);
        Assert.That(Options(engine, new PlanId(99)), Is.Empty);
    }
}
