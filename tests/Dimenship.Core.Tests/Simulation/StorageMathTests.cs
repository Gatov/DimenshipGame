using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// <see cref="StorageMath"/> on its own, over a catalog and a state with no engine. The engine's
/// storage tests (<see cref="SharedHoldVolumeTests"/>, <see cref="StorageTests"/>) cover the rules
/// as a tick meets them; these pin what the split promised: the answers come from the state as it
/// stands, and the writes land in the state rather than in anything beside it.
/// </summary>
public class StorageMathTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("smelter_buffer");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Smelter = new("smelter");

    /// <summary>A thousand ore or five hundred alloy fill either storage.</summary>
    private static WorldBuilder Vessel(params ItemAmount[] holdInitial) =>
        new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Hold, StorageArchetype.FullHold, holdInitial)
            .Storage(Buffer);

    [Test]
    public void RoomForDelivery_HoldsBackOneRunsOutput_ForTheFacilitySetUpThere_AndRoomDoesNot()
    {
        // A hundred alloy is a fifth of the buffer, so a feed may fill four fifths of it with ore
        // and production still has somewhere to put its run.
        var world = Collaborators.Over(Vessel()
            .Schematic(Smelt, new ItemAmount(Alloy, 100), FacilityType.MatterReactor, inputs: new ItemAmount(Ore, 100))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer));

        Assert.That(world.Storage.Room(Buffer, Ore), Is.EqualTo(1_000));
        Assert.That(world.Storage.RoomForDelivery(Buffer, Ore), Is.EqualTo(800));
    }

    [Test]
    public void Deposits_AreWrittenIntoTheStatesStockList_InFirstDepositOrder_AndReadBackFromIt()
    {
        var world = Collaborators.Over(Vessel());
        var hold = world.State.Vessel.Storages.Single(s => s.Id == Hold);

        world.Storage.Deposit(Hold, Alloy, 5);
        world.Storage.Deposit(Hold, Ore, 3);
        world.Storage.Withdraw(Hold, Alloy, 2);

        Assert.That(hold.Stock, Is.EqualTo(new[] { new StoredItem(Alloy, 3), new StoredItem(Ore, 3) }));

        // Nothing is cached beside the state: a change made to it directly is the next answer.
        hold.Stock[1] = new StoredItem(Ore, 9);
        Assert.That(world.Storage.Available(Hold, Ore), Is.EqualTo(9));
        Assert.That(world.Storage.TotalOf(Ore), Is.EqualTo(9));
    }

    [Test]
    public void Free_IsWhatNoPlanHolds_AndNeverLessThanNothing()
    {
        var world = Collaborators.Over(Vessel(new ItemAmount(Ore, 100)));

        world.State.Claims.Change(new PlanId(1), Hold, Ore, 30);
        Assert.That(world.Storage.Free(Hold, Ore), Is.EqualTo(70));
        Assert.That(world.Storage.Available(Hold, Ore), Is.EqualTo(100), "Available stays physically present.");

        world.State.Claims.Change(new PlanId(2), Hold, Ore, 200);
        Assert.That(world.Storage.Free(Hold, Ore), Is.Zero);
    }

    [Test]
    public void AWriteToAStorageTheVesselDoesNotHave_Throws_RatherThanLosingTheMaterial()
    {
        var world = Collaborators.Over(Vessel());
        var nowhere = new StorageId("nowhere");

        Assert.Throws<InvalidOperationException>(() => world.Storage.Deposit(nowhere, Ore, 5));
        Assert.Throws<InvalidOperationException>(() => world.Storage.Withdraw(nowhere, Ore, 5));
        Assert.That(world.Storage.Available(nowhere, Ore), Is.Zero, "A read still answers nothing.");
    }
}
