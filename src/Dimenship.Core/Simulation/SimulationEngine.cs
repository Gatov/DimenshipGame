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
public sealed class SimulationEngine : IWorldView
{
    private readonly Dictionary<StorageId, StorageInstance> _storagesById = new();
    private readonly Dictionary<ItemId, ItemDefinition> _items = new();
    private readonly Dictionary<ItemId, long> _holdCapacity = new();
    private readonly Dictionary<ItemId, long> _lastDelta = new();
    private readonly Dictionary<ExecutorId, FacilityInstance> _facilitiesById = new();
    private readonly Dictionary<ExecutorId, TransportInstance> _linesById = new();

    private bool _starvedThisTick;

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
        }

        foreach (var line in state.Vessel.Transports)
        {
            _linesById[line.Id] = line;
        }

        Snapshot = BuildSnapshot();
    }

    /// <summary>Starts a campaign from content: seed the scenario, then run the world it made.</summary>
    public static SimulationEngine NewGame(
        ContentCatalog catalog, Scenario scenario, ulong seed = ScenarioSeeder.DefaultSeed) =>
        new(catalog, ScenarioSeeder.Seed(catalog, scenario, seed));

    /// <summary>The rulebook. Immutable, shared by every world open in this process.</summary>
    public ContentCatalog Catalog { get; }

    /// <summary>This world, authoritative. Callers read it; the engine writes it.</summary>
    public WorldState State { get; }

    public WorldSnapshot Snapshot { get; private set; }

    /// <summary>
    /// How much of an item a storage currently holds. Read from the instance's own stock list
    /// rather than from an index beside it: the state is authoritative, and a cache of it would be
    /// a second answer that a load could disagree with.
    /// </summary>
    public long Available(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            return 0;
        }

        foreach (var stored in instance.Stock)
        {
            if (stored.Item == item)
            {
                return stored.Amount;
            }
        }

        return 0;
    }

    /// <summary>How much more of an item a storage could accept.</summary>
    public long Room(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance) || !_items.TryGetValue(item, out var known))
        {
            return 0;
        }

        return CapacityOf(instance, known) - Available(storage, item);
    }

    /// <summary>
    /// Injects a task into a compatible executor's queue. The executor decides when it runs;
    /// queue position is a starting point for that decision, not a schedule.
    /// <para>
    /// A null run count is a standing order: run for as long as the inputs keep arriving.
    /// </para>
    /// </summary>
    public TaskId Enqueue(SchematicId schematic, int? runs, ExecutorId executor)
    {
        if (runs is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), runs, "A task must request at least one run.");
        }

        if (!_facilitiesById.TryGetValue(executor, out var target))
        {
            throw new ArgumentException($"No executor '{executor}'.", nameof(executor));
        }

        var definition = Catalog.Schematics.Get(schematic);
        if (!IsUnlocked(schematic))
        {
            throw new ArgumentException(
                $"Schematic '{schematic}' is not unlocked.", nameof(schematic));
        }

        RequireCompatible(definition, target);

        var task = new ProductionTask
        {
            Id = State.Tasks.Mint(),
            SchematicId = schematic,
            RequestedRuns = runs,
            ExecutorId = executor,
        };

        State.Tasks.Add(task);
        target.Queue.Add(task.Id);

        // Event data is a plain long map, so a standing order omits the count rather than carrying
        // a sentinel that every reader would have to know about.
        var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
        if (runs is { } requested)
        {
            data["runs"] = requested;
        }

        Emit(EventCategory.Production, EventCode.TaskQueued, executor.Value, data);

        return task.Id;
    }

    /// <summary>
    /// Injects a transfer into a transport line's queue. The line decides when it moves; a
    /// transfer never reports "waiting for transport", because the line is what does the waiting.
    /// </summary>
    public TaskId EnqueueTransfer(
        ItemId item, long? quantity, StorageId from, StorageId to, ExecutorId executor)
    {
        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A transfer must move at least one unit.");
        }

        if (!_linesById.TryGetValue(executor, out var line))
        {
            throw new ArgumentException($"No transport executor '{executor}'.", nameof(executor));
        }

        RequireKnownItem(item);

        if (!_storagesById.ContainsKey(from))
        {
            throw new ArgumentException($"No storage '{from}'.", nameof(from));
        }

        if (!_storagesById.ContainsKey(to))
        {
            throw new ArgumentException($"No storage '{to}'.", nameof(to));
        }

        if (from == to)
        {
            throw new ArgumentException(
                $"A transfer from '{from}' to itself would move nothing.", nameof(to));
        }

        // A line runs a fixed route. Queueing a transfer it could never make would leave a task
        // sitting in a queue that no line aboard can serve, which reads as a stalled vessel rather
        // than as the planning mistake it is.
        if (line.From != from || line.To != to)
        {
            throw new ArgumentException(
                $"Transport '{executor}' runs '{line.From}' to '{line.To}', " +
                $"not '{from}' to '{to}'.",
                nameof(executor));
        }

        var task = new TransportTask
        {
            Id = State.Tasks.Mint(),
            Item = item,
            RequestedQuantity = quantity,
            Source = from,
            Destination = to,
            ExecutorId = executor,
        };

        State.Tasks.Add(task);
        line.Queue.Add(task.Id);

        var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
        if (quantity is { } requested)
        {
            data["quantity"] = requested;
        }

        Emit(EventCategory.Logistics, EventCode.TaskQueued, executor.Value, data);

        return task.Id;
    }

    /// <summary>
    /// Injects a plan's proposals into executor queues, turning them into runtime tasks. Until
    /// this is called a plan is a description and nothing more.
    /// <para>
    /// A plan carrying shortages commits: the available portion begins immediately, and each
    /// shortage is reported so the player can decide whether to acquire the rest.
    /// </para>
    /// </summary>
    public IReadOnlyList<TaskId> Commit(ProductionPlan plan)
    {
        var created = new List<TaskId>(plan.Runs.Count + plan.Transfers.Count);

        // Transfers first, so the material a run needs is queued to arrive before the run that
        // needs it is queued to start. Executors reorder as they see fit either way; this only
        // decides what the queues look like when they first see them.
        foreach (var transfer in plan.Transfers)
        {
            created.Add(EnqueueTransfer(
                transfer.Item, transfer.Quantity, transfer.From, transfer.To, transfer.Executor));
        }

        foreach (var run in plan.Runs)
        {
            created.Add(Enqueue(run.Schematic, run.Runs, run.Executor));
        }

        // The goal is the only level at which progress is legible: tasks are per-executor by
        // design, so nothing in the task list can answer "how far along is four robot frames"
        // without the plan that grouped them.
        State.Plans.Record(new CommittedPlan
        {
            Id = State.Plans.Mint(),
            Goal = plan.Goal,
            CommittedAtTick = State.Clock.Tick,
            SpawnedTasks = created.ToList(),
            Shortages = plan.Shortages,
        });

        Emit(EventCategory.Planning, EventCode.PlanCommitted, plan.Goal.Item.Value,
            new Dictionary<string, long>
            {
                ["goal"] = plan.Goal.Quantity,
                ["runs"] = plan.Runs.Count,
                ["transfers"] = plan.Transfers.Count,
                ["shortages"] = plan.Shortages.Count,
            });

        foreach (var shortage in plan.Shortages)
        {
            Emit(EventCategory.Planning, EventCode.PlanShortage, shortage.Item.Value,
                new Dictionary<string, long>
                {
                    ["missing"] = shortage.Missing,
                    ["kind"] = (long)shortage.Kind,
                });
        }

        Snapshot = BuildSnapshot();
        return created;
    }

    SchematicCatalog IWorldView.Schematics => Catalog.Schematics;

    StorageId IWorldView.Hold => State.Vessel.Hold;

    /// <summary>
    /// The seam the planner asks through. The unlock set is the world's, not the catalog's: it
    /// changes during play and differs between two players running the same build, which is the
    /// question that sorts campaign progress from rulebook.
    /// </summary>
    public bool IsUnlocked(SchematicId schematic) =>
        State.Progress.UnlockedSchematics.Contains(schematic);

    IReadOnlyList<PlannerFacility> IWorldView.Facilities
    {
        get
        {
            var facilities = new List<PlannerFacility>(State.Vessel.Facilities.Count);
            foreach (var executor in State.Vessel.Facilities)
            {
                var queued = 0L;
                var occupied = false;
                foreach (var task in Queued(executor))
                {
                    if (task.IsFinished)
                    {
                        continue;
                    }

                    // A standing order has no remaining run count to add up. Expressing it as one
                    // would mean choosing a large number, which is the placeholder this replaced.
                    if (task.RequestedRuns is { } requested)
                    {
                        queued += requested - task.CompletedRuns;
                    }
                    else
                    {
                        occupied = true;
                    }
                }

                facilities.Add(new PlannerFacility(
                    executor.Id,
                    Archetype(executor).Type,
                    executor.LocalStorage,
                    queued,
                    occupied));
            }

            return facilities;
        }
    }

    IReadOnlyList<PlannerTransport> IWorldView.TransportLines
    {
        get
        {
            var lines = new List<PlannerTransport>(State.Vessel.Transports.Count);
            foreach (var hauler in State.Vessel.Transports)
            {
                var queued = 0L;
                foreach (var task in Queued(hauler))
                {
                    if (!task.IsFinished)
                    {
                        queued++;
                    }
                }

                lines.Add(new PlannerTransport(
                    hauler.Id,
                    hauler.From,
                    hauler.To,
                    queued));
            }

            return lines;
        }
    }

    /// <inheritdoc />
    public long Uncommitted(ItemId item)
    {
        var total = TotalOf(item);

        foreach (var task in State.Tasks.Production)
        {
            if (task.IsFinished)
            {
                continue;
            }

            var schematic = Catalog.Schematics.Get(task.SchematicId);

            // A standing order is not a claim on a finite quantity: it consumes whatever arrives,
            // for as long as it arrives. Counting a future it has not committed to is what made
            // the default vessel's opening stock read as a deficit of eight billion.
            var remaining = task.RequestedRuns is { } requested
                ? requested - task.CompletedRuns
                : task.RunActive ? 1 : 0;

            // The run in flight has already taken its inputs out of storage, so counting them
            // again would charge the vessel twice for the same material.
            var unstarted = task.RunActive ? remaining - 1 : remaining;

            foreach (var input in schematic.Inputs)
            {
                if (input.Item == item)
                {
                    total -= input.Quantity * unstarted;
                }
            }

            if (schematic.Output.Item == item)
            {
                total += schematic.Output.Quantity * remaining;
            }
        }

        return total;
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

    private void RequireCompatible(SchematicDefinition schematic, FacilityInstance executor)
    {
        var type = Archetype(executor).Type;
        if (schematic.RequiredFacilityType != type)
        {
            throw new ArgumentException(
                $"Schematic '{schematic.Id}' needs a {schematic.RequiredFacilityType}, " +
                $"but '{executor.Id}' is a {type}.");
        }
    }

    private static EventCode CodeFor(PostponeReason reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => EventCode.PostponeInsufficientInput,
        PostponeReason.InsufficientSourceMaterial => EventCode.PostponeInsufficientSource,
        PostponeReason.DestinationFull => EventCode.PostponeDestinationFull,
        PostponeReason.InsufficientEnergy => EventCode.PostponeInsufficientEnergy,
        PostponeReason.OutputRouteUnavailable => EventCode.PostponeOutputRoute,
        PostponeReason.SafetyLock => EventCode.PostponeSafetyLock,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped postpone reason."),
    };

    private static EventCategory CategoryFor(PostponeReason reason) =>
        reason == PostponeReason.InsufficientEnergy ? EventCategory.Power : EventCategory.Production;

    private void RequireKnownItem(ItemId item)
    {
        if (!_items.ContainsKey(item))
        {
            throw new ArgumentException($"Unknown item '{item}'.", "definition");
        }
    }

    private void Deposit(StorageId storage, ItemId item, long quantity) =>
        Set(storage, item, Available(storage, item) + quantity);

    private void Withdraw(StorageId storage, ItemId item, long quantity) =>
        Set(storage, item, Available(storage, item) - quantity);

    /// <summary>
    /// Writes a storage's holding of one item. Appended in first-deposit order and updated in
    /// place afterwards, so a storage's stock list has a stable order and no entry for an item it
    /// has never held.
    /// </summary>
    private void Set(StorageId storage, ItemId item, long amount)
    {
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            return;
        }

        for (var i = 0; i < instance.Stock.Count; i++)
        {
            if (instance.Stock[i].Item == item)
            {
                instance.Stock[i] = instance.Stock[i] with { Amount = amount };
                return;
            }
        }

        instance.Stock.Add(new StoredItem(item, amount));
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
            State.Vessel.Energy.DrawLastTick += Archetype(executor).StandingPowerDraw;
            executor.PowerDrawLastTick = Archetype(executor).StandingPowerDraw;
        }

        foreach (var hauler in State.Vessel.Transports)
        {
            State.Vessel.Energy.DrawLastTick += Archetype(hauler).StandingPowerDraw;
            hauler.PowerDrawLastTick = Archetype(hauler).StandingPowerDraw;

            // Reset beside the draw, and for the same reason: both describe this tick alone, and
            // a line that moved nothing must report nothing rather than last tick's figure.
            hauler.MovedLastTick = 0;
        }

        // Transport runs before production, so material delivered this tick is available to the
        // facility that needs it this tick rather than next.
        foreach (var hauler in State.Vessel.Transports)
        {
            StepHauler(hauler);
        }

        foreach (var executor in State.Vessel.Facilities)
        {
            StepProducer(executor);
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

        foreach (var item in Catalog.Items)
        {
            _lastDelta[item.Id] = TotalOf(item.Id) - before[item.Id];
        }
    }

    private void StepProducer(FacilityInstance executor)
    {
        executor.BlockReason = null;

        if (executor.SwitchOverRemaining > 0)
        {
            AdvanceSwitchOver(executor);
            return;
        }

        // A finished run whose output would not fit holds the facility. Neither the work nor the
        // consumed inputs are lost; the run is deposited as soon as room appears.
        if (CurrentJob(executor) is { RunAwaitingDeposit: true } holding)
        {
            if (TryDeposit(executor, holding))
            {
                executor.Status = ExecutorStatus.RunningTask;
            }

            return;
        }

        if (CurrentJob(executor) is { RunActive: true } running)
        {
            AdvanceRun(executor, running);
            return;
        }

        SelectAndStart(executor);
    }

    private void SelectAndStart(FacilityInstance executor)
    {
        // 1. Continue the current task when its next run can start. Preferring the work already
        //    configured is what keeps a facility producing instead of reconfiguring.
        if (CurrentJob(executor) is { } current && !current.IsFinished && CanStart(executor, current, out _))
        {
            StartRun(executor, current);
            return;
        }

        // 2. Any queued task using the configuration already loaded.
        if (executor.Configured is { } configured)
        {
            foreach (var task in Queued(executor))
            {
                if (!task.IsFinished && task.SchematicId == configured && CanStart(executor, task, out _))
                {
                    executor.Current = task.Id;
                    StartRun(executor, task);
                    return;
                }
            }
        }

        // 3. A runnable task on a different schematic, which costs a reconfiguration. A facility
        //    that has never been configured has nothing to tear down and pays nothing.
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || !CanStart(executor, task, out _))
            {
                continue;
            }

            executor.Current = task.Id;

            if (executor.Configured is null || SwitchOverTicks(executor) <= 0)
            {
                executor.Configured = task.SchematicId;
                StartRun(executor, task);
                return;
            }

            executor.SwitchOverRemaining = SwitchOverTicks(executor);
            executor.SwitchTarget = task.Id;
            Emit(EventCategory.Production, EventCode.SwitchOverStarted, executor.Id.Value,
                new Dictionary<string, long>
                {
                    ["task"] = task.Id.Value,
                    ["ticks"] = SwitchOverTicks(executor),
                });

            // The tick that decides to reconfigure is the first tick of the reconfiguration, not
            // a free one spent deciding. Otherwise a switch-over always costs its ticks plus one.
            AdvanceSwitchOver(executor);
            return;
        }

        // 4. Nothing can run. Every unfinished task records why, which is what turns "the vessel
        //    stopped" into "the vessel stopped because these three things are missing".
        var pending = 0;
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished)
            {
                continue;
            }

            pending++;
            CanStart(executor, task, out var reason);
            Postpone(executor, task, reason);
        }

        if (pending == 0)
        {
            executor.Status = ExecutorStatus.NoTasksQueued;
            executor.Current = null;
            return;
        }

        if (executor.Status != ExecutorStatus.AllQueuedTasksBlocked)
        {
            Emit(EventCategory.Production, EventCode.AllTasksBlocked, executor.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    private void StepHauler(TransportInstance hauler)
    {
        hauler.BlockReason = null;

        // Continue the transfer already in hand before looking at anything else, for the same
        // reason a facility prefers its loaded configuration: finishing beats starting.
        if (CurrentTransfer(hauler) is { } current && !current.IsFinished && TryMove(hauler, current))
        {
            return;
        }

        foreach (var task in Queued(hauler))
        {
            if (!task.IsFinished && TryMove(hauler, task))
            {
                return;
            }
        }

        var pending = 0;
        foreach (var task in Queued(hauler))
        {
            if (task.IsFinished)
            {
                continue;
            }

            pending++;
            CanMove(hauler, task, out _, out var reason);
            Postpone(hauler, task, reason);
        }

        if (pending == 0)
        {
            hauler.Status = ExecutorStatus.NoTasksQueued;
            hauler.Current = null;
            return;
        }

        if (hauler.Status != ExecutorStatus.AllQueuedTasksBlocked)
        {
            Emit(EventCategory.Logistics, EventCode.AllTasksBlocked, hauler.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        hauler.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    private bool CanMove(TransportInstance hauler, TransportTask task, out long quantity, out PostponeReason reason)
    {
        // A standing order is bounded by what is at the source and what fits at the destination,
        // and by nothing else.
        var outstanding = task.RequestedQuantity is { } requested
            ? requested - task.MovedQuantity
            : long.MaxValue;
        var atSource = Available(task.Source, task.Item);
        var room = Room(task.Destination, task.Item);

        quantity = Math.Min(
            Math.Min(Throughput(hauler), outstanding),
            Math.Min(atSource, room));

        if (quantity > 0)
        {
            reason = PostponeReason.SafetyLock;
            return true;
        }

        // Source first: an empty source is the ordinary case, and reporting a full destination
        // when there is also nothing to move would send the player to the wrong end of the route.
        reason = atSource <= 0
            ? PostponeReason.InsufficientSourceMaterial
            : PostponeReason.DestinationFull;
        return false;
    }

    private bool TryMove(TransportInstance hauler, TransportTask task)
    {
        if (!CanMove(hauler, task, out var quantity, out _))
        {
            return false;
        }

        Withdraw(task.Source, task.Item, quantity);
        Deposit(task.Destination, task.Item, quantity);
        task.MovedQuantity += quantity;
        hauler.MovedLastTick += quantity;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        hauler.Current = task.Id;
        hauler.Status = ExecutorStatus.RunningTask;

        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null))
        {
            var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
            if (task.RequestedQuantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TransferStarted, hauler.Id.Value, data);
        }

        if (task.RequestedQuantity is { } target && task.MovedQuantity >= target)
        {
            task.State = TaskState.Complete;
            task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);
            hauler.Current = null;
            Emit(EventCategory.Logistics, EventCode.TransferCompleted, hauler.Id.Value,
                new Dictionary<string, long>
                {
                    ["task"] = task.Id.Value,
                    ["moved"] = task.MovedQuantity,
                });
            Retire(hauler.Queue, task.Id);
        }

        return true;
    }

    private void Postpone(TransportInstance hauler, TransportTask task, PostponeReason reason)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = State.Clock.Tick;
        hauler.BlockReason = reason;

        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Postponed, reason))
        {
            Emit(EventCategory.Logistics, CodeFor(reason), hauler.Id.Value, SimEvent.NoData);
        }
    }

    private void AdvanceSwitchOver(FacilityInstance executor)
    {
        executor.SwitchOverRemaining--;
        executor.Status = ExecutorStatus.SwitchingOver;

        if (executor.SwitchOverRemaining > 0)
        {
            return;
        }

        var target = State.Tasks.Job(executor.SwitchTarget!.Value)!;
        executor.Configured = target.SchematicId;
        executor.SwitchTarget = null;
        Emit(EventCategory.Production, EventCode.SwitchOverCompleted, executor.Id.Value,
            new Dictionary<string, long> { ["task"] = target.Id.Value });
    }

    private bool CanStart(FacilityInstance executor, ProductionTask task, out PostponeReason reason)
    {
        var schematic = Catalog.Schematics.Get(task.SchematicId);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            if (Available(storage, input.Item) < input.Quantity)
            {
                reason = PostponeReason.InsufficientInputMaterial;
                return false;
            }
        }

        // Room is checked before anything is consumed. A facility that cannot place its output
        // must not shred its input for nothing.
        if (Room(storage, schematic.Output.Item) < schematic.Output.Quantity)
        {
            reason = PostponeReason.DestinationFull;
            return false;
        }

        reason = PostponeReason.SafetyLock;
        return true;
    }

    private void StartRun(FacilityInstance executor, ProductionTask task)
    {
        var schematic = Catalog.Schematics.Get(task.SchematicId);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            Withdraw(storage, input.Item, input.Quantity);
        }

        executor.Current = task.Id;
        task.RunActive = true;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null);

        var started = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["run"] = task.CompletedRuns + 1,
        };
        if (task.RequestedRuns is { } requestedRuns)
        {
            started["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunStarted, executor.Id.Value, started);

        AdvanceRun(executor, task);
    }

    private void AdvanceRun(FacilityInstance executor, ProductionTask task)
    {
        var schematic = Catalog.Schematics.Get(task.SchematicId);
        var effort = schematic.EffortPerRun.Value;
        var work = Math.Min(WorkRate(executor), effort - task.WorkDoneThisRun);

        // Charged cumulatively rather than as a per-tick slice: the final tick's work equals the
        // full effort, so the target lands exactly on the schematic's energy and the rounding
        // remainder settles itself with no special case.
        var targetTotal = schematic.EnergyPerRun.Value * (task.WorkDoneThisRun + work) / effort;
        var charge = targetTotal - task.EnergyChargedThisRun;

        if (State.Vessel.Energy.DrawLastTick + charge > State.Vessel.Energy.Capacity)
        {
            _starvedThisTick = true;
            Postpone(executor, task, PostponeReason.InsufficientEnergy, new Dictionary<string, long>
            {
                ["required"] = charge,
                ["reserve"] = State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick,
            });
            executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
            return;
        }

        State.Vessel.Energy.DrawLastTick += charge;
        executor.PowerDrawLastTick += charge;
        task.EnergyChargedThisRun = targetTotal;
        task.WorkDoneThisRun += work;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        executor.Status = ExecutorStatus.RunningTask;

        if (task.WorkDoneThisRun >= effort)
        {
            TryDeposit(executor, task);
        }
    }

    private bool TryDeposit(FacilityInstance executor, ProductionTask task)
    {
        var schematic = Catalog.Schematics.Get(task.SchematicId);
        var storage = executor.LocalStorage;

        if (Room(storage, schematic.Output.Item) < schematic.Output.Quantity)
        {
            task.RunAwaitingDeposit = true;
            Postpone(executor, task, PostponeReason.DestinationFull, new Dictionary<string, long>
            {
                ["room"] = Room(storage, schematic.Output.Item),
                ["need"] = schematic.Output.Quantity,
            });
            executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
            return false;
        }

        Deposit(storage, schematic.Output.Item, schematic.Output.Quantity);
        task.RunActive = false;
        task.RunAwaitingDeposit = false;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.CompletedRuns++;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.RunCompleted, null);

        var done = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["done"] = task.CompletedRuns,
        };
        if (task.RequestedRuns is { } requestedRuns)
        {
            done["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunCompleted, executor.Id.Value, done);

        if (task.RequestedRuns is { } target && task.CompletedRuns >= target)
        {
            task.State = TaskState.Complete;
            task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);
            executor.Current = null;
            Emit(EventCategory.Production, EventCode.TaskCompleted, executor.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
            Retire(executor.Queue, task.Id);
        }

        return true;
    }

    private void Postpone(FacilityInstance executor, ProductionTask task, PostponeReason reason) =>
        Postpone(executor, task, reason, SimEvent.NoData);

    private void Postpone(
        FacilityInstance executor,
        ProductionTask task,
        PostponeReason reason,
        IReadOnlyDictionary<string, long> data)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = State.Clock.Tick;
        executor.BlockReason = reason;

        // Edge-triggered. A task blocked on the same thing for a thousand ticks made one
        // decision, not a thousand, and emitting it every tick would bury everything else in the
        // console within seconds.
        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Postponed, reason))
        {
            Emit(CategoryFor(reason), CodeFor(reason), executor.Id.Value, data);
        }
    }

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
            var totalAmount = 0L;
            var totalCapacity = 0L;
            foreach (var item in Catalog.Items)
            {
                var stock = new ItemStock(
                    item.Id, Available(storage.Id, item.Id), CapacityOf(storage, item));
                contents.Add(stock);
                totalAmount += stock.Amount;
                totalCapacity += stock.Capacity;
            }

            storages.Add(new StorageState(
                storage.Id,
                WorldState.NameOf(Catalog, storage),
                totalAmount,
                totalCapacity,
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
                executor.Status,
                executor.Configured,
                executor.Current,
                executor.PowerDrawLastTick,
                RunTicksRemaining(executor),
                RunTicksTotal(executor),
                executor.SwitchOverRemaining,
                executor.BlockReason));
        }

        var transports = new List<TransportExecutorState>(State.Vessel.Transports.Count);
        foreach (var hauler in State.Vessel.Transports)
        {
            transports.Add(new TransportExecutorState(
                hauler.Id,
                WorldState.NameOf(Catalog, hauler),
                hauler.From,
                hauler.To,
                hauler.Status,
                hauler.Current,
                CurrentTransfer(hauler)?.Item,
                Throughput(hauler),
                hauler.MovedLastTick,
                hauler.PowerDrawLastTick,
                hauler.BlockReason));
        }

        var sinks = new List<PowerSinkState>();
        foreach (var sink in Sinks())
        {
            sinks.Add(new PowerSinkState(sink.Id.Value, sink.Label, sink.PowerDraw));
        }

        var tasks = new List<ProductionTaskState>(State.Tasks.Production.Count);
        foreach (var task in State.Tasks.Production)
        {
            tasks.Add(new ProductionTaskState(
                task.Id,
                task.SchematicId,
                task.ExecutorId,
                task.RequestedRuns,
                task.CompletedRuns,
                task.State,
                task.LastReason,
                task.PostponedAtTick));
        }

        var transfers = new List<TransportTaskState>(State.Tasks.Transport.Count);
        foreach (var task in State.Tasks.Transport)
        {
            transfers.Add(new TransportTaskState(
                task.Id,
                task.Item,
                task.ExecutorId,
                task.Source,
                task.Destination,
                task.RequestedQuantity,
                task.MovedQuantity,
                task.State,
                task.LastReason,
                task.PostponedAtTick));
        }

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
            transfers,
            State.Journal.Events.ToList(),
            State.Journal.TotalEmitted);
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

        var effort = Catalog.Schematics.Get(task.SchematicId).EffortPerRun.Value;
        var left = effort - task.WorkDoneThisRun;
        if (left <= 0)
        {
            return 0;
        }

        var rate = WorkRate(executor);
        return (left + rate - 1) / rate;
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

        var effort = Catalog.Schematics.Get(task.SchematicId).EffortPerRun.Value;
        var rate = WorkRate(executor);

        return (effort + rate - 1) / rate;
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

    private IEnumerable<PowerSinkDefinition> Sinks()
    {
        foreach (var id in State.Vessel.Sinks)
        {
            foreach (var sink in Catalog.Sinks)
            {
                if (sink.Id == id)
                {
                    yield return sink;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The tasks queued on a facility, in queue order. Instances hold ids and the registry holds
    /// bodies, because a task is referenced from an executor queue, a plan and the journal, and
    /// only one of those can own it.
    /// </summary>
    private IEnumerable<ProductionTask> Queued(FacilityInstance facility)
    {
        foreach (var id in facility.Queue)
        {
            if (State.Tasks.Job(id) is { } task)
            {
                yield return task;
            }
        }
    }

    /// <inheritdoc cref="Queued(FacilityInstance)"/>
    private IEnumerable<TransportTask> Queued(TransportInstance line)
    {
        foreach (var id in line.Queue)
        {
            if (State.Tasks.Transfer(id) is { } task)
            {
                yield return task;
            }
        }
    }

    private ProductionTask? CurrentJob(FacilityInstance facility) =>
        facility.Current is { } id ? State.Tasks.Job(id) : null;

    private TransportTask? CurrentTransfer(TransportInstance line) =>
        line.Current is { } id ? State.Tasks.Transfer(id) : null;

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
        }
    }

    private long CapacityOf(StorageInstance storage, ItemDefinition item) =>
        item.HoldCapacity * Archetype(storage).CapacityPermille / StorageArchetype.FullHold;
}
