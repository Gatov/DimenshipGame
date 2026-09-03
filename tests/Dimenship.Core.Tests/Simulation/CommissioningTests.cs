using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// Commissioning consumes one whole construction unit from local storage and sets Built. A unit
/// delivered this tick commissions this tick; the facility produces from the next.
/// </summary>
public class CommissioningTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId DockUnit = new("dock_unit");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Smelter = new("smelter");
    private static readonly ExecutorId Feed = new("feed");

    [Test]
    public void AFacilityCommissions_TheTickItsUnitArrives_AndProducesTheTickAfter()
    {
        // Task is seeded past Enqueue so the unbuilt facility can already hold work; Enqueue
        // refuses unbuilt and that rule is covered elsewhere.
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 100)
            .Item(DockUnit, holdCapacity: 40_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(DockUnit, 1_000))
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(Ore, 10))
            .Schematic(Smelt, new ItemAmount(Alloy, 2), FacilityType.MatterReactor,
                energy: 0, effort: 100, inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, standingDraw: 0,
                storage: Buffer, builtAtStart: false, constructionUnit: DockUnit)
            .Transport(Feed, Hold, Buffer, throughputPerTick: 1_000)
            .Transfer(DockUnit, 1_000, Hold, Buffer, Feed)
            .Task(Smelt, 1, Smelter)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.State.Vessel.Facilities.Single(f => f.Id == Smelter).Built, Is.True);
        Assert.That(engine.Available(Buffer, DockUnit), Is.Zero, "the whole unit was consumed");
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e =>
                e.Code == EventCode.FacilityBuilt && e.Subject == Smelter.Value),
            Is.True);
        Assert.That(engine.Available(Buffer, Alloy), Is.Zero,
            "commissioned this tick; production starts next");

        engine.Advance(1);

        Assert.That(engine.Available(Buffer, Alloy), Is.EqualTo(2),
            "one tick of work at rate 100 finishes the 100-effort run");
    }

    [Test]
    public void Commissioning_ConsumesExactlyOneWholeUnit_AndLeavesTheRemainder()
    {
        var engine = new WorldBuilder()
            .Item(DockUnit, holdCapacity: 40_000)
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(DockUnit, 2_500))
            .Producer(Smelter, FacilityType.MatterReactor, initialSchematic: null,
                storage: Buffer, builtAtStart: false, constructionUnit: DockUnit)
            .Engine();

        engine.Advance(1);

        Assert.That(engine.State.Vessel.Facilities.Single(f => f.Id == Smelter).Built, Is.True);
        Assert.That(engine.Available(Buffer, DockUnit), Is.EqualTo(1_500));
    }
}
