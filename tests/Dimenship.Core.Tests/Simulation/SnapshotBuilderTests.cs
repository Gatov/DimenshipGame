using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// <see cref="SnapshotBuilder"/> on its own. It is handed the catalog, the state and the other
/// collaborators, and the one figure the state does not carry, each item's change over the last
/// tick, as an argument. These pin that it needs nothing more.
/// </summary>
public class SnapshotBuilderTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Annex = new("annex");

    private static WorldBuilder Vessel() =>
        new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Annex, StorageArchetype.FullHold / 2);

    [Test]
    public void TheNetRate_IsTheFigureItIsHanded_AndTheAmountIsTheStatesAsItIsNow()
    {
        var world = Collaborators.Over(Vessel());
        var builder = world.Snapshots();

        var before = builder.Build(new Dictionary<ItemId, long> { [Ore] = 7 });
        world.Storage.Deposit(Annex, Ore, 50);
        var after = builder.Build(new Dictionary<ItemId, long> { [Ore] = 0 });

        Assert.That(before.Resources.Single().Amount, Is.EqualTo(100));
        Assert.That(before.Resources.Single().NetRatePerTick, Is.EqualTo(7));
        Assert.That(after.Resources.Single().Amount, Is.EqualTo(150));
        Assert.That(after.Storages.Single(s => s.Id == Annex).FillPermille, Is.EqualTo(100));
    }

    [Test]
    public void AnItemsCapacity_IsSummedOverEveryStorage_ThroughEachStoragesArchetype()
    {
        var snapshot = Collaborators.Over(Vessel()).Snapshots().Build(new Dictionary<ItemId, long> { [Ore] = 0 });

        Assert.That(snapshot.Resources.Single().Capacity, Is.EqualTo(1_500));
    }
}
