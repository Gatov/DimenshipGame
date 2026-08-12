using Dimenship.Core.Content;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Tests;

/// <summary>
/// Builds a catalog and a scenario for a test without spelling out the parts the test does not
/// care about. Declaration order is preserved throughout, because that order is the simulation's
/// determinism contract and a builder that reordered anything would hide it.
/// <para>
/// Each executor gets an archetype of its own, named for it. A test that says "a refinery working
/// at 100 a tick" means that machine, and sharing one archetype between two would make them quietly
/// interdependent — change one number and the other moves.
/// </para>
/// <para>
/// Nothing here goes through the JSON loader. Its rules — id patterns, placements, unique cells —
/// are the loader's business and are tested against the loader; a test about postponement should
/// not have to author a grid.
/// </para>
/// </summary>
internal sealed class WorldBuilder
{
    public static readonly ItemId Ore = new("ore");
    public static readonly ItemId Alloy = new("alloy");
    public static readonly ItemId Chip = new("chip");
    public static readonly StorageId Hold = new("hold");

    private readonly List<ItemDefinition> _items = new();
    private readonly List<SchematicDefinition> _schematics = new();
    private readonly List<SchematicId> _unlocked = new();
    private readonly List<StorageArchetype> _storageArchetypes = new();
    private readonly List<FacilityArchetype> _facilityArchetypes = new();
    private readonly List<TransportArchetype> _transportArchetypes = new();
    private readonly List<PowerSinkDefinition> _sinks = new();

    private readonly List<ScenarioStorage> _storages = new();
    private readonly List<ScenarioFacility> _facilities = new();
    private readonly List<ScenarioRoute> _routes = new();
    private readonly List<PowerSinkId> _vesselSinks = new();
    private readonly List<ScenarioTask> _tasks = new();
    private readonly List<ScenarioTransfer> _transfers = new();

    private long _energyCapacity = 1_000_000;
    private int _cell;

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

    public WorldBuilder Storage(
        StorageId id, long capacityPermille = StorageArchetype.FullHold, params ItemAmount[] initial)
    {
        var archetype = new StorageArchetypeId($"{id.Value}_kind");
        _storageArchetypes.Add(new StorageArchetype(archetype, id.Value, capacityPermille));
        _storages.Add(new ScenarioStorage(id, archetype, null, initial, Cell()));
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
        StorageId? storage = null,
        bool commandable = true)
    {
        var archetype = new FacilityArchetypeId($"{id.Value}_kind");
        _facilityArchetypes.Add(new FacilityArchetype(
            archetype, id.Value, type, workRate, standingDraw, switchOverTicks,
            BufferPermille: StorageArchetype.FullHold, commandable));

        _facilities.Add(new ScenarioFacility(
            id, archetype, null, storage ?? Hold, initialSchematic, BuiltAtStart: true, Cell()));

        return this;
    }

    public WorldBuilder Transport(
        ExecutorId id, StorageId from, StorageId to, long throughputPerTick, long standingDraw = 0)
    {
        var archetype = new TransportArchetypeId($"{id.Value}_kind");
        _transportArchetypes.Add(new TransportArchetype(
            archetype, id.Value, throughputPerTick, standingDraw));

        _routes.Add(new ScenarioRoute(id, archetype, null, from, to, BuiltAtStart: true));
        return this;
    }

    public WorldBuilder Transfer(
        ItemId item, long? quantity, StorageId from, StorageId to, ExecutorId executor)
    {
        _transfers.Add(new ScenarioTransfer(item, quantity, from, to, executor));
        return this;
    }

    public WorldBuilder Sink(string id, long draw)
    {
        var sink = new PowerSinkId(id);
        _sinks.Add(new PowerSinkDefinition(sink, id, draw));
        _vesselSinks.Add(sink);
        return this;
    }

    /// <summary>Unlocks a schematic the builder did not declare, to exercise the world's check.</summary>
    public WorldBuilder Unlock(SchematicId schematic)
    {
        _unlocked.Add(schematic);
        return this;
    }

    public WorldBuilder Task(SchematicId schematic, int? runs, ExecutorId executor)
    {
        _tasks.Add(new ScenarioTask(schematic, runs, executor));
        return this;
    }

    public ContentCatalog Catalog() =>
        new(
            "test",
            new SchematicCatalog(_schematics),
            _items,
            _storageArchetypes,
            _facilityArchetypes,
            _transportArchetypes,
            _sinks,
            Array.Empty<ReactorArchetype>(),
            Array.Empty<StratumDefinition>());

    public Scenario Scenario() =>
        new(
            "test",
            "Test Vessel",
            _energyCapacity,
            _storages.Count > 0 ? _storages[0].Id : Hold,
            _storages,
            _facilities,
            _routes,
            new NodePlacement(-1, 0, "P"),
            _vesselSinks,
            _unlocked,
            _tasks,
            _transfers);

    public WorldState State() => ScenarioSeeder.Seed(Catalog(), Scenario());

    public SimulationEngine Engine() => new(Catalog(), State());

    /// <summary>A cell per node, on one row. No test here asserts a layout.</summary>
    private NodePlacement Cell() => new(_cell++, 0, string.Empty);
}
