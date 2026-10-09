using Dimenship.Core.Content;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// K5a: what is where for one item — storages, then belts, then runs in progress, read off the
/// snapshot and changing nothing.
/// </summary>
public class StockLocationsTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly ExecutorId Line = new("line");
    private static readonly ExecutorId Reactor = new("reactor");
    private static readonly SchematicId Smelt = new("smelt");

    private static SimulationEngine World() =>
        new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                effort: 300, inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null)
            .Task(Smelt, 1, Reactor)
            .Engine();

    [Test]
    public void AnItemInAStorageAndOnABelt_IsListedInBoth_StoragesFirst()
    {
        var engine = World();
        engine.Advance(2);

        var places = StockLocations.For(engine.Snapshot, Ore);

        Assert.That(places.Select(p => p.Place), Is.EqualTo(new[] { StockPlace.Storage, StockPlace.Belt }));
        Assert.That(places[0].Storage, Is.EqualTo(Hold));
        Assert.That(places[1].Executor, Is.EqualTo(Line));
        Assert.That(places[1].Amount, Is.EqualTo(20), "two ticks of pickup at 10 a tick");
        Assert.That(
            places.Sum(p => p.Amount),
            Is.EqualTo(100 - 10), "everything but the ore a run consumed is somewhere listed");
    }

    [Test]
    public void ARunInProgress_ListsWhatItWillDeposit_UntilItDoes()
    {
        var engine = World();
        engine.Advance(1);

        var running = StockLocations.For(engine.Snapshot, Alloy);
        Assert.That(running, Is.EqualTo(new[] { new StockAt(StockPlace.Run, null, Reactor, 1, 0) }));

        engine.Advance(3);

        var deposited = StockLocations.For(engine.Snapshot, Alloy).Single();
        Assert.That(deposited.Place, Is.EqualTo(StockPlace.Storage), "the run deposited, and the run row went");
        Assert.That(engine.Snapshot.Executors.Single().RunOutput, Is.Null);
    }

    [Test]
    public void AnItemNowhereAboard_HasNoPlaces()
    {
        var engine = World();

        Assert.That(StockLocations.For(engine.Snapshot, Alloy), Is.Empty);
    }
}
