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

    private static SchematicCatalog NoSchematics() =>
        new(Array.Empty<SchematicDefinition>(), Array.Empty<SchematicId>());

    private static IReadOnlyList<ItemDefinition> TwoItems() => new[]
    {
        new ItemDefinition(Ore, "Ore", HoldCapacity: 1_000),
        new ItemDefinition(Alloy, "Alloy", HoldCapacity: 500),
    };

    /// <summary>A full hold plus a one-percent local buffer, and nothing running.</summary>
    private static WorldDefinition TwoStorages(
        IReadOnlyList<ItemAmount>? holdInitial = null,
        IReadOnlyList<ItemAmount>? bufferInitial = null) =>
        new(
            EnergyCapacity: 10_000,
            Schematics: NoSchematics(),
            Items: TwoItems(),
            Storages: new[]
            {
                new StorageDefinition(
                    Hold, "Main Hold", StorageDefinition.FullHold,
                    holdInitial ?? Array.Empty<ItemAmount>()),
                new StorageDefinition(
                    Buffer, "Smelter Buffer", 10, bufferInitial ?? Array.Empty<ItemAmount>()),
            },
            Producers: Array.Empty<ProductionExecutorDefinition>(),
            Transports: Array.Empty<TransportExecutorDefinition>(),
            Sinks: Array.Empty<PowerSinkDefinition>(),
            InitialTasks: Array.Empty<InitialTask>(),
            InitialTransfers: Array.Empty<InitialTransfer>());

    [Test]
    public void AStoragesTotals_AreTheSumOverItsItemsInItemOrder()
    {
        // Summed once, in the kernel, so a storage node and a storage panel cannot disagree about
        // a fill percentage by summing it differently.
        var engine = new SimulationEngine(TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 300), new ItemAmount(Alloy, 40) }));

        var hold = engine.Snapshot.Storages.Single(s => s.Id == Hold);

        Assert.That(hold.TotalAmount, Is.EqualTo(340));
        Assert.That(hold.TotalCapacity, Is.EqualTo(1_500), "1,000 ore plus 500 alloy");
        Assert.That(hold.TotalAmount, Is.EqualTo(hold.Items.Sum(i => i.Amount)));
        Assert.That(hold.TotalCapacity, Is.EqualTo(hold.Items.Sum(i => i.Capacity)));
    }

    [Test]
    public void AnEmptyStoragesTotal_IsZeroAgainstARealCapacity()
    {
        var engine = new SimulationEngine(TwoStorages());

        var buffer = engine.Snapshot.Storages.Single(s => s.Id == Buffer);

        Assert.That(buffer.TotalAmount, Is.EqualTo(0));
        Assert.That(buffer.TotalCapacity, Is.EqualTo(15), "ten permille of 1,000 and of 500");
    }

    [Test]
    public void StorageCapacity_IsAPermilleOfTheItemsHoldCapacity()
    {
        var engine = new SimulationEngine(TwoStorages());

        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(1_000));
        Assert.That(engine.Room(Buffer, Ore), Is.EqualTo(10), "one percent of a 1,000 hold");
        Assert.That(engine.Room(Buffer, Alloy), Is.EqualTo(5), "and one percent of a 500 hold");
    }

    [Test]
    public void InitialContents_LandInTheirOwnStorage()
    {
        var engine = new SimulationEngine(TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 400) },
            bufferInitial: new[] { new ItemAmount(Ore, 7) }));

        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(400));
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(7));
        Assert.That(engine.Room(Hold, Ore), Is.EqualTo(600));
        Assert.That(engine.Room(Buffer, Ore), Is.EqualTo(3));
    }

    [Test]
    public void Available_AndRoom_AreZeroForUnknownStoragesAndItems()
    {
        var engine = new SimulationEngine(TwoStorages());

        Assert.That(engine.Available(new StorageId("nowhere"), Ore), Is.EqualTo(0));
        Assert.That(engine.Room(new StorageId("nowhere"), Ore), Is.EqualTo(0));
        Assert.That(engine.Room(Hold, new ItemId("unobtanium")), Is.EqualTo(0));
    }

    [Test]
    public void Resources_RollUpAcrossEveryStorage()
    {
        // The shell asks "how much ore does this vessel have", not "how much is in the hold".
        // Splitting stock across locations must not change that answer.
        var engine = new SimulationEngine(TwoStorages(
            holdInitial: new[] { new ItemAmount(Ore, 400) },
            bufferInitial: new[] { new ItemAmount(Ore, 7) }));

        var ore = engine.Snapshot.Resources.Single(r => r.Id == Ore);
        Assert.That(ore.Amount, Is.EqualTo(407));
        Assert.That(ore.Capacity, Is.EqualTo(1_010), "1,000 in the hold plus 10 in the buffer");
    }

    [Test]
    public void Storages_ListEveryItemInWorldOrder_IncludingEmptyOnes()
    {
        var engine = new SimulationEngine(TwoStorages(
            holdInitial: new[] { new ItemAmount(Alloy, 12) }));

        var hold = engine.Snapshot.Storages.Single(s => s.Id == Hold);
        Assert.That(
            hold.Items.Select(i => i.Id.Value).ToList(),
            Is.EqualTo(new List<string> { "ore", "alloy" }),
            "world item order, so a panel's rows do not reorder themselves between frames");
        Assert.That(hold.Items[0].Amount, Is.EqualTo(0));
        Assert.That(hold.Items[1].Amount, Is.EqualTo(12));
        Assert.That(hold.Label, Is.EqualTo("Main Hold"));
    }

    [Test]
    public void Storages_AppearInDefinitionOrder()
    {
        var engine = new SimulationEngine(TwoStorages());

        Assert.That(
            engine.Snapshot.Storages.Select(s => s.Id.Value).ToList(),
            Is.EqualTo(new List<string> { "hold", "smelter_buffer" }));
    }

    [Test]
    public void InitialContentsBeyondCapacity_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            new SimulationEngine(TwoStorages(bufferInitial: new[] { new ItemAmount(Ore, 11) })));
    }

    [Test]
    public void InitialContentsOfAnUnknownItem_Throw()
    {
        Assert.Throws<ArgumentException>(() =>
            new SimulationEngine(TwoStorages(
                holdInitial: new[] { new ItemAmount(new ItemId("unobtanium"), 1) })));
    }

    [Test]
    public void DuplicateStorageId_Throws()
    {
        var definition = new WorldDefinition(
            EnergyCapacity: 10_000,
            Schematics: NoSchematics(),
            Items: TwoItems(),
            Storages: new[]
            {
                new StorageDefinition(Hold, "One", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
                new StorageDefinition(Hold, "Two", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
            },
            Producers: Array.Empty<ProductionExecutorDefinition>(),
            Transports: Array.Empty<TransportExecutorDefinition>(),
            Sinks: Array.Empty<PowerSinkDefinition>(),
            InitialTasks: Array.Empty<InitialTask>(),
            InitialTransfers: Array.Empty<InitialTransfer>());

        Assert.Throws<ArgumentException>(() => new SimulationEngine(definition));
    }

    [Test]
    public void DuplicateItemId_Throws()
    {
        var definition = new WorldDefinition(
            EnergyCapacity: 10_000,
            Schematics: NoSchematics(),
            Items: new[]
            {
                new ItemDefinition(Ore, "Ore", 1_000),
                new ItemDefinition(Ore, "Ore again", 1_000),
            },
            Storages: new[]
            {
                new StorageDefinition(Hold, "Hold", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
            },
            Producers: Array.Empty<ProductionExecutorDefinition>(),
            Transports: Array.Empty<TransportExecutorDefinition>(),
            Sinks: Array.Empty<PowerSinkDefinition>(),
            InitialTasks: Array.Empty<InitialTask>(),
            InitialTransfers: Array.Empty<InitialTransfer>());

        Assert.Throws<ArgumentException>(() => new SimulationEngine(definition));
    }

    [Test]
    public void FacilityNamingAnUnknownStorage_Throws()
    {
        var definition = new WorldDefinition(
            EnergyCapacity: 10_000,
            Schematics: NoSchematics(),
            Items: TwoItems(),
            Storages: new[]
            {
                new StorageDefinition(Hold, "Hold", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
            },
            Producers: new[]
            {
                new ProductionExecutorDefinition(
                    new ExecutorId("stray"), "Stray", FacilityType.Extractor, new StorageId("elsewhere"),
                    WorkRatePerTick: 100, StandingPowerDraw: 0, SwitchOverTicks: 0,
                    InitialSchematic: null),
            },
            Transports: Array.Empty<TransportExecutorDefinition>(),
            Sinks: Array.Empty<PowerSinkDefinition>(),
            InitialTasks: Array.Empty<InitialTask>(),
            InitialTransfers: Array.Empty<InitialTransfer>());

        Assert.Throws<ArgumentException>(() => new SimulationEngine(definition));
    }

    [Test]
    public void ExecutorsWorkTheirOwnStorage_NotAGlobalPool()
    {
        // The smelter draws from its buffer and deposits into its buffer. Ore sitting in the
        // hold is not reachable from there, which is the whole reason transport has to exist.
        var schematics = new SchematicCatalog(
            new[]
            {
                new SchematicDefinition
                {
                    Id = Smelt,
                    Output = new ItemAmount(Alloy, 1),
                    Inputs = new[] { new ItemAmount(Ore, 4) },
                    EffortPerRun = new WorkAmount(100),
                    EnergyPerRun = new EnergyAmount(1_000),
                    RequiredFacilityType = FacilityType.MatterReactor,
                },
            },
            new[] { Smelt });

        var definition = new WorldDefinition(
            EnergyCapacity: 10_000,
            Schematics: schematics,
            Items: TwoItems(),
            Storages: new[]
            {
                new StorageDefinition(
                    Hold, "Main Hold", StorageDefinition.FullHold,
                    new[] { new ItemAmount(Ore, 900) }),
                new StorageDefinition(
                    Buffer, "Smelter Buffer", 10, Array.Empty<ItemAmount>()),
            },
            Producers: new[]
            {
                new ProductionExecutorDefinition(
                    Smelter, "Smelter", FacilityType.MatterReactor, Buffer,
                    WorkRatePerTick: 100, StandingPowerDraw: 0, SwitchOverTicks: 0,
                    InitialSchematic: Smelt),
            },
            Transports: Array.Empty<TransportExecutorDefinition>(),
            Sinks: Array.Empty<PowerSinkDefinition>(),
            InitialTasks: new[] { new InitialTask(Smelt, 1, Smelter) },
            InitialTransfers: Array.Empty<InitialTransfer>());
        var engine = new SimulationEngine(definition);

        engine.Advance(1);

        Assert.That(
            engine.Snapshot.Executors[0].Status, Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked));
        Assert.That(
            engine.Snapshot.Executors[0].BlockReason,
            Is.EqualTo(PostponeReason.InsufficientInputMaterial));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(900), "the hold's ore was never touched");
    }
}
