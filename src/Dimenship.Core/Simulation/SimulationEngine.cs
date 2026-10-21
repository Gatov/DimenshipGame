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
    private readonly Dictionary<ItemId, long> _lastDelta = new();
    private readonly Dictionary<ExecutorId, FacilityInstance> _facilitiesById = new();
    private readonly Dictionary<ExecutorId, TransportInstance> _linesById = new();

    // The stateless collaborators (R2.2). Each is handed the catalog, the state and the ones before
    // it, never the engine, so none of them can read the per-tick scratch below.
    private readonly Resolver _resolve;
    private readonly StorageMath _storage;
    private readonly EngineWorldView _view;
    private readonly SnapshotBuilder _snapshots;

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
            _lastDelta[item.Id] = 0;
        }

        foreach (var facility in state.Vessel.Facilities)
        {
            _facilitiesById[facility.Id] = facility;
        }

        foreach (var line in state.Vessel.Transports)
        {
            _linesById[line.Id] = line;
        }

        _resolve = new Resolver(catalog, state);
        Acceptance = new WorkpieceAcceptance(
            catalog, state.Vessel.Facilities.Select(f => (f.LocalStorage, Archetype(f).Type)));
        _storage = new StorageMath(catalog, state, _resolve, Acceptance);
        _view = new EngineWorldView(catalog, state, _resolve, _storage, Acceptance);
        _snapshots = new SnapshotBuilder(catalog, state, _resolve, _storage);

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

        var before = new Dictionary<ItemId, long>(Catalog.Items.Count);
        foreach (var item in Catalog.Items)
        {
            before[item.Id] = _storage.TotalOf(item.Id);
        }

        // Standing draw first and unconditionally: sinks, then every executor whatever it is
        // doing. Only the production charge that follows can be refused.
        foreach (var sink in _resolve.Sinks())
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
            _lastDelta[item.Id] = _storage.TotalOf(item.Id) - before[item.Id];
        }
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

    // The engine's own spellings of the collaborators' answers, so a phase reads WorkRate(executor)
    // and Available(storage, item) as it always did. The rules themselves live in Resolver and
    // StorageMath; nothing here adds to them.

    private FacilityArchetype Archetype(FacilityInstance facility) => _resolve.Archetype(facility);

    private TransportArchetype Archetype(TransportInstance line) => _resolve.Archetype(line);

    private long WorkRate(FacilityInstance facility) => _resolve.WorkRate(facility);

    private long SwitchOverTicks(FacilityInstance facility) => _resolve.SwitchOverTicks(facility);

    private long Throughput(TransportInstance line) => _resolve.Throughput(line);

    private long Capacity(TransportInstance line) => _resolve.Capacity(line);

    private IEnumerable<TaskInstance> Queued(IExecutorInstance executor) => _resolve.Queued(executor);

    private TaskInstance? CurrentOf(IExecutorInstance executor) => _resolve.CurrentOf(executor);

    private Priority PriorityOf(TaskInstance task) => _resolve.PriorityOf(task);

    private bool IsHeld(TaskInstance task) => _resolve.IsHeld(task);

    /// <inheritdoc cref="StorageMath.Available"/>
    public long Available(StorageId storage, ItemId item) => _storage.Available(storage, item);

    /// <inheritdoc cref="StorageMath.Free"/>
    public long Free(StorageId storage, ItemId item) => _storage.Free(storage, item);

    /// <inheritdoc cref="StorageMath.Room"/>
    public long Room(StorageId storage, ItemId item) => _storage.Room(storage, item);

    /// <inheritdoc cref="StorageMath.RoomForDelivery"/>
    public long RoomForDelivery(StorageId storage, ItemId item) => _storage.RoomForDelivery(storage, item);

    /// <inheritdoc cref="StorageMath.FillPermille"/>
    public long FillPermille(StorageId storage) => _storage.FillPermille(storage);

    private WorldSnapshot BuildSnapshot() => _snapshots.Build(_lastDelta);

    SchematicCatalog IWorldView.Schematics => _view.Schematics;

    bool IWorldView.Accepts(StorageId storage, ItemId item) => _view.Accepts(storage, item);

    StorageId IWorldView.Hold => _view.Hold;

    IReadOnlyList<PlannerFacility> IWorldView.Facilities => _view.Facilities;

    IReadOnlyList<PlannerTransport> IWorldView.TransportLines => _view.TransportLines;

    /// <inheritdoc cref="EngineWorldView.IsUnlocked"/>
    public bool IsUnlocked(SchematicId schematic) => _view.IsUnlocked(schematic);

    /// <inheritdoc cref="EngineWorldView.InHold"/>
    public long InHold(ItemId item) => _view.InHold(item);

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
