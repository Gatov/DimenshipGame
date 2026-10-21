using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

/// <summary>
/// The deterministic core, over a catalog and a world state. Holds no wall-clock reference and
/// constructs no random source: all time enters through <see cref="Advance"/>, and anything
/// non-deterministic enters through <see cref="State"/>'s streams or not at all. Pause, speed and
/// offline catch-up belong to the caller, which is what keeps them out of the reproducible path.
/// <para>
/// The planner creates and coordinates demand; executors decide what actually runs. Nothing here
/// follows a plan's sequence — each executor evaluates its own queue every tick.
/// </para>
/// <para>
/// The catalog is the rulebook and the state is this world: the engine reads work rates, capacities
/// and throughputs <b>through the archetype at the point of use</b>, so an upgrade moves a permille
/// on an instance and no schematic, task or run in flight is touched.
/// </para>
/// </summary>
public sealed partial class SimulationEngine : IWorldView
{
    private readonly Dictionary<StorageId, StorageInstance> _storagesById = new();
    private readonly Dictionary<ItemId, ItemDefinition> _items = new();
    private readonly Dictionary<ItemId, long> _holdCapacity = new();
    private readonly Dictionary<ItemId, long> _lastDelta = new();
    private readonly Dictionary<ExecutorId, FacilityInstance> _facilitiesById = new();
    private readonly Dictionary<ExecutorId, TransportInstance> _linesById = new();

    /// <summary>
    /// Which facilities work out of each storage, in declaration order. An index rebuilt from
    /// state rather than saved, like every other dictionary here. A list rather than a single
    /// facility because nothing in content forbids two facilities sharing one buffer, and a
    /// silently dropped second claimant would under-reserve the room they both need.
    /// </summary>
    private readonly Dictionary<StorageId, List<FacilityInstance>> _facilitiesByStorage = new();

    private bool _starvedThisTick;

    /// <summary>
    /// This tick's power decisions for runs already in progress, keyed by task. Scratch for one
    /// tick, cleared before the next, never saved: it is derived entirely from the state at the
    /// start of the production phase.
    /// </summary>
    private readonly Dictionary<TaskId, (bool Granted, long Reserve)> _powerGrants = new();

    /// <summary>Power granted to runs in progress that have not yet stepped this tick.</summary>
    private long _reservedPower;

    /// <summary>
    /// Takes a world as it stands. Every dictionary built here is an index rather than data —
    /// rebuilt from the state on construction, never saved, because an index in a save file is a
    /// second copy of something already there.
    /// </summary>
    public SimulationEngine(ContentCatalog catalog, WorldState state)
    {
        Catalog = catalog;
        State = state;

        foreach (var item in catalog.Items)
        {
            _items[item.Id] = item;
            _holdCapacity[item.Id] = 0;
            _lastDelta[item.Id] = 0;
        }

        foreach (var storage in state.Vessel.Storages)
        {
            _storagesById[storage.Id] = storage;
            foreach (var item in catalog.Items)
            {
                _holdCapacity[item.Id] += CapacityOf(storage, item);
            }
        }

        foreach (var facility in state.Vessel.Facilities)
        {
            _facilitiesById[facility.Id] = facility;

            if (!_facilitiesByStorage.TryGetValue(facility.LocalStorage, out var working))
            {
                working = new List<FacilityInstance>();
                _facilitiesByStorage[facility.LocalStorage] = working;
            }

            working.Add(facility);
        }

        foreach (var line in state.Vessel.Transports)
        {
            _linesById[line.Id] = line;
        }

        Acceptance = new WorkpieceAcceptance(
            catalog, state.Vessel.Facilities.Select(f => (f.LocalStorage, Archetype(f).Type)));

        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Which storages accept which workpieces (K3), derived from the catalog and the vessel's
    /// facilities and never saved. With no item flagged it accepts everything everywhere, which is
    /// what keeps a vessel without workpieces byte-identical to one from before they existed.
    /// </summary>
    public WorkpieceAcceptance Acceptance { get; }

    /// <summary>Starts a campaign from content: seed the scenario, then run the world it made.</summary>
    public static SimulationEngine NewGame(
        ContentCatalog catalog, Scenario scenario, ulong seed = ScenarioSeeder.DefaultSeed) =>
        new(catalog, ScenarioSeeder.Seed(catalog, scenario, seed));

    /// <summary>The rulebook. Immutable, shared by every world open in this process.</summary>
    public ContentCatalog Catalog { get; }

    /// <summary>This world, authoritative. Callers read it; the engine writes it.</summary>
    public WorldState State { get; }

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
        State.Clock.Tick++;
        State.Vessel.Energy.DrawLastTick = 0;
        _starvedThisTick = false;

        var before = new Dictionary<ItemId, long>(_items.Count);
        foreach (var item in Catalog.Items)
        {
            before[item.Id] = TotalOf(item.Id);
        }

        // Standing draw first and unconditionally: sinks, then every executor whatever it is
        // doing. Only the production charge that follows can be refused.
        foreach (var sink in Sinks())
        {
            State.Vessel.Energy.DrawLastTick += sink.PowerDraw;
        }

        foreach (var executor in State.Vessel.Facilities)
        {
            if (!executor.Built)
            {
                executor.PowerDrawLastTick = 0;
                continue;
            }

            State.Vessel.Energy.DrawLastTick += Archetype(executor).StandingPowerDraw;
            executor.PowerDrawLastTick = Archetype(executor).StandingPowerDraw;
        }

        foreach (var hauler in State.Vessel.Transports)
        {
            if (!hauler.Built)
            {
                hauler.PowerDrawLastTick = 0;
                hauler.LoadedLastTick = 0;
                hauler.DeliveredLastTick = 0;
                continue;
            }

            State.Vessel.Energy.DrawLastTick += Archetype(hauler).StandingPowerDraw;
            hauler.PowerDrawLastTick = Archetype(hauler).StandingPowerDraw;

            // Reset beside the draw, and for the same reason: all three describe this tick alone,
            // and a line that moved nothing must report nothing rather than last tick's figure.
            hauler.LoadedLastTick = 0;
            hauler.DeliveredLastTick = 0;
        }

        // Transport runs before production, so material delivered this tick is available to the
        // facility that needs it this tick rather than next.
        foreach (var hauler in State.Vessel.Transports)
        {
            if (!hauler.Built)
            {
                continue;
            }

            StepHauler(hauler);
        }

        // Facilities Built before commissioning this tick. Newly commissioned ones produce from
        // the next tick — the determinism contract with delivery: unit arrives → Built same tick,
        // work starts after.
        var producers = new List<FacilityInstance>();
        foreach (var executor in State.Vessel.Facilities)
        {
            if (executor.Built)
            {
                producers.Add(executor);
            }
        }

        CommissionFacilities();
        GrantPowerToRunsInProgress(producers);

        foreach (var executor in producers)
        {
            StepProducer(executor);

            // Counted after the step, from what the step decided, so the window and the snapshot
            // read the same status and cannot name two different causes for one tick.
            executor.Utilization.Record(
                UtilizationWindow.CategoryOf(executor.Status, executor.BlockReason));
        }

        if (_starvedThisTick)
        {
            State.Vessel.Energy.StarvedTicks++;
        }

        if (State.Vessel.Energy.DrawLastTick >= State.Vessel.Energy.Capacity)
        {
            State.Vessel.Energy.CapHits++;
            Emit(EventCategory.Power, EventCode.PowerCapReached, "vessel", new Dictionary<string, long>
            {
                ["draw"] = State.Vessel.Energy.DrawLastTick,
                ["capacity"] = State.Vessel.Energy.Capacity,
            });
        }

        ReviewWaitingPlans();

        foreach (var item in Catalog.Items)
        {
            _lastDelta[item.Id] = TotalOf(item.Id) - before[item.Id];
        }
    }

    /// <summary>
    /// A task's effective priority: its plan's when it has one, read live, and its own only when
    /// it was queued by hand (D3, Decision 1).
    /// </summary>
    private Priority PriorityOf(TaskInstance task) => State.Plans.Owning(task.Id)?.Priority ?? task.Priority;

    /// <summary>Whether a task's unstarted work is held: its plan's flag, or its own when it has none.</summary>
    private bool IsHeld(TaskInstance task) => State.Plans.Owning(task.Id)?.Held ?? task.Held;

    private long TotalOf(ItemId item)
    {
        var total = 0L;
        foreach (var storage in State.Vessel.Storages)
        {
            total += Available(storage.Id, item);
        }

        return total;
    }

    private void Emit(
        EventCategory category,
        EventCode code,
        string subject,
        IReadOnlyDictionary<string, long> data)
    {
        State.Journal.Events.Enqueue(new SimEvent(State.Clock.Tick, category, code, subject, data));
        State.Journal.TotalEmitted++;

        while (State.Journal.Events.Count > JournalLedger.Capacity)
        {
            State.Journal.Events.Dequeue();
        }
    }

    // Indices and resolution. Everything below reads the archetype at the point of use rather than
    // copying its numbers onto an instance, which is what lets an upgrade move a permille without
    // touching a schematic or a run in flight.

    private FacilityArchetype Archetype(FacilityInstance facility) =>
        Catalog.Facility(facility.Archetype)
        ?? throw new KeyNotFoundException($"No facility archetype '{facility.Archetype}'.");

    private TransportArchetype Archetype(TransportInstance line) =>
        Catalog.Transport(line.Archetype)
        ?? throw new KeyNotFoundException($"No transport archetype '{line.Archetype}'.");

    private StorageArchetype Archetype(StorageInstance storage) =>
        Catalog.Storage(storage.Archetype)
        ?? throw new KeyNotFoundException($"No storage archetype '{storage.Archetype}'.");

    /// <summary>Work per tick after upgrades, floored at one: a facility that does no work per
    /// tick would never finish a run, and a permille of zero is a content error, not a rate.</summary>
    private long WorkRate(FacilityInstance facility) =>
        Math.Max(1, Archetype(facility).WorkRatePerTick * facility.WorkRatePermille / 1000);

    private long SwitchOverTicks(FacilityInstance facility) => Archetype(facility).SwitchOverTicks;

    /// <inheritdoc cref="WorkRate"/>
    private long Throughput(TransportInstance line) =>
        Math.Max(1, Archetype(line).ThroughputPerTick * line.ThroughputPermille / 1000);

    /// <summary>
    /// How much a line can hold: one tick of intake per slot, and one slot per tick of length.
    /// Derived rather than authored, so a capacity can never drift from the throughput and the
    /// distance it is made of.
    /// </summary>
    private long Capacity(TransportInstance line) => Throughput(line) * line.LengthTicks;

    /// <summary>
    /// The tasks queued on a facility or line, in queue order. Instances hold ids and the registry
    /// holds bodies, because a task is referenced from an executor queue, a plan and the journal,
    /// and only one of those can own it.
    /// </summary>
    private IEnumerable<TaskInstance> Queued(IExecutorInstance executor)
    {
        foreach (var id in executor.Queue)
        {
            if (State.Tasks.Task(id) is { } task)
            {
                yield return task;
            }
        }
    }

    /// <summary>The task a facility or line has in hand: a facility's run, a line's transfer.</summary>
    private TaskInstance? CurrentOf(IExecutorInstance executor) =>
        executor.Current is { } id ? State.Tasks.Task(id) : null;

    /// <summary>The facility or line a task is queued on, facilities first, as the indexes were built.</summary>
    private IExecutorInstance? ExecutorOf(ExecutorId id) =>
        _facilitiesById.TryGetValue(id, out var facility) ? facility
        : _linesById.TryGetValue(id, out var line) ? line
        : null;

    /// <summary>Whether a task could start or load now, and why not when it cannot.</summary>
    private delegate bool Readiness(TaskInstance task, out PostponeReason reason);

    /// <summary>
    /// The highest priority among an executor's eligible tasks that are ready now, if any, and
    /// optionally only among priorities strictly above <paramref name="above"/>. Shared by
    /// production and transport, which differ only in what makes a task eligible (a line skips a
    /// transfer already entirely aboard) and in what ready means (start a run, load a slot). The
    /// rule was once written twice and kept in step by hand.
    /// </summary>
    private Priority? TopReadyPriority(
        IExecutorInstance executor, Func<TaskInstance, bool> eligible, Readiness ready, Priority? above)
    {
        Priority? top = null;
        foreach (var task in Queued(executor))
        {
            if (!eligible(task) || (above is { } floor && PriorityOf(task) <= floor)
                || (top is { } best && PriorityOf(task) <= best))
            {
                continue;
            }

            if (ready(task, out _))
            {
                top = PriorityOf(task);
            }
        }

        return top;
    }

    /// <summary>
    /// Says why the tasks a selection did not take are waiting, where priority is the reason. A
    /// ready task below the chosen tier was <see cref="PostponeReason.Outranked"/>, by
    /// <paramref name="chosen"/>. A task above it could not start, and records its own physical
    /// reason, so an urgent order that is not running says what it lacks. Tasks in the chosen tier
    /// are left alone: that is the behaviour from before priority, and keeping it is what makes
    /// default priority neutral. The task in hand is skipped too.
    /// <para>
    /// <paramref name="chosen"/> is passed rather than read off <c>Current</c> because a line clears
    /// <c>Current</c> the moment a transfer is entirely aboard, which can be the very load that won.
    /// <paramref name="categoryOf"/> is the event category a reason is journalled under: a facility
    /// files by reason, a line files everything under logistics.
    /// </para>
    /// </summary>
    private void RecordPassedOver(
        IExecutorInstance executor,
        Priority tier,
        TaskId? chosen,
        Func<TaskInstance, bool> eligible,
        Readiness ready,
        Func<PostponeReason, EventCategory> categoryOf)
    {
        foreach (var task in Queued(executor))
        {
            if (!eligible(task) || PriorityOf(task) == tier || executor.Current == task.Id)
            {
                continue;
            }

            if (PriorityOf(task) > tier)
            {
                ready(task, out var reason);
                PostponeTask(executor.Id, task, reason, categoryOf(reason));
            }
            else if (ready(task, out _))
            {
                PostponeTask(executor.Id, task, PostponeReason.Outranked, categoryOf(PostponeReason.Outranked),
                    byTask: chosen);
            }
        }
    }

    /// <summary>
    /// Takes a finished task out of the executor's queue and out of the live registry, and tells
    /// the plan that owns it. A registry that only ever grew would make a snapshot rebuild and a
    /// planner pass both grow without bound, and a save that grows forever.
    /// <para>
    /// The plan counts completions as they happen rather than rescanning its own task list,
    /// because a retired task is no longer there to scan.
    /// </para>
    /// </summary>
    private void Retire(List<TaskId> queue, TaskId task)
    {
        queue.Remove(task);
        State.Tasks.Retire(task);

        if (State.Plans.Owning(task) is not { } plan)
        {
            return;
        }

        plan.CompletedTasks++;
        if (plan.State == PlanState.Active && plan.IsFinished)
        {
            plan.State = PlanState.Complete;

            // A finished plan needs nothing, so it holds nothing. Anything left (a run's rounding
            // surplus can leave a claim its withdrawals never drew) goes back through allocation.
            foreach (var (storage, item, _) in State.Claims.Release(plan.Id))
            {
                AllocateFree(storage, item);
            }

            Emit(EventCategory.Planning, EventCode.PlanCompleted, plan.Goal.Item.Value,
                new Dictionary<string, long>
                {
                    ["plan"] = plan.Id.Value,
                    ["goal"] = plan.Goal.Quantity,
                });
        }
    }
}
