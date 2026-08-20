using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// A storage is one volume that every item competes for, not a silo per item. These are the tests
/// that fail if it ever goes back to being a silo per item, which is the shape it had first and
/// which let the shipped vessel's global hold reach 123% of a hold without anything noticing.
/// </summary>
public class SharedHoldVolumeTests
{
    private static readonly ItemId Ore = new("ore");
    private static readonly ItemId Alloy = new("alloy");
    private static readonly StorageId Hold = new("hold");
    private static readonly StorageId Buffer = new("smelter_buffer");

    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Smelter = new("smelter");
    private static readonly ExecutorId Feed = new("feed");
    private static readonly ExecutorId Return = new("return");

    /// <summary>
    /// A hold that a thousand ore would fill, or five hundred alloy. So one ore occupies a
    /// thousandth of it and one alloy a five-hundredth: alloy is twice as bulky per unit.
    /// </summary>
    private static WorldBuilder Vessel(params ItemAmount[] holdInitial) =>
        new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Hold, StorageArchetype.FullHold, holdInitial);

    [Test]
    public void AStoragesFill_IsTheSumOfEachItemsShareOfTheWholeVolume()
    {
        // A third of the hold in ore and a half of it in alloy is five sixths full, which is the
        // rule this whole model exists to state. 333 + 500 permille, floored per item.
        var engine = Vessel(new ItemAmount(Ore, 333), new ItemAmount(Alloy, 250)).Engine();

        Assert.That(engine.FillPermille(Hold), Is.EqualTo(833));
    }

    [Test]
    public void AnEmptyStorage_ReadsEmpty_AgainstARealVolume()
    {
        var engine = Vessel().Engine();

        Assert.That(engine.FillPermille(Hold), Is.Zero);
        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(1_000));
    }

    [Test]
    public void RoomForOneItem_ShrinksAsAnotherItemTakesTheVolume()
    {
        // Half the volume is alloy, so half is left — for either item, measured in that item's
        // own units. Under a silo per item this was 1,000 and 250.
        var engine = Vessel(new ItemAmount(Alloy, 250)).Engine();

        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(500));
        Assert.That(engine.Room(Hold, Alloy), Is.EqualTo(250));
    }

    [Test]
    public void AHoldFullOfOneItem_HasNoRoomForAnother()
    {
        var engine = Vessel(new ItemAmount(Ore, 1_000)).Engine();

        Assert.That(engine.FillPermille(Hold), Is.EqualTo(1_000));
        Assert.That(engine.Room(Hold, Alloy), Is.Zero);
        Assert.That(engine.Room(Hold, Ore), Is.Zero);
    }

    [Test]
    public void ATransportStops_WhenTheDestinationHasNoVolumeLeft_NotWhenItsOwnItemIsCapped()
    {
        // The destination holds no ore at all, so an ore ceiling of its own would say it has room
        // for a hundred. It is full of alloy, and full is full.
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 400))
            .Storage(Buffer, 100, new ItemAmount(Alloy, 50))
            .Transport(Feed, Hold, Buffer, throughputPerTick: 10)
            .Transfer(Ore, null, Hold, Buffer, Feed)
            .Engine();

        engine.Advance(10);

        Assert.That(engine.Available(Buffer, Ore), Is.Zero);
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(400), "nothing left the hold");

        var line = engine.Snapshot.Transports.Single(t => t.Id == Feed);
        Assert.That(line.BlockReason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    [Test]
    public void AFacilityRuns_WhenItsOutputFitsTheVolumeItsOwnInputsFree()
    {
        // The buffer is full of ore. The run takes four ore out — four thousandths of the volume —
        // and puts one alloy in, which is two thousandths. It fits, but only afterwards: checking
        // the output against a buffer still holding the inputs would deadlock the facility.
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                energy: 1_000, effort: 100, inputs: new ItemAmount(Ore, 4))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Task(Smelt, 1, Smelter)
            .Engine();

        engine.Advance(2);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(996));
        Assert.That(engine.Available(Buffer, Alloy), Is.EqualTo(1));
        Assert.That(engine.FillPermille(Buffer), Is.EqualTo(998));
    }

    [Test]
    public void AFacilityPostpones_WhenItsOutputIsBulkierThanTheInputsItFrees()
    {
        // Two ore become one alloy: two thousandths of the volume out, two thousandths in, and
        // the run needs one more thousandth than it releases. A full buffer cannot run it.
        var engine = new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Buffer, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 2), FacilityType.MatterReactor,
                energy: 1_000, effort: 100, inputs: new ItemAmount(Ore, 2))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Task(Smelt, 1, Smelter)
            .Engine();

        engine.Advance(2);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(1_000), "no input was shredded");
        Assert.That(engine.Available(Buffer, Alloy), Is.Zero);

        var facility = engine.Snapshot.Executors.Single(e => e.Id == Smelter);
        Assert.That(facility.BlockReason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    /// <summary>
    /// A buffer fed by a standing order, working a schematic whose output is bulkier than its
    /// input: ten ore occupy a tenth of the buffer and become two alloy, which occupy a fifth.
    /// This is the shape that deadlocked the shipped vessel, so it is the shape under test.
    /// </summary>
    private static WorldBuilder FedFacility() =>
        new WorldBuilder()
            .Energy(100_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 100)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 500))
            .Storage(Buffer, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 2), FacilityType.MatterReactor,
                energy: 1_000, effort: 100, inputs: new ItemAmount(Ore, 10))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Transport(Feed, Hold, Buffer, throughputPerTick: 5)
            .Transfer(Ore, null, Hold, Buffer, Feed)
            .Task(Smelt, null, Smelter);

    [Test]
    public void AFeedLine_StopsShortOfTheRoomTheFacilityItFeedsNeeds()
    {
        var engine = FedFacility().Engine();

        engine.Advance(500);

        // Two alloy of a ten-alloy buffer is two hundred permille, and that much is never sold to
        // the line filling it. The line reports a full destination while the buffer plainly is
        // not full, which is the point: the room is spoken for.
        Assert.That(engine.FillPermille(Buffer), Is.LessThanOrEqualTo(800));
        Assert.That(engine.Room(Buffer, Ore), Is.GreaterThan(0));
        Assert.That(engine.RoomForDelivery(Buffer, Ore), Is.Zero);

        var line = engine.Snapshot.Transports.Single(t => t.Id == Feed);
        Assert.That(line.BlockReason, Is.EqualTo(PostponeReason.DestinationFull));
    }

    [Test]
    public void AFedFacility_KeepsRunning_ForAsLongAsThereIsAnythingToFeedIt()
    {
        // The regression this whole reservation exists for. Without it the feed line fills the
        // buffer to the brim, the run's own inputs free less volume than its output needs, and
        // the facility never runs again — which is how the shipped vessel died four operational
        // hours in. With the room held back, the chain runs until the ore runs out and stops on
        // the shortage it actually has.
        var engine = FedFacility()
            .Transport(Return, Buffer, Hold, throughputPerTick: 5)
            .Transfer(Alloy, null, Buffer, Hold, Return)
            .Engine();

        engine.Advance(100);

        var facility = engine.Snapshot.Executors.Single(e => e.Id == Smelter);
        Assert.That(facility.Status, Is.EqualTo(ExecutorStatus.RunningTask));
        Assert.That(
            engine.Available(Hold, Alloy),
            Is.GreaterThanOrEqualTo(90),
            "it turned nearly all the hold's ore into alloy, run after run");
        Assert.That(
            engine.FillPermille(Buffer),
            Is.LessThanOrEqualTo(800),
            "and the feed line never crowded out the deposit");

        engine.Advance(400);

        Assert.That(
            engine.Snapshot.Executors.Single(e => e.Id == Smelter).BlockReason,
            Is.EqualTo(PostponeReason.InsufficientInputMaterial),
            "the chain ends on an empty hold, not on a buffer it cannot deposit into");
    }

    [Test]
    public void AStorageNoFacilityWorksOutOf_ReservesNothing()
    {
        // The vessel's own hold is nobody's buffer. Nothing is held back there, or the room the
        // player paid for would quietly be smaller than the number on the card.
        var engine = FedFacility().Engine();

        Assert.That(
            engine.RoomForDelivery(Hold, Ore),
            Is.EqualTo(engine.Room(Hold, Ore)),
            "500 ore in a 1,000 hold leaves 500, spoken for by no one");
    }

    [Test]
    public void FillAndRoom_AreZeroForAStorageThatDoesNotExist()
    {
        var engine = Vessel().Engine();

        Assert.That(engine.FillPermille(new StorageId("nowhere")), Is.Zero);
        Assert.That(engine.Room(new StorageId("nowhere"), Ore), Is.Zero);
    }
}
