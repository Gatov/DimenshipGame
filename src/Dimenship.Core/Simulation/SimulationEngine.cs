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
    private readonly Dictionary<ResourceId, long> _amounts = new();
    private readonly Dictionary<ResourceId, long> _capacities = new();
    private readonly Dictionary<ResourceId, long> _lastDelta = new();
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

        foreach (var resource in definition.Resources)
        {
            _amounts[resource.Id] = resource.InitialAmount;
            _capacities[resource.Id] = resource.Capacity;
            _lastDelta[resource.Id] = 0;
        }

        foreach (var facility in definition.Facilities)
        {
            _facilityStates.Add(new FacilityState(facility.Id, facility.Kind, FacilityStatus.Idle, 0, null));
        }

        Snapshot = BuildSnapshot();
    }

    public WorldSnapshot Snapshot { get; private set; }

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

    private void Tick()
    {
        _tick++;
        _draw = 0;

        var before = new Dictionary<ResourceId, long>(_amounts);
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

            if (facility.Input is { } input && _amounts[input] < facility.InputPerTick)
            {
                Block(facility, EventCode.BlockMissingInput, EventCategory.Production, new Dictionary<string, long>
                {
                    ["have"] = _amounts[input],
                    ["need"] = facility.InputPerTick,
                });
                continue;
            }

            if (facility.Output is { } target)
            {
                var room = _capacities[target] - _amounts[target];
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
                _amounts[consumed] -= facility.InputPerTick;
            }

            if (facility.Output is { } output)
            {
                _amounts[output] += facility.OutputPerTick;
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

        foreach (var resource in _definition.Resources)
        {
            _lastDelta[resource.Id] = _amounts[resource.Id] - before[resource.Id];
        }
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
        // Built from the definition's ordering rather than dictionary ordering, so the list
        // is stable across runs.
        var resources = new List<ResourceStock>(_definition.Resources.Count);
        foreach (var resource in _definition.Resources)
        {
            resources.Add(new ResourceStock(
                resource.Id, _amounts[resource.Id], resource.Capacity, _lastDelta[resource.Id]));
        }

        return new WorldSnapshot(
            _tick,
            resources,
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
