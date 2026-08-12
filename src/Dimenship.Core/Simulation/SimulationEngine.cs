using Dimenship.Core.Planning;
using Dimenship.Core.Production;

namespace Dimenship.Core.Simulation;

/// <summary>
/// The deterministic core. Holds no wall-clock reference and constructs no random source:
/// all time enters through <see cref="Advance"/>. Pause, speed and offline catch-up belong
/// to the caller, which is what keeps them out of the reproducible path.
/// <para>
/// The planner creates and coordinates demand; executors decide what actually runs. Nothing here
/// follows a plan's sequence — each executor evaluates its own queue every tick.
/// </para>
/// </summary>
public sealed class SimulationEngine : IWorldView
{
    public const int EventBufferCapacity = 512;

    private readonly WorldDefinition _definition;
    private readonly Dictionary<(StorageId Storage, ItemId Item), long> _stock = new();
    private readonly Dictionary<StorageId, StorageDefinition> _storages = new();
    private readonly Dictionary<ItemId, ItemDefinition> _items = new();
    private readonly Dictionary<ItemId, long> _holdCapacity = new();
    private readonly Dictionary<ItemId, long> _lastDelta = new();
    private readonly Queue<SimEvent> _events = new();
    private readonly List<Executor> _executors = new();
    private readonly Dictionary<ExecutorId, Executor> _executorsById = new();
    private readonly List<Hauler> _haulers = new();
    private readonly Dictionary<ExecutorId, Hauler> _haulersById = new();
    private readonly List<ProductionTask> _tasks = new();
    private readonly List<TransportTask> _transfers = new();
    private readonly HashSet<SchematicId> _unlocked;

    private long _tick;
    private long _totalEventsEmitted;
    private long _draw;
    private long _nextTaskId;
    private int _capHits;
    private int _starvedTicks;
    private bool _starvedThisTick;

    public SimulationEngine(WorldDefinition definition)
    {
        _definition = definition;

        foreach (var unlocked in definition.UnlockedSchematics)
        {
            if (!definition.Schematics.TryGet(unlocked, out _))
            {
                throw new ArgumentException(
                    $"Cannot unlock unknown schematic '{unlocked}'.", nameof(definition));
            }
        }

        _unlocked = new HashSet<SchematicId>(definition.UnlockedSchematics);

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

        var standing = 0L;
        foreach (var sink in definition.Sinks)
        {
            standing += sink.PowerDraw;
        }

        foreach (var producer in definition.Producers)
        {
            if (!_storages.ContainsKey(producer.LocalStorage))
            {
                throw new ArgumentException(
                    $"Executor '{producer.Id}' names unknown storage '{producer.LocalStorage}'.",
                    nameof(definition));
            }

            if (producer.WorkRatePerTick <= 0)
            {
                throw new ArgumentException(
                    $"Executor '{producer.Id}' has a work rate of {producer.WorkRatePerTick}; " +
                    "a facility that does no work per tick would never finish a run.",
                    nameof(definition));
            }

            if (producer.InitialSchematic is { } initial)
            {
                RequireCompatible(definition.Schematics.Get(initial), producer);
            }

            var executor = new Executor(producer) { Configured = producer.InitialSchematic };
            _executors.Add(executor);
            if (!_executorsById.TryAdd(producer.Id, executor))
            {
                throw new ArgumentException($"Duplicate executor '{producer.Id}'.", nameof(definition));
            }

            standing += producer.StandingPowerDraw;
        }

        foreach (var transport in definition.Transports)
        {
            if (transport.ThroughputPerTick <= 0)
            {
                throw new ArgumentException(
                    $"Transport '{transport.Id}' moves {transport.ThroughputPerTick} per tick; " +
                    "a line that carries nothing would never finish a transfer.",
                    nameof(definition));
            }

            if (!_storages.ContainsKey(transport.From))
            {
                throw new ArgumentException(
                    $"Transport '{transport.Id}' runs from '{transport.From}', which is not a " +
                    "storage. A route must join two places that exist.",
                    nameof(definition));
            }

            if (!_storages.ContainsKey(transport.To))
            {
                throw new ArgumentException(
                    $"Transport '{transport.Id}' runs to '{transport.To}', which is not a " +
                    "storage. A route must join two places that exist.",
                    nameof(definition));
            }

            if (transport.From == transport.To)
            {
                throw new ArgumentException(
                    $"Transport '{transport.Id}' runs from '{transport.From}' to itself; " +
                    "a line that ends where it starts would move nothing.",
                    nameof(definition));
            }

            var hauler = new Hauler(transport);
            _haulers.Add(hauler);
            if (!_haulersById.TryAdd(transport.Id, hauler) || _executorsById.ContainsKey(transport.Id))
            {
                throw new ArgumentException($"Duplicate executor '{transport.Id}'.", nameof(definition));
            }

            standing += transport.StandingPowerDraw;
        }

        // Standing draw is claimed unconditionally and can never be refused, so a vessel whose
        // standing draw alone exceeds capacity could not honour both that rule and the invariant
        // that draw never exceeds capacity. It is a definition error, and it is caught here
        // rather than producing a negative reserve at some later tick.
        if (standing > definition.EnergyCapacity)
        {
            throw new ArgumentException(
                $"Standing power draw is {standing} against a capacity of {definition.EnergyCapacity}. " +
                "Sinks and idle executors must fit within capacity.",
                nameof(definition));
        }

        foreach (var task in definition.InitialTasks)
        {
            Enqueue(task.Schematic, task.Runs, task.Executor);
        }

        foreach (var transfer in definition.InitialTransfers)
        {
            EnqueueTransfer(
                transfer.Item, transfer.Quantity, transfer.From, transfer.To, transfer.Executor);
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

        if (!_executorsById.TryGetValue(executor, out var target))
        {
            throw new ArgumentException($"No executor '{executor}'.", nameof(executor));
        }

        var definition = _definition.Schematics.Get(schematic);
        if (!IsUnlocked(schematic))
        {
            throw new ArgumentException(
                $"Schematic '{schematic}' is not unlocked.", nameof(schematic));
        }

        RequireCompatible(definition, target.Definition);

        var task = new ProductionTask
        {
            Id = new TaskId(++_nextTaskId),
            SchematicId = schematic,
            RequestedRuns = runs,
            ExecutorId = executor,
        };

        target.Queue.Add(task);
        _tasks.Add(task);

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

        if (!_haulersById.TryGetValue(executor, out var line))
        {
            throw new ArgumentException($"No transport executor '{executor}'.", nameof(executor));
        }

        RequireKnownItem(item);

        if (!_storages.ContainsKey(from))
        {
            throw new ArgumentException($"No storage '{from}'.", nameof(from));
        }

        if (!_storages.ContainsKey(to))
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
        if (line.Definition.From != from || line.Definition.To != to)
        {
            throw new ArgumentException(
                $"Transport '{executor}' runs '{line.Definition.From}' to '{line.Definition.To}', " +
                $"not '{from}' to '{to}'.",
                nameof(executor));
        }

        var task = new TransportTask
        {
            Id = new TaskId(++_nextTaskId),
            Item = item,
            RequestedQuantity = quantity,
            Source = from,
            Destination = to,
            ExecutorId = executor,
        };

        line.Queue.Add(task);
        _transfers.Add(task);

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

    SchematicCatalog IWorldView.Schematics => _definition.Schematics;

    StorageId IWorldView.Hold => _definition.Storages[0].Id;

    /// <summary>
    /// The seam the planner asks through. The unlock set is the world's, not the catalog's: it
    /// changes during play and differs between two players running the same build, which is the
    /// question that sorts campaign progress from rulebook.
    /// </summary>
    public bool IsUnlocked(SchematicId schematic) => _unlocked.Contains(schematic);

    IReadOnlyList<PlannerFacility> IWorldView.Facilities
    {
        get
        {
            var facilities = new List<PlannerFacility>(_executors.Count);
            foreach (var executor in _executors)
            {
                var queued = 0L;
                var occupied = false;
                foreach (var task in executor.Queue)
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
                    executor.Definition.Id,
                    executor.Definition.Type,
                    executor.Definition.LocalStorage,
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
            var lines = new List<PlannerTransport>(_haulers.Count);
            foreach (var hauler in _haulers)
            {
                var queued = 0L;
                foreach (var task in hauler.Queue)
                {
                    if (!task.IsFinished)
                    {
                        queued++;
                    }
                }

                lines.Add(new PlannerTransport(
                    hauler.Definition.Id,
                    hauler.Definition.From,
                    hauler.Definition.To,
                    queued));
            }

            return lines;
        }
    }

    /// <inheritdoc />
    public long Uncommitted(ItemId item)
    {
        var total = TotalOf(item);

        foreach (var task in _tasks)
        {
            if (task.IsFinished)
            {
                continue;
            }

            var schematic = _definition.Schematics.Get(task.SchematicId);

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

    private static long CapacityOf(StorageDefinition storage, ItemDefinition item) =>
        item.HoldCapacity * storage.CapacityPermille / StorageDefinition.FullHold;

    private static void RequireCompatible(SchematicDefinition schematic, ProductionExecutorDefinition executor)
    {
        if (schematic.RequiredFacilityType != executor.Type)
        {
            throw new ArgumentException(
                $"Schematic '{schematic.Id}' needs a {schematic.RequiredFacilityType}, " +
                $"but '{executor.Id}' is a {executor.Type}.");
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
        _stock[(storage, item)] = Available(storage, item) + quantity;

    private void Withdraw(StorageId storage, ItemId item, long quantity) =>
        _stock[(storage, item)] = Available(storage, item) - quantity;

    private void Tick()
    {
        _tick++;
        _draw = 0;
        _starvedThisTick = false;

        var before = new Dictionary<ItemId, long>(_items.Count);
        foreach (var item in _definition.Items)
        {
            before[item.Id] = TotalOf(item.Id);
        }

        // Standing draw first and unconditionally: sinks, then every executor whatever it is
        // doing. Only the production charge that follows can be refused.
        foreach (var sink in _definition.Sinks)
        {
            _draw += sink.PowerDraw;
        }

        foreach (var executor in _executors)
        {
            _draw += executor.Definition.StandingPowerDraw;
            executor.PowerDraw = executor.Definition.StandingPowerDraw;
        }

        foreach (var hauler in _haulers)
        {
            _draw += hauler.Definition.StandingPowerDraw;
            hauler.PowerDraw = hauler.Definition.StandingPowerDraw;

            // Reset beside the draw, and for the same reason: both describe this tick alone, and
            // a line that moved nothing must report nothing rather than last tick's figure.
            hauler.MovedLastTick = 0;
        }

        // Transport runs before production, so material delivered this tick is available to the
        // facility that needs it this tick rather than next.
        foreach (var hauler in _haulers)
        {
            StepHauler(hauler);
        }

        foreach (var executor in _executors)
        {
            StepProducer(executor);
        }

        if (_starvedThisTick)
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

    private void StepProducer(Executor executor)
    {
        executor.BlockReason = null;

        if (executor.SwitchOverRemaining > 0)
        {
            AdvanceSwitchOver(executor);
            return;
        }

        // A finished run whose output would not fit holds the facility. Neither the work nor the
        // consumed inputs are lost; the run is deposited as soon as room appears.
        if (executor.Current is { RunAwaitingDeposit: true } holding)
        {
            if (TryDeposit(executor, holding))
            {
                executor.Status = ExecutorStatus.RunningTask;
            }

            return;
        }

        if (executor.Current is { RunActive: true } running)
        {
            AdvanceRun(executor, running);
            return;
        }

        SelectAndStart(executor);
    }

    private void SelectAndStart(Executor executor)
    {
        // 1. Continue the current task when its next run can start. Preferring the work already
        //    configured is what keeps a facility producing instead of reconfiguring.
        if (executor.Current is { } current && !current.IsFinished && CanStart(executor, current, out _))
        {
            StartRun(executor, current);
            return;
        }

        // 2. Any queued task using the configuration already loaded.
        if (executor.Configured is { } configured)
        {
            foreach (var task in executor.Queue)
            {
                if (!task.IsFinished && task.SchematicId == configured && CanStart(executor, task, out _))
                {
                    executor.Current = task;
                    StartRun(executor, task);
                    return;
                }
            }
        }

        // 3. A runnable task on a different schematic, which costs a reconfiguration. A facility
        //    that has never been configured has nothing to tear down and pays nothing.
        foreach (var task in executor.Queue)
        {
            if (task.IsFinished || !CanStart(executor, task, out _))
            {
                continue;
            }

            executor.Current = task;

            if (executor.Configured is null || executor.Definition.SwitchOverTicks <= 0)
            {
                executor.Configured = task.SchematicId;
                StartRun(executor, task);
                return;
            }

            executor.SwitchOverRemaining = executor.Definition.SwitchOverTicks;
            executor.SwitchTarget = task;
            Emit(EventCategory.Production, EventCode.SwitchOverStarted, executor.Definition.Id.Value,
                new Dictionary<string, long>
                {
                    ["task"] = task.Id.Value,
                    ["ticks"] = executor.Definition.SwitchOverTicks,
                });

            // The tick that decides to reconfigure is the first tick of the reconfiguration, not
            // a free one spent deciding. Otherwise a switch-over always costs its ticks plus one.
            AdvanceSwitchOver(executor);
            return;
        }

        // 4. Nothing can run. Every unfinished task records why, which is what turns "the vessel
        //    stopped" into "the vessel stopped because these three things are missing".
        var pending = 0;
        foreach (var task in executor.Queue)
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
            Emit(EventCategory.Production, EventCode.AllTasksBlocked, executor.Definition.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    private void StepHauler(Hauler hauler)
    {
        hauler.BlockReason = null;

        // Continue the transfer already in hand before looking at anything else, for the same
        // reason a facility prefers its loaded configuration: finishing beats starting.
        if (hauler.Current is { } current && !current.IsFinished && TryMove(hauler, current))
        {
            return;
        }

        foreach (var task in hauler.Queue)
        {
            if (!task.IsFinished && TryMove(hauler, task))
            {
                return;
            }
        }

        var pending = 0;
        foreach (var task in hauler.Queue)
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
            Emit(EventCategory.Logistics, EventCode.AllTasksBlocked, hauler.Definition.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        hauler.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    private bool CanMove(Hauler hauler, TransportTask task, out long quantity, out PostponeReason reason)
    {
        // A standing order is bounded by what is at the source and what fits at the destination,
        // and by nothing else.
        var outstanding = task.RequestedQuantity is { } requested
            ? requested - task.MovedQuantity
            : long.MaxValue;
        var atSource = Available(task.Source, task.Item);
        var room = Room(task.Destination, task.Item);

        quantity = Math.Min(
            Math.Min(hauler.Definition.ThroughputPerTick, outstanding),
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

    private bool TryMove(Hauler hauler, TransportTask task)
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
        hauler.Current = task;
        hauler.Status = ExecutorStatus.RunningTask;

        if (task.RecordAttempt(_tick, TaskAttemptOutcome.Started, null))
        {
            var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
            if (task.RequestedQuantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TransferStarted, hauler.Definition.Id.Value, data);
        }

        if (task.RequestedQuantity is { } target && task.MovedQuantity >= target)
        {
            task.State = TaskState.Complete;
            task.RecordAttempt(_tick, TaskAttemptOutcome.Completed, null);
            hauler.Current = null;
            Emit(EventCategory.Logistics, EventCode.TransferCompleted, hauler.Definition.Id.Value,
                new Dictionary<string, long>
                {
                    ["task"] = task.Id.Value,
                    ["moved"] = task.MovedQuantity,
                });
        }

        return true;
    }

    private void Postpone(Hauler hauler, TransportTask task, PostponeReason reason)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = _tick;
        hauler.BlockReason = reason;

        if (task.RecordAttempt(_tick, TaskAttemptOutcome.Postponed, reason))
        {
            Emit(EventCategory.Logistics, CodeFor(reason), hauler.Definition.Id.Value, SimEvent.NoData);
        }
    }

    private void AdvanceSwitchOver(Executor executor)
    {
        executor.SwitchOverRemaining--;
        executor.Status = ExecutorStatus.SwitchingOver;

        if (executor.SwitchOverRemaining > 0)
        {
            return;
        }

        var target = executor.SwitchTarget!;
        executor.Configured = target.SchematicId;
        executor.SwitchTarget = null;
        Emit(EventCategory.Production, EventCode.SwitchOverCompleted, executor.Definition.Id.Value,
            new Dictionary<string, long> { ["task"] = target.Id.Value });
    }

    private bool CanStart(Executor executor, ProductionTask task, out PostponeReason reason)
    {
        var schematic = _definition.Schematics.Get(task.SchematicId);
        var storage = executor.Definition.LocalStorage;

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

    private void StartRun(Executor executor, ProductionTask task)
    {
        var schematic = _definition.Schematics.Get(task.SchematicId);
        var storage = executor.Definition.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            Withdraw(storage, input.Item, input.Quantity);
        }

        executor.Current = task;
        task.RunActive = true;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.RecordAttempt(_tick, TaskAttemptOutcome.Started, null);

        var started = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["run"] = task.CompletedRuns + 1,
        };
        if (task.RequestedRuns is { } requestedRuns)
        {
            started["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunStarted, executor.Definition.Id.Value, started);

        AdvanceRun(executor, task);
    }

    private void AdvanceRun(Executor executor, ProductionTask task)
    {
        var schematic = _definition.Schematics.Get(task.SchematicId);
        var effort = schematic.EffortPerRun.Value;
        var work = Math.Min(executor.Definition.WorkRatePerTick, effort - task.WorkDoneThisRun);

        // Charged cumulatively rather than as a per-tick slice: the final tick's work equals the
        // full effort, so the target lands exactly on the schematic's energy and the rounding
        // remainder settles itself with no special case.
        var targetTotal = schematic.EnergyPerRun.Value * (task.WorkDoneThisRun + work) / effort;
        var charge = targetTotal - task.EnergyChargedThisRun;

        if (_draw + charge > _definition.EnergyCapacity)
        {
            _starvedThisTick = true;
            Postpone(executor, task, PostponeReason.InsufficientEnergy, new Dictionary<string, long>
            {
                ["required"] = charge,
                ["reserve"] = _definition.EnergyCapacity - _draw,
            });
            executor.Status = ExecutorStatus.AllQueuedTasksBlocked;
            return;
        }

        _draw += charge;
        executor.PowerDraw += charge;
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

    private bool TryDeposit(Executor executor, ProductionTask task)
    {
        var schematic = _definition.Schematics.Get(task.SchematicId);
        var storage = executor.Definition.LocalStorage;

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
        task.RecordAttempt(_tick, TaskAttemptOutcome.RunCompleted, null);

        var done = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["done"] = task.CompletedRuns,
        };
        if (task.RequestedRuns is { } requestedRuns)
        {
            done["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunCompleted, executor.Definition.Id.Value, done);

        if (task.RequestedRuns is { } target && task.CompletedRuns >= target)
        {
            task.State = TaskState.Complete;
            task.RecordAttempt(_tick, TaskAttemptOutcome.Completed, null);
            executor.Current = null;
            Emit(EventCategory.Production, EventCode.TaskCompleted, executor.Definition.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
        }

        return true;
    }

    private void Postpone(Executor executor, ProductionTask task, PostponeReason reason) =>
        Postpone(executor, task, reason, SimEvent.NoData);

    private void Postpone(
        Executor executor,
        ProductionTask task,
        PostponeReason reason,
        IReadOnlyDictionary<string, long> data)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = _tick;
        executor.BlockReason = reason;

        // Edge-triggered. A task blocked on the same thing for a thousand ticks made one
        // decision, not a thousand, and emitting it every tick would bury everything else in the
        // console within seconds.
        if (task.RecordAttempt(_tick, TaskAttemptOutcome.Postponed, reason))
        {
            Emit(CategoryFor(reason), CodeFor(reason), executor.Definition.Id.Value, data);
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
            var totalAmount = 0L;
            var totalCapacity = 0L;
            foreach (var item in _definition.Items)
            {
                var stock = new ItemStock(
                    item.Id, Available(storage.Id, item.Id), CapacityOf(storage, item));
                contents.Add(stock);
                totalAmount += stock.Amount;
                totalCapacity += stock.Capacity;
            }

            storages.Add(new StorageState(
                storage.Id, storage.Label, totalAmount, totalCapacity, contents));
        }

        var executors = new List<ExecutorState>(_executors.Count);
        foreach (var executor in _executors)
        {
            executors.Add(new ExecutorState(
                executor.Definition.Id,
                executor.Definition.Label,
                executor.Definition.Type,
                executor.Definition.LocalStorage,
                executor.Status,
                executor.Configured,
                executor.Current?.Id,
                executor.PowerDraw,
                RunTicksRemaining(executor),
                RunTicksTotal(executor),
                executor.SwitchOverRemaining,
                executor.BlockReason));
        }

        var transports = new List<TransportExecutorState>(_haulers.Count);
        foreach (var hauler in _haulers)
        {
            transports.Add(new TransportExecutorState(
                hauler.Definition.Id,
                hauler.Definition.Label,
                hauler.Definition.From,
                hauler.Definition.To,
                hauler.Status,
                hauler.Current?.Id,
                hauler.Current?.Item,
                hauler.Definition.ThroughputPerTick,
                hauler.MovedLastTick,
                hauler.PowerDraw,
                hauler.BlockReason));
        }

        var sinks = new List<PowerSinkState>(_definition.Sinks.Count);
        foreach (var sink in _definition.Sinks)
        {
            sinks.Add(new PowerSinkState(sink.Id.Value, sink.Label, sink.PowerDraw));
        }

        var tasks = new List<ProductionTaskState>(_tasks.Count);
        foreach (var task in _tasks)
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

        var transfers = new List<TransportTaskState>(_transfers.Count);
        foreach (var task in _transfers)
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
            _tick,
            resources,
            storages,
            new EnergyState(
                _definition.EnergyCapacity,
                _draw,
                _definition.EnergyCapacity - _draw,
                _capHits,
                _starvedTicks),
            executors,
            transports,
            sinks,
            tasks,
            transfers,
            _events.ToList(),
            _totalEventsEmitted);
    }

    /// <summary>
    /// Ticks left on the run in progress, derived from the work still to do rather than stored
    /// separately. One source of truth: a stored countdown and an accumulated work total would
    /// drift apart the first time a run was postponed.
    /// </summary>
    private long RunTicksRemaining(Executor executor)
    {
        if (executor.Current is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = _definition.Schematics.Get(task.SchematicId).EffortPerRun.Value;
        var left = effort - task.WorkDoneThisRun;
        if (left <= 0)
        {
            return 0;
        }

        var rate = executor.Definition.WorkRatePerTick;
        return (left + rate - 1) / rate;
    }

    /// <summary>
    /// Ticks a whole run costs, derived the same way and for the same reason. Neither the
    /// schematic's effort nor the facility's work rate can change while a run is in progress, so
    /// this is fixed from the moment the run starts without needing a field to hold it — and a
    /// postponement, which does no work, cannot move it.
    /// </summary>
    private long RunTicksTotal(Executor executor)
    {
        if (executor.Current is not { RunActive: true } task)
        {
            return 0;
        }

        var effort = _definition.Schematics.Get(task.SchematicId).EffortPerRun.Value;
        var rate = executor.Definition.WorkRatePerTick;

        return (effort + rate - 1) / rate;
    }

    /// <summary>A transport line's runtime state. Mutable, and owned entirely by the engine.</summary>
    private sealed class Hauler(TransportExecutorDefinition definition)
    {
        public TransportExecutorDefinition Definition { get; } = definition;

        public List<TransportTask> Queue { get; } = new();

        public TransportTask? Current { get; set; }

        public ExecutorStatus Status { get; set; } = ExecutorStatus.NoTasksQueued;

        public long PowerDraw { get; set; }

        /// <summary>
        /// How much this line actually moved during the tick just finished. The graph's edge
        /// colour is computed from it; without it a view can only tell running from not.
        /// </summary>
        public long MovedLastTick { get; set; }

        public PostponeReason? BlockReason { get; set; }
    }

    /// <summary>An executor's runtime state. Mutable, and owned entirely by the engine.</summary>
    private sealed class Executor(ProductionExecutorDefinition definition)
    {
        public ProductionExecutorDefinition Definition { get; } = definition;

        public List<ProductionTask> Queue { get; } = new();

        /// <summary>The schematic the facility is set up for. Retained while idle.</summary>
        public SchematicId? Configured { get; set; }

        public ProductionTask? Current { get; set; }

        public ProductionTask? SwitchTarget { get; set; }

        public long SwitchOverRemaining { get; set; }

        public ExecutorStatus Status { get; set; } = ExecutorStatus.NoTasksQueued;

        public long PowerDraw { get; set; }

        public PostponeReason? BlockReason { get; set; }
    }
}
