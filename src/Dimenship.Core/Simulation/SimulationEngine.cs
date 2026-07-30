namespace Dimenship.Core.Simulation;

/// <summary>
/// The deterministic core. Holds no wall-clock reference and constructs no random source:
/// all time enters through <see cref="Advance"/>. Pause, speed and offline catch-up belong
/// to the caller, which is what keeps them out of the reproducible path.
/// </summary>
public sealed class SimulationEngine
{
    public const int EventBufferCapacity = 512;

    private readonly WorldDefinition _definition;
    private readonly Dictionary<(StorageId Storage, ItemId Item), long> _stock = new();
    private readonly Dictionary<StorageId, StorageDefinition> _storages = new();
    private readonly Dictionary<ItemId, ItemDefinition> _items = new();
    private readonly Dictionary<ItemId, long> _holdCapacity = new();
    private readonly Dictionary<ItemId, long> _lastDelta = new();
    private readonly Queue<SimEvent> _events = new();
    private readonly List<FacilityState> _facilityStates = new();

    private long _tick;
    private long _totalEventsEmitted;
    private long _draw;
    private int _capHits;
    private int _starvedTicks;

    public SimulationEngine(WorldDefinition definition)
    {
        _definition = definition;

        foreach (var item in definition.Items)
        {
            if (!_items.TryAdd(item.Id, item))
            {
                throw new ArgumentException($"Duplicate item '{item.Id}'.", nameof(definition));
            }

            _lastDelta[item.Id] = 0;
            _holdCapacity[item.Id] = 0;
        }

        foreach (var storage in definition.Storages)
        {
            if (!_storages.TryAdd(storage.Id, storage))
            {
                throw new ArgumentException($"Duplicate storage '{storage.Id}'.", nameof(definition));
            }

            foreach (var item in definition.Items)
            {
                _stock[(storage.Id, item.Id)] = 0;
                _holdCapacity[item.Id] += CapacityOf(storage, item);
            }

            foreach (var initial in storage.Initial)
            {
                RequireKnownItem(initial.Item);
                var capacity = CapacityOf(storage, _items[initial.Item]);
                if (initial.Quantity > capacity)
                {
                    throw new ArgumentException(
                        $"Storage '{storage.Id}' starts with {initial.Quantity} {initial.Item} " +
                        $"but holds at most {capacity}.",
                        nameof(definition));
                }

                _stock[(storage.Id, initial.Item)] = initial.Quantity;
            }
        }

        foreach (var facility in definition.Facilities)
        {
            if (!_storages.ContainsKey(facility.Storage))
            {
                throw new ArgumentException(
                    $"Facility '{facility.Id}' names unknown storage '{facility.Storage}'.",
                    nameof(definition));
            }

            if (facility.Input is { } input)
            {
                RequireKnownItem(input);
            }

            if (facility.Output is { } output)
            {
                RequireKnownItem(output);
            }

            _facilityStates.Add(new FacilityState(facility.Id, facility.Kind, FacilityStatus.Idle, 0, null));
        }

        Snapshot = BuildSnapshot();
    }

    public WorldSnapshot Snapshot { get; private set; }

    /// <summary>How much of an item a storage currently holds.</summary>
    public long Available(StorageId storage, ItemId item) =>
        _stock.TryGetValue((storage, item), out var amount) ? amount : 0;

    /// <summary>How much more of an item a storage could accept.</summary>
    public long Room(StorageId storage, ItemId item)
    {
        if (!_storages.TryGetValue(storage, out var definition) || !_items.TryGetValue(item, out var known))
        {
            return 0;
        }

        return CapacityOf(definition, known) - Available(storage, item);
    }

    public void Advance(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Time does not run backwards.");
        }

        if (ticks == 0)
        {
            return;
        }

        for (var i = 0L; i < ticks; i++)
        {
            Tick();
        }

        Snapshot = BuildSnapshot();
    }

    private static long CapacityOf(StorageDefinition storage, ItemDefinition item) =>
        item.HoldCapacity * storage.CapacityPermille / StorageDefinition.FullHold;

    private void RequireKnownItem(ItemId item)
    {
        if (!_items.ContainsKey(item))
        {
            throw new ArgumentException($"Unknown item '{item}'.", "definition");
        }
    }

    private void Deposit(StorageId storage, ItemId item, long quantity) =>
        _stock[(storage, item)] = Available(storage, item) + quantity;

    private void Withdraw(StorageId storage, ItemId item, long quantity) =>
        _stock[(storage, item)] = Available(storage, item) - quantity;

    private void Tick()
    {
        _tick++;
        _draw = 0;

        var before = new Dictionary<ItemId, long>(_items.Count);
        foreach (var item in _definition.Items)
        {
            before[item.Id] = TotalOf(item.Id);
        }

        var starved = false;
        _facilityStates.Clear();

        foreach (var facility in _definition.Facilities)
        {
            if (_draw + facility.PowerDraw > _definition.EnergyCapacity)
            {
                // Counted once per tick however many facilities are refused, so it stays
                // comparable with _capHits rather than scaling with facility count.
                starved = true;
                Block(facility, EventCode.BlockPowerCap, EventCategory.Power, new Dictionary<string, long>
                {
                    ["draw"] = _draw,
                    ["required"] = facility.PowerDraw,
                    ["capacity"] = _definition.EnergyCapacity,
                });
                continue;
            }

            if (facility.Input is { } input && Available(facility.Storage, input) < facility.InputPerTick)
            {
                Block(facility, EventCode.BlockMissingInput, EventCategory.Production, new Dictionary<string, long>
                {
                    ["have"] = Available(facility.Storage, input),
                    ["need"] = facility.InputPerTick,
                });
                continue;
            }

            if (facility.Output is { } target)
            {
                var room = Room(facility.Storage, target);
                if (room < facility.OutputPerTick)
                {
                    Block(facility, EventCode.StockFull, EventCategory.Production, new Dictionary<string, long>
                    {
                        ["room"] = room,
                        ["need"] = facility.OutputPerTick,
                    });
                    continue;
                }
            }

            if (facility.Input is { } consumed)
            {
                Withdraw(facility.Storage, consumed, facility.InputPerTick);
            }

            if (facility.Output is { } output)
            {
                Deposit(facility.Storage, output, facility.OutputPerTick);
            }

            _draw += facility.PowerDraw;
            _facilityStates.Add(new FacilityState(
                facility.Id, facility.Kind, FacilityStatus.Running, facility.PowerDraw, null));
            Emit(EventCategory.Production, EventCode.Run, facility.Id.Value, SimEvent.NoData);
        }

        if (starved)
        {
            _starvedTicks++;
        }

        if (_draw >= _definition.EnergyCapacity)
        {
            _capHits++;
            Emit(EventCategory.Power, EventCode.PowerCapReached, "vessel", new Dictionary<string, long>
            {
                ["draw"] = _draw,
                ["capacity"] = _definition.EnergyCapacity,
            });
        }

        foreach (var item in _definition.Items)
        {
            _lastDelta[item.Id] = TotalOf(item.Id) - before[item.Id];
        }
    }

    private long TotalOf(ItemId item)
    {
        var total = 0L;
        foreach (var storage in _definition.Storages)
        {
            total += Available(storage.Id, item);
        }

        return total;
    }

    private void Block(
        FacilityDefinition facility,
        EventCode code,
        EventCategory category,
        IReadOnlyDictionary<string, long> data)
    {
        _facilityStates.Add(new FacilityState(facility.Id, facility.Kind, FacilityStatus.Blocked, 0, code));
        Emit(category, code, facility.Id.Value, data);
    }

    private void Emit(
        EventCategory category,
        EventCode code,
        string subject,
        IReadOnlyDictionary<string, long> data)
    {
        _events.Enqueue(new SimEvent(_tick, category, code, subject, data));
        _totalEventsEmitted++;

        while (_events.Count > EventBufferCapacity)
        {
            _events.Dequeue();
        }
    }

    private WorldSnapshot BuildSnapshot()
    {
        // Built from the definition's ordering rather than dictionary ordering, so the lists
        // are stable across runs.
        var resources = new List<ResourceStock>(_definition.Items.Count);
        foreach (var item in _definition.Items)
        {
            resources.Add(new ResourceStock(
                item.Id, TotalOf(item.Id), _holdCapacity[item.Id], _lastDelta[item.Id]));
        }

        var storages = new List<StorageState>(_definition.Storages.Count);
        foreach (var storage in _definition.Storages)
        {
            var contents = new List<ItemStock>(_definition.Items.Count);
            foreach (var item in _definition.Items)
            {
                contents.Add(new ItemStock(
                    item.Id, Available(storage.Id, item.Id), CapacityOf(storage, item)));
            }

            storages.Add(new StorageState(storage.Id, storage.Label, contents));
        }

        return new WorldSnapshot(
            _tick,
            resources,
            storages,
            new EnergyState(
                _definition.EnergyCapacity,
                _draw,
                _definition.EnergyCapacity - _draw,
                _capHits,
                _starvedTicks),
            _facilityStates.ToList(),
            _events.ToList(),
            _totalEventsEmitted);
    }
}
