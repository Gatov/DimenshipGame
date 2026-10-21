using Dimenship.Core.Content;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    private WorldSnapshot BuildSnapshot()
    {
        // Built from the definition's ordering rather than dictionary ordering, so the lists
        // are stable across runs.
        var resources = new List<ResourceStock>(Catalog.Items.Count);
        foreach (var item in Catalog.Items)
        {
            resources.Add(new ResourceStock(
                item.Id, TotalOf(item.Id), _holdCapacity[item.Id], _lastDelta[item.Id]));
        }

        var storages = new List<StorageState>(State.Vessel.Storages.Count);
        foreach (var storage in State.Vessel.Storages)
        {
            var contents = new List<ItemStock>(Catalog.Items.Count);
            foreach (var item in Catalog.Items)
            {
                contents.Add(new ItemStock(
                    item.Id,
                    Available(storage.Id, item.Id),
                    CapacityOf(storage, item),
                    State.Claims.HeldAt(storage.Id, item.Id)));
            }

            storages.Add(new StorageState(
                storage.Id,
                WorldState.NameOf(Catalog, storage),
                FillPermille(storage.Id),
                contents));
        }

        var executors = new List<ExecutorState>(State.Vessel.Facilities.Count);
        foreach (var executor in State.Vessel.Facilities)
        {
            executors.Add(new ExecutorState(
                executor.Id,
                WorldState.NameOf(Catalog, executor),
                Archetype(executor).Type,
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
                CurrentJob(executor) is { RunActive: true } running
                    ? Catalog.Schematics.Get(running.Produce.Schematic).Output
                    : null,
                executor.SwitchTarget is { } target && executor.SwitchOverRemaining > 0
                    ? State.Tasks.Task(target)?.Produce.Schematic
                    : null,
                SwitchOverTicks(executor)));
        }

        var transports = new List<TransportExecutorState>(State.Vessel.Transports.Count);
        foreach (var hauler in State.Vessel.Transports)
        {
            transports.Add(new TransportExecutorState(
                hauler.Id,
                WorldState.NameOf(Catalog, hauler),
                hauler.From,
                hauler.To,
                hauler.Built,
                hauler.Status,
                hauler.Current,
                Cargo(hauler),
                Throughput(hauler),
                hauler.LengthTicks,
                Capacity(hauler),
                FillPermille(hauler),
                hauler.LoadedLastTick,
                hauler.DeliveredLastTick,
                hauler.PowerDrawLastTick,
                hauler.BlockReason));
        }

        var sinks = new List<PowerSinkState>();
        foreach (var sink in Sinks())
        {
            sinks.Add(new PowerSinkState(sink.Id.Value, sink.Label, sink.PowerDraw));
        }

        var tasks = new List<TaskInstanceState>(State.Tasks.All.Count);
        foreach (var task in State.Tasks.All)
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
                PriorityOf(task),
                IsHeld(task),
                cause?.ByTask,
                cause?.ByPlan));
        }

        var plans = new List<CommittedPlanState>(State.Plans.Plans.Count);
        foreach (var plan in State.Plans.Plans)
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

        var alerts = State.Alerts.Alerts
            .Select(a => new AlertState(
                a.Id, a.Severity, a.Code, a.SubjectId, a.RaisedAtTick, a.RootCause, a.RelatedSubjectId,
                a.Acknowledged, a.Pinned))
            .ToList();

        return new WorldSnapshot(
            State.Clock.Tick,
            resources,
            storages,
            new EnergyState(
                State.Vessel.Energy.Capacity,
                State.Vessel.Energy.DrawLastTick,
                State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick,
                State.Vessel.Energy.CapHits,
                State.Vessel.Energy.StarvedTicks),
            executors,
            transports,
            sinks,
            tasks,
            plans,
            State.Journal.Events.ToList(),
            State.Journal.TotalEmitted,
            InProcess(),
            State.Claims.Entries.Select(c => new ClaimState(c.Plan, c.Storage, c.Item, c.Held)).ToList(),
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
        foreach (var task in State.Tasks.All)
        {
            if (!task.IsProduce || !task.RunActive)
            {
                continue;
            }

            foreach (var input in Catalog.Schematics.Get(task.Produce.Schematic).Inputs)
            {
                inRuns[input.Item] = inRuns.GetValueOrDefault(input.Item) + input.Quantity;
            }
        }

        var onBelts = new Dictionary<ItemId, long>();
        foreach (var line in State.Vessel.Transports)
        {
            foreach (var slot in line.Belt)
            {
                if (slot is not null)
                {
                    onBelts[slot.Item] = onBelts.GetValueOrDefault(slot.Item) + slot.Quantity;
                }
            }
        }

        var items = new List<ItemInProcess>(Catalog.Items.Count);
        foreach (var item in Catalog.Items)
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
        if (CurrentJob(executor) is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var left = effort - task.WorkDoneThisRun;
        if (left <= 0)
        {
            return 0;
        }

        var rate = WorkRate(executor);
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
        if (CurrentJob(executor) is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var rate = WorkRate(executor);

        return IntMath.CeilDiv(effort, rate);
    }

    /// <summary>
    /// How full the belt is, in permille of <see cref="Capacity"/>. Quantities are summed across
    /// items rather than converted to volume, because throughput — the number capacity is built
    /// from — is itself a quantity a tick and not a volume a tick.
    /// </summary>
    private long FillPermille(TransportInstance line)
    {
        var capacity = Capacity(line);
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
