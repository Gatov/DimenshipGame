using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

public class StorageTests
{
    private static readonly ItemId Ore = new("ore");
    private static readonly ItemId Alloy = new("alloy");
    private static readonly StorageId Hold = new("hold");
    private static readonly StorageId Buffer = new("smelter_buffer");

    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId Smelter = new("smelter");

    /// <summary>A full hold plus a one-percent local buffer, and nothing running.</summary>
    private static WorldBuilder TwoStorages(
        IReadOnlyList<ItemAmount>? holdInitial = null,
        IReadOnlyList<ItemAmount>? bufferInitial = null) =>
        new WorldBuilder()
            .Energy(10_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Hold, StorageArchetype.FullHold, (holdInitial ?? Array.Empty<ItemAmount>()).ToArray())
            .Storage(Buffer, 10, (bufferInitial ?? Array.Empty<ItemAmount>()).ToArray());

    [Test]
    public void AStoragesTotals_AreTheSumOverItsItemsInItemOrder()
    {
        // Summed once, in the kernel, so a storage node and a storage panel cannot disagree about
        // a fill percentage by summing it differently.
        var engine = TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 300), new ItemAmount(Alloy, 40) }).Engine();

        var hold = engine.Snapshot.Storages.Single(s => s.Id == Hold);

        Assert.That(hold.TotalAmount, Is.EqualTo(340));
        Assert.That(hold.TotalCapacity, Is.EqualTo(1_500), "1,000 ore plus 500 alloy");
        Assert.That(hold.TotalAmount, Is.EqualTo(hold.Items.Sum(i => i.Amount)));
        Assert.That(hold.TotalCapacity, Is.EqualTo(hold.Items.Sum(i => i.Capacity)));
    }

    [Test]
    public void AnEmptyStoragesTotal_IsZeroAgainstARealCapacity()
    {
        var engine = TwoStorages().Engine();

        var buffer = engine.Snapshot.Storages.Single(s => s.Id == Buffer);

        Assert.That(buffer.TotalAmount, Is.EqualTo(0));
        Assert.That(buffer.TotalCapacity, Is.EqualTo(15), "ten permille of 1,000 and of 500");
    }

    [Test]
    public void StorageCapacity_IsAPermilleOfTheItemsHoldCapacity()
    {
        var engine = TwoStorages().Engine();

        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(1_000));
        Assert.That(engine.Room(Buffer, Ore), Is.EqualTo(10), "one percent of a 1,000 hold");
        Assert.That(engine.Room(Buffer, Alloy), Is.EqualTo(5), "and one percent of a 500 hold");
    }

    [Test]
    public void InitialContents_LandInTheirOwnStorage()
    {
        var engine = TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 400) },
            bufferInitial: new[] { new ItemAmount(Ore, 7) }).Engine();

        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(400));
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(7));
        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(600));
        Assert.That(engine.Room(Buffer, Ore), Is.EqualTo(3));
    }

    [Test]
    public void Available_AndRoom_AreZeroForUnknownStoragesAndItems()
    {
        var engine = TwoStorages().Engine();

        Assert.That(engine.Available(new StorageId("nowhere"), Ore), Is.EqualTo(0));
        Assert.That(engine.Room(new StorageId("nowhere"), Ore), Is.EqualTo(0));
        Assert.That(engine.Room(Hold, new ItemId("unobtanium")), Is.EqualTo(0));
    }

    [Test]
    public void Resources_RollUpAcrossEveryStorage()
    {
        // The shell asks "how much ore does this vessel have", not "how much is in the hold".
        // Splitting stock across locations must not change that answer.
        var engine = TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 400) },
            bufferInitial: new[] { new ItemAmount(Ore, 7) }).Engine();

        var ore = engine.Snapshot.Resources.Single(r => r.Id == Ore);
        Assert.That(ore.Amount, Is.EqualTo(407));
        Assert.That(ore.Capacity, Is.EqualTo(1_010), "1,000 in the hold plus 10 in the buffer");
    }

    [Test]
    public void Storages_ListEveryItemInWorldOrder_IncludingEmptyOnes()
    {
        var engine = TwoStorages(
            holdInitial: new[] { new ItemAmount(Alloy, 12) }).Engine();

        var hold = engine.Snapshot.Storages.Single(s => s.Id == Hold);
        Assert.That(
            hold.Items.Select(i => i.Id.Value).ToList(),
            Is.EqualTo(new List<string> { "ore", "alloy" }),
            "world item order, so a panel's rows do not reorder themselves between frames");
        Assert.That(hold.Items[0].Amount, Is.EqualTo(0));
        Assert.That(hold.Items[1].Amount, Is.EqualTo(12));
        Assert.That(hold.Label, Is.EqualTo("hold"), "the archetype's label, resolved");
    }

    [Test]
    public void Storages_AppearInDefinitionOrder()
    {
        var engine = TwoStorages().Engine();

        Assert.That(
            engine.Snapshot.Storages.Select(s => s.Id.Value).ToList(),
            Is.EqualTo(new List<string> { "hold", "smelter_buffer" }));
    }

    [Test]
    public void ExecutorsWorkTheirOwnStorage_NotAGlobalPool()
    {
        // The smelter draws from its buffer and deposits into its buffer. Ore sitting in the
        // hold is not reachable from there, which is the whole reason transport has to exist.
        var engine = new WorldBuilder()
            .Energy(10_000)
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 500)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer, 10)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                energy: 1_000, inputs: new ItemAmount(Ore, 4))
            .Producer(Smelter, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Task(Smelt, 1, Smelter)
            .Engine();

        engine.Advance(5);

        Assert.That(
            engine.Available(Hold, Ore),
            Is.EqualTo(100),
            "the smelter reached into the hold, which no route connects it to");
        Assert.That(engine.Available(Buffer, Alloy), Is.Zero, "it smelted ore it cannot reach");
    }
}
