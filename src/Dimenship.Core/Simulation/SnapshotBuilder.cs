using Dimenship.Core.Content;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

/// <summary>
/// Builds a <see cref="WorldSnapshot"/> from the state and the catalog. Split out of the engine
/// (R2.2) so that what the snapshot may read is decided by its constructor rather than by
/// discipline: it is handed the catalog, the state, the <see cref="Resolver"/> and the
/// <see cref="StorageMath"/>, and nothing else. The engine's per-tick scratch (the power grants,
/// the starved flag, the reserved power) is out of its reach, so a reading on the shell can never
/// depend on how far through a tick the snapshot was taken, and
/// <c>EveryValueTheSnapshotShows_ComesFromTheCatalogAndTheState</c> stays true by construction.
/// <para>
/// Lists are built in declaration order, never dictionary order, so two snapshots of one world are
/// equal item for item.
/// </para>
/// </summary>
internal sealed class SnapshotBuilder
{
    private readonly ContentCatalog _catalog;
    private readonly WorldState _state;
    private readonly Resolver _resolve;
    private readonly StorageMath _storage;

    /// <summary>
    /// Each item's capacity summed over every storage aboard. Computed once: capacity is the
    /// catalog's item ceiling through each storage's archetype, and neither changes during a run.
    /// </summary>
    private readonly Dictionary<ItemId, long> _holdCapacity = new();

    public SnapshotBuilder(ContentCatalog catalog, WorldState state, Resolver resolve, StorageMath storage)
    {
        _catalog = catalog;
        _state = state;
        _resolve = resolve;
        _storage = storage;

        foreach (var item in catalog.Items)
        {
            _holdCapacity[item.Id] = 0;
        }

        foreach (var storageInstance in state.Vessel.Storages)
        {
            foreach (var item in catalog.Items)
            {
                _holdCapacity[item.Id] += storage.CapacityOf(storageInstance, item);
            }
        }
    }

    /// <summary>
    /// The world as it stands, with <paramref name="lastDelta"/> as each item's change over the last
    /// tick. The delta is passed in rather than read because it is the one figure here that is not
    /// in the state: the engine measures it across a tick, and a builder that could read the
    /// engine's fields to find it could read the rest of its scratch as well.
    /// </summary>
    public WorldSnapshot Build(IReadOnlyDictionary<ItemId, long> lastDelta)
    {
        // Built from the definition's ordering rather than dictionary ordering, so the lists
        // are stable across runs.
        var resources = new List<ResourceStock>(_catalog.Items.Count);
        foreach (var item in _catalog.Items)
        {
            resources.Add(new ResourceStock(
                item.Id, _storage.TotalOf(item.Id), _holdCapacity[item.Id], lastDelta[item.Id]));
        }

        var storages = new List<StorageState>(_state.Vessel.Storages.Count);
        foreach (var storage in _state.Vessel.Storages)
        {
            var contents = new List<ItemStock>(_catalog.Items.Count);
            foreach (var item in _catalog.Items)
            {
                contents.Add(new ItemStock(
                    item.Id,
                    _storage.Available(storage.Id, item.Id),
                    _storage.CapacityOf(storage, item),
                    _state.Claims.HeldAt(storage.Id, item.Id)));
            }

            storages.Add(new StorageState(
                storage.Id,
                WorldState.NameOf(_catalog, storage),
                _storage.FillPermille(storage.Id),
                contents));
        }

        var executors = new List<ExecutorState>(_state.Vessel.Facilities.Count);
        foreach (var executor in _state.Vessel.Facilities)
        {
            executors.Add(new ExecutorState(
                executor.Id,
                WorldState.NameOf(_catalog, executor),
                _resolve.Archetype(executor).Type,
                executor.LocalStorage,
                executor.Built,
                executor.Status,
                executor.Configured,
                executor.Current,
                executor.PowerDrawLastTick,
                RunTicksRemaining(executor),
                RunTicksTotal(executor),
                executor.SwitchOverRemaining,
                executor.BlockReason,
                Reading(executor.Utilization),
                _resolve.CurrentOf(executor) is { RunActive: true } running
                    ? _catalog.Schematics.Get(running.Produce.Schematic).Output
                    : null,
                executor.SwitchTarget is { } target && executor.SwitchOverRemaining > 0
                    ? _state.Tasks.Task(target)?.Produce.Schematic
                    : null,
                _resolve.SwitchOverTicks(executor)));
        }

        var transports = new List<TransportExecutorState>(_state.Vessel.Transports.Count);
        foreach (var hauler in _state.Vessel.Transports)
        {
            transports.Add(new TransportExecutorState(
                hauler.Id,
                WorldState.NameOf(_catalog, hauler),
                hauler.From,
                hauler.To,
                hauler.Built,
                hauler.Status,
                hauler.Current,
                Cargo(hauler),
                _resolve.Throughput(hauler),
                hauler.LengthTicks,
                _resolve.Capacity(hauler),
                FillPermille(hauler),
                hauler.LoadedLastTick,
                hauler.DeliveredLastTick,
                hauler.PowerDrawLastTick,
                hauler.BlockReason));
        }

        var sinks = new List<PowerSinkState>();
        foreach (var sink in _resolve.Sinks())
        {
            sinks.Add(new PowerSinkState(sink.Id.Value, sink.Label, sink.PowerDraw));
        }

        var tasks = new List<TaskInstanceState>(_state.Tasks.All.Count);
        foreach (var task in _state.Tasks.All)
        {
            // The last attempt carries the cause of the last postponement: postponing always
            // records one unless it repeats the last exactly, and anything else that happens to the
            // task clears LastReason.
            var cause = task.LastReason is not null && task.History.Count > 0
                && task.History[^1].Outcome == TaskAttemptOutcome.Postponed
                    ? task.History[^1]
                    : null;
            tasks.Add(new TaskInstanceState(
                task.Id,
                task.ExecutorId,
                task.Script.Action,
                task.State,
                task.LastReason,
                task.PostponedAtTick,
                task.CompletedRuns,
                task.MovedQuantity,
                task.LoadedQuantity,
                task.EnqueuedAtTick,
                task.FirstStartedAtTick,
                task.CompletedAtTick,
                _resolve.PriorityOf(task),
                _resolve.IsHeld(task),
                cause?.ByTask,
                cause?.ByPlan));
        }

        var plans = new List<CommittedPlanState>(_state.Plans.Plans.Count);
        foreach (var plan in _state.Plans.Plans)
        {
            plans.Add(new CommittedPlanState(
                plan.Id,
                plan.Goal,
                plan.Destination,
                plan.CommittedAtTick,
                plan.SpawnedTasks,
                plan.CompletedTasks,
                plan.State,
                plan.Priority,
                plan.Held));
        }

        var alerts = _state.Alerts.Alerts
            .Select(a => new AlertState(
                a.Id, a.Severity, a.Code, a.SubjectId, a.RaisedAtTick, a.RootCause, a.RelatedSubjectId,
                a.Acknowledged, a.Pinned))
            .ToList();

        return new WorldSnapshot(
            _state.Clock.Tick,
            resources,
            storages,
            new EnergyState(
                _state.Vessel.Energy.Capacity,
                _state.Vessel.Energy.DrawLastTick,
                _state.Vessel.Energy.Capacity - _state.Vessel.Energy.DrawLastTick,
                _state.Vessel.Energy.CapHits,
                _state.Vessel.Energy.StarvedTicks),
            executors,
            transports,
            sinks,
            tasks,
            plans,
            _state.Journal.Events.ToList(),
            _state.Journal.TotalEmitted,
            InProcess(),
            _state.Claims.Entries.Select(c => new ClaimState(c.Plan, c.Storage, c.Item, c.Held)).ToList(),
            alerts);
    }

    private static UtilizationReading Reading(UtilizationWindow window) =>
        new(
            window.Measured,
            window.Total(UtilizationCategory.Working),
            window.Total(UtilizationCategory.Idle),
            window.Total(UtilizationCategory.WaitingInput),
            window.Total(UtilizationCategory.WaitingOutput),
            window.Total(UtilizationCategory.Throttled),
            window.Total(UtilizationCategory.SwitchingOver),
            window.Total(UtilizationCategory.Held));

    /// <summary>
    /// Material tied up in unfinished work, one entry per catalog item in catalog order, including
    /// items nothing holds, so a reader gets a stable set of rows.
    /// <para>
    /// A run's inputs are read from its schematic rather than recorded when withdrawn, because the
    /// run consumes exactly those and nothing else: <c>RunActive</c> is true from the withdrawal
    /// to the deposit, held deposits included, which is precisely the span the material is in
    /// neither storage.
    /// </para>
    /// </summary>
    private IReadOnlyList<ItemInProcess> InProcess()
    {
        var inRuns = new Dictionary<ItemId, long>();
        foreach (var task in _state.Tasks.All)
        {
            if (!task.IsProduce || !task.RunActive)
            {
                continue;
            }

            foreach (var input in _catalog.Schematics.Get(task.Produce.Schematic).Inputs)
            {
                inRuns[input.Item] = inRuns.GetValueOrDefault(input.Item) + input.Quantity;
            }
        }

        var onBelts = new Dictionary<ItemId, long>();
        foreach (var line in _state.Vessel.Transports)
        {
            foreach (var slot in line.Belt)
            {
                if (slot is not null)
                {
                    onBelts[slot.Item] = onBelts.GetValueOrDefault(slot.Item) + slot.Quantity;
                }
            }
        }

        var items = new List<ItemInProcess>(_catalog.Items.Count);
        foreach (var item in _catalog.Items)
        {
            items.Add(new ItemInProcess(
                item.Id, inRuns.GetValueOrDefault(item.Id), onBelts.GetValueOrDefault(item.Id)));
        }

        return items;
    }

    /// <summary>
    /// Ticks left on the run in progress, derived from the work still to do rather than stored
    /// separately. One source of truth: a stored countdown and an accumulated work total would
    /// drift apart the first time a run was postponed.
    /// </summary>
    private long RunTicksRemaining(FacilityInstance executor)
    {
        if (_resolve.CurrentOf(executor) is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = _catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var left = effort - task.WorkDoneThisRun;
        if (left <= 0)
        {
            return 0;
        }

        var rate = _resolve.WorkRate(executor);
        return IntMath.CeilDiv(left, rate);
    }

    /// <summary>
    /// Ticks a whole run costs, derived the same way and for the same reason. Neither the
    /// schematic's effort nor the facility's work rate can change while a run is in progress, so
    /// this is fixed from the moment the run starts without needing a field to hold it — and a
    /// postponement, which does no work, cannot move it.
    /// </summary>
    private long RunTicksTotal(FacilityInstance executor)
    {
        if (_resolve.CurrentOf(executor) is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = _catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var rate = _resolve.WorkRate(executor);

        return IntMath.CeilDiv(effort, rate);
    }

    /// <summary>
    /// How full the belt is, in permille of <see cref="Resolver.Capacity"/>. Quantities are summed
    /// across items rather than converted to volume, because throughput — the number capacity is
    /// built from — is itself a quantity a tick and not a volume a tick.
    /// </summary>
    private long FillPermille(TransportInstance line)
    {
        var capacity = _resolve.Capacity(line);
        return capacity <= 0 ? 0 : line.CargoQuantity * StorageArchetype.FullHold / capacity;
    }

    /// <summary>
    /// What is on the belt, one entry per item, in the order the items first appear travelling
    /// from the destination end back to the source. Aggregated because the view asks what a line
    /// is carrying, not which stretch of belt each parcel is on.
    /// </summary>
    private static IReadOnlyList<BeltCargo> Cargo(TransportInstance line)
    {
        var order = new List<ItemId>();
        var totals = new Dictionary<ItemId, long>();
        foreach (var slot in line.Belt)
        {
            if (slot is null)
            {
                continue;
            }

            if (!totals.ContainsKey(slot.Item))
            {
                order.Add(slot.Item);
                totals[slot.Item] = 0;
            }

            totals[slot.Item] += slot.Quantity;
        }

        var cargo = new List<BeltCargo>(order.Count);
        foreach (var item in order)
        {
            cargo.Add(new BeltCargo(item, totals[item]));
        }

        return cargo;
    }
}
