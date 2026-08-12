using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Tests;

/// <summary>
/// Builds a <see cref="WorldDefinition"/> for a test without spelling out the parts the test does
/// not care about. Declaration order is preserved throughout, because that order is the
/// simulation's determinism contract and a builder that reordered anything would hide it.
/// </summary>
internal sealed class WorldBuilder
{
    public static readonly ItemId Ore = new("ore");
    public static readonly ItemId Alloy = new("alloy");
    public static readonly ItemId Chip = new("chip");
    public static readonly StorageId Hold = new("hold");

    private readonly List<ItemDefinition> _items = new();
    private readonly List<StorageDefinition> _storages = new();
    private readonly List<SchematicDefinition> _schematics = new();
    private readonly List<SchematicId> _unlocked = new();
    private readonly List<ProductionExecutorDefinition> _producers = new();
    private readonly List<TransportExecutorDefinition> _transports = new();
    private readonly List<PowerSinkDefinition> _sinks = new();
    private readonly List<InitialTask> _tasks = new();
    private readonly List<InitialTransfer> _transfers = new();

    private long _energyCapacity = 1_000_000;

    public WorldBuilder Energy(long capacity)
    {
        _energyCapacity = capacity;
        return this;
    }

    public WorldBuilder Item(ItemId id, long holdCapacity = 1_000_000)
    {
        _items.Add(new ItemDefinition(id, id.Value, holdCapacity));
        return this;
    }

    public WorldBuilder Storage(StorageId id, long capacityPermille = StorageDefinition.FullHold, params ItemAmount[] initial)
    {
        _storages.Add(new StorageDefinition(id, id.Value, capacityPermille, initial));
        return this;
    }

    public WorldBuilder Schematic(
        SchematicId id,
        ItemAmount output,
        FacilityType facility,
        long effort = 100,
        long energy = 0,
        bool unlocked = true,
        params ItemAmount[] inputs)
    {
        _schematics.Add(new SchematicDefinition
        {
            Id = id,
            Output = output,
            Inputs = inputs,
            EffortPerRun = new WorkAmount(effort),
            EnergyPerRun = new EnergyAmount(energy),
            RequiredFacilityType = facility,
        });

        if (unlocked)
        {
            _unlocked.Add(id);
        }

        return this;
    }

    public WorldBuilder Producer(
        ExecutorId id,
        FacilityType type,
        SchematicId? initialSchematic,
        long workRate = 100,
        long standingDraw = 0,
        long switchOverTicks = 0,
        StorageId? storage = null)
    {
        _producers.Add(new ProductionExecutorDefinition(
            id, id.Value, type, storage ?? Hold, workRate, standingDraw, switchOverTicks, initialSchematic));
        return this;
    }

    public WorldBuilder Transport(
        ExecutorId id, StorageId from, StorageId to, long throughputPerTick, long standingDraw = 0)
    {
        _transports.Add(new TransportExecutorDefinition(
            id, id.Value, from, to, throughputPerTick, standingDraw));
        return this;
    }

    public WorldBuilder Transfer(ItemId item, long? quantity, StorageId from, StorageId to, ExecutorId executor)
    {
        _transfers.Add(new InitialTransfer(item, quantity, from, to, executor));
        return this;
    }

    public WorldBuilder Sink(string id, long draw)
    {
        _sinks.Add(new PowerSinkDefinition(id, id, draw));
        return this;
    }

    public WorldBuilder Task(SchematicId schematic, int? runs, ExecutorId executor)
    {
        _tasks.Add(new InitialTask(schematic, runs, executor));
        return this;
    }

    public WorldDefinition Build() =>
        new(
            _energyCapacity,
            new SchematicCatalog(_schematics, _unlocked),
            _items,
            _storages,
            _producers,
            _transports,
            _sinks,
            _tasks,
            _transfers);

    public SimulationEngine Engine() => new(Build());
}
