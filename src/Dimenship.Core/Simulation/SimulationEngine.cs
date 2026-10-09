using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Programs;
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

    /// <summary>
    /// How much more of an item a storage could accept, which is its whole free volume expressed
    /// in that item's units. Two items asked in turn are each told about the same free volume;
    /// only one of them can take it.
    /// <para>
    /// The item's own ceiling is not a second check, because it cannot be passed: an amount above
    /// <see cref="CapacityOf"/> would occupy more than the entire storage. One rule, so there is
    /// no pair of rules to disagree.
    /// </para>
    /// </summary>
    public long Room(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance) || !_items.TryGetValue(item, out var known))
        {
            return 0;
        }

        return RoomIn(instance, known, OccupiedVolume(instance));
    }

    /// <summary>
    /// How much of an item may be <i>delivered</i> into a storage, which is its free volume less
    /// the room its own facilities need for the output of the run they are set up for.
    /// <para>
    /// Transport asks this and production does not, because production is what the reservation is
    /// being held for. Without it a standing feed fills a buffer to the brim and the facility it
    /// feeds can never place its output: the shipped vessel deadlocked four operational hours in,
    /// permanently, because Matter Mix is less bulky than the Basic Metals it separates into, so
    /// consuming a run's inputs freed less volume than the run's output needed.
    /// </para>
    /// </summary>
    public long RoomForDelivery(StorageId storage, ItemId item)
    {
        if (!_storagesById.TryGetValue(storage, out var instance) || !_items.TryGetValue(item, out var known))
        {
            return 0;
        }

        return RoomIn(instance, known, OccupiedVolume(instance) + ReservedVolume(instance));
    }

    /// <summary>
    /// The volume a storage is holding back for the facilities that work out of it: one run's
    /// output each, at the schematic each is set up for.
    /// <para>
    /// A facility that has never been configured falls back to the schematic of the first task in
    /// its queue, so a fresh campaign reserves from the first tick rather than from whenever the
    /// first run happens to start. A facility with neither reserves nothing, which is right: it
    /// has no output to place.
    /// </para>
    /// <para>
    /// A whole run's output rather than the amount by which it exceeds the run's inputs. The
    /// stronger number is what makes the deadlock impossible instead of merely unlikely: a buffer
    /// filled to the reservation still has room for the output <i>before</i> its inputs are
    /// consumed, so no ordering of deliveries and runs can trap it.
    /// </para>
    /// </summary>
    private long ReservedVolume(StorageInstance storage)
    {
        if (!_facilitiesByStorage.TryGetValue(storage.Id, out var facilities))
        {
            return 0;
        }

        var reserved = 0L;

        foreach (var facility in facilities)
        {
            if (!facility.Built)
            {
                continue;
            }

            if (OutputOf(facility) is not { } output || !_items.TryGetValue(output.Item, out var known))
            {
                continue;
            }

            reserved += VolumeOf(storage, known, output.Quantity);
        }

        return reserved;
    }

    /// <summary>What one run of a facility's loaded schematic would produce, if it has one.</summary>
    private ItemAmount? OutputOf(FacilityInstance facility)
    {
        if (facility.Configured is { } configured)
        {
            return Catalog.Schematics.Get(configured).Output;
        }

        foreach (var task in Queued(facility))
        {
            if (!task.IsFinished)
            {
                return Catalog.Schematics.Get(task.Produce.Schematic).Output;
            }
        }

        return null;
    }

    /// <summary>
    /// How full a storage is, <c>1000</c> being full. The one reading the shell shows and the one
    /// the engine enforces room against, so a bar on a card cannot claim a hold has room the
    /// transport line feeding it disagrees about.
    /// </summary>
    public long FillPermille(StorageId storage) =>
        _storagesById.TryGetValue(storage, out var instance)
            ? OccupiedVolume(instance) * StorageArchetype.FullHold / StorageArchetype.FullVolume
            : 0;

    /// <summary>
    /// How much of its one shared volume a storage is using, in
    /// <see cref="StorageArchetype.FullVolume"/>ths. Summed over what the storage actually holds
    /// rather than over the catalog: an item it has never held contributes nothing, and the stock
    /// list is in first-deposit order, which is stable.
    /// </summary>
    private long OccupiedVolume(StorageInstance storage)
    {
        var occupied = 0L;

        foreach (var stored in storage.Stock)
        {
            if (stored.Amount > 0 && _items.TryGetValue(stored.Item, out var known))
            {
                occupied += VolumeOf(storage, known, stored.Amount);
            }
        }

        return occupied;
    }

    /// <summary>
    /// The volume an amount of one item occupies in one storage. Floored, and
    /// <see cref="RoomIn"/> floors in the same direction, so rounding costs the vessel room rather
    /// than inventing it — which is what keeps a storage from ever coming out over full.
    /// </summary>
    private long VolumeOf(StorageInstance storage, ItemDefinition item, long amount)
    {
        var capacity = CapacityOf(storage, item);
        return capacity <= 0 ? 0 : amount * StorageArchetype.FullVolume / capacity;
    }

    /// <summary>
    /// A free volume, in units of one item. An item the storage has no capacity for gets no room
    /// at all rather than a division: a storage that cannot hold a thing has nowhere to put it.
    /// </summary>
    private long RoomIn(StorageInstance storage, ItemDefinition item, long occupied)
    {
        var capacity = CapacityOf(storage, item);
        if (capacity <= 0)
        {
            return 0;
        }

        var free = StorageArchetype.FullVolume - occupied;
        return free <= 0 ? 0 : free * capacity / StorageArchetype.FullVolume;
    }

    /// <summary>
    /// The one door through which anything outside the kernel changes the world, other than
    /// <see cref="Advance"/> (C0). Dispatches to the command's method and reports what became of it.
    /// <para>
    /// An <see cref="ArgumentException"/> is the kernel saying the command makes no sense against
    /// this world, and becomes a <see cref="CommandRefused"/>: a controller acting on a stale world,
    /// or a player cancelling a plan that finished a tick ago, is ordinary and must not fault the
    /// game. That is honest only because every command checks before it changes anything, so a
    /// refused command left the world as it found it. Any other exception is a broken invariant,
    /// not a refusal, and propagates as it does from <see cref="Advance"/>.
    /// </para>
    /// </summary>
    public CommandResult Execute(Command command)
    {
        try
        {
            return Dispatch(command);
        }
        catch (ArgumentException refusal)
        {
            return new CommandRefused(command, refusal.Message, Array.Empty<DraftIssue>());
        }
    }

    private CommandResult Dispatch(Command command)
    {
        CommandAccepted Done(PlanId? plan = null, IReadOnlyList<TaskId>? tasks = null) =>
            new(command, plan, tasks ?? Array.Empty<TaskId>());

        switch (command)
        {
            case OrderGoal order:
            {
                var draft = PlanDraftEditor.Create(order.Goal, this, order.Destination, order.AssemblyTarget);
                var approval = PlanDraftEditor.Approve(draft, this);
                if (approval is PlanApprovalRefused refused)
                {
                    return new CommandRefused(command, "The planner refused the order; its issues say why.", refused.Issues);
                }

                var ordered = Commit(((PlanApprovalCommitted)approval).Plan);
                return Done(State.Plans.Plans[^1].Id, ordered);
            }

            case CommitPlan commit:
                var created = Commit(commit.Plan);
                return Done(State.Plans.Plans[^1].Id, created);

            case QueueTask queue:
                return Done(null, new[] { Enqueue(queue.Script, queue.Executor) });

            case SetPlanPriority set:
                SetPriority(set.Plan, set.Priority);
                return Done(set.Plan);

            case SetTaskPriority set:
                SetPriority(set.Task, set.Priority);
                return Done(null, new[] { set.Task });

            case HoldPlan hold:
                Hold(hold.Plan);
                return Done(hold.Plan);

            case ReleasePlan release:
                Release(release.Plan);
                return Done(release.Plan);

            case CancelPlan cancel:
                Cancel(cancel.Plan);
                return Done(cancel.Plan);

            case AmendPlan amend:
            {
                var before = State.Plans.Plans.FirstOrDefault(p => p.Id == amend.Plan)?.SpawnedTasks.Count ?? 0;
                if (Amend(amend.Plan, amend.Quantity) is PlanApprovalRefused refused)
                {
                    return new CommandRefused(
                        command, "The planner refused the amended goal; its issues say why.", refused.Issues);
                }

                var appended = State.Plans.Plans.First(p => p.Id == amend.Plan).SpawnedTasks.Skip(before).ToList();
                return Done(amend.Plan, appended);
            }

            case HoldTask hold:
                Hold(hold.Task);
                return Done(null, new[] { hold.Task });

            case ReleaseTask release:
                Release(release.Task);
                return Done(null, new[] { release.Task });

            case CancelTask cancel:
                Cancel(cancel.Task);
                return Done(null, new[] { cancel.Task });

            case RelinquishStock relinquish:
                Relinquish(relinquish.Plan, relinquish.Storage, relinquish.Item, relinquish.Quantity);
                return Done(relinquish.Plan);

            case ReassignStock reassign:
                Reassign(reassign.From, reassign.To, reassign.Storage, reassign.Item, reassign.Quantity);
                return Done(reassign.To);

            default:
                throw new ArgumentException($"Unknown command '{command.GetType().Name}'.", nameof(command));
        }
    }

    /// <summary>
    /// Injects a task script into a compatible executor's queue. The executor decides when it
    /// runs; queue position is a starting point for that decision, not a schedule.
    /// <para>
    /// Validation dispatches on the action kind and keeps every check the two old entry points
    /// had. Conditions are accepted here and evaluated at selection — empty conditions mean
    /// attempt every tick, which is what keeps planner and scenario tasks byte-identical to today.
    /// </para>
    /// </summary>
    public TaskId Enqueue(TaskScript script, ExecutorId executor)
    {
        RequireQueueable(script, executor);
        return Queue(script, executor);
    }

    /// <summary>
    /// Every check <see cref="Enqueue"/> makes, and nothing it changes. Split from the queueing so
    /// <see cref="Commit"/> can check a whole plan before queueing any of it: a refused command must
    /// leave the world as it found it (C0, Decision 2), and a plan whose third task was invalid
    /// used to leave the first two queued with no plan.
    /// </summary>
    private void RequireQueueable(TaskScript script, ExecutorId executor)
    {
        RefuseUnboundOperands(script.Conditions);

        switch (script.Action)
        {
            case Produce produce:
                RequireProducible(script, produce, executor);
                break;
            case Transfer transfer:
                RequireHaulable(script, transfer, executor);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown task action '{script.Action.GetType().Name}'.", nameof(script));
        }
    }

    /// <summary>Queues a task <see cref="RequireQueueable"/> has already accepted.</summary>
    private TaskId Queue(TaskScript script, ExecutorId executor)
    {
        var task = new TaskInstance
        {
            Id = State.Tasks.Mint(),
            Script = script,
            ExecutorId = executor,
            EnqueuedAtTick = State.Clock.Tick,
        };

        State.Tasks.Add(task);

        // Event data is a plain long map, so a standing order omits the count rather than carrying
        // a sentinel that every reader would have to know about.
        var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
        if (script.Action is Produce produce)
        {
            _facilitiesById[executor].Queue.Add(task.Id);
            if (produce.Runs is { } requested)
            {
                data["runs"] = requested;
            }

            Emit(EventCategory.Production, EventCode.TaskQueued, executor.Value, data);
        }
        else
        {
            var transfer = (Transfer)script.Action;
            _linesById[executor].Queue.Add(task.Id);
            if (transfer.Quantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TaskQueued, executor.Value, data);
        }

        return task.Id;
    }

    private void RequireProducible(TaskScript script, Produce produce, ExecutorId executor)
    {
        if (produce.Runs is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(script), produce.Runs, "A task must request at least one run.");
        }

        if (!_facilitiesById.TryGetValue(executor, out var target))
        {
            throw new ArgumentException(WrongKindOrMissing(executor, wantedFacility: true), nameof(executor));
        }

        if (!target.Built)
        {
            throw new ArgumentException(
                $"Executor '{executor}' is unbuilt and cannot be queued on.", nameof(executor));
        }

        var archetype = Archetype(target);
        if (!archetype.Commandable)
        {
            // Same sentence the content loader uses for a scenario task on a passive source —
            // one wording, two seams, so a picker that somehow offers one fails the same way.
            throw new ArgumentException(NotCommandable(executor, archetype), nameof(executor));
        }

        var definition = Catalog.Schematics.Get(produce.Schematic);
        if (!IsUnlocked(produce.Schematic))
        {
            throw new ArgumentException(
                $"Schematic '{produce.Schematic}' is not unlocked.", nameof(script));
        }

        RequireCompatible(definition, target);
    }

    private static string NotCommandable(ExecutorId executor, FacilityArchetype archetype) =>
        $"'{executor}' is a {archetype.Id}, which is not commandable. A passive " +
        "facility runs what it is configured with and is scheduled by nobody.";

    private void RequireHaulable(TaskScript script, Transfer transfer, ExecutorId executor)
    {
        if (transfer.Quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(script), transfer.Quantity, "A transfer must move at least one unit.");
        }

        if (!_linesById.TryGetValue(executor, out var line))
        {
            throw new ArgumentException(WrongKindOrMissing(executor, wantedFacility: false), nameof(executor));
        }

        if (!line.Built)
        {
            throw new ArgumentException(
                $"Transport '{executor}' is unbuilt and cannot be queued on.", nameof(executor));
        }

        RequireKnownItem(transfer.Item);

        if (!_storagesById.ContainsKey(transfer.From))
        {
            throw new ArgumentException($"No storage '{transfer.From}'.", nameof(script));
        }

        if (!_storagesById.ContainsKey(transfer.To))
        {
            throw new ArgumentException($"No storage '{transfer.To}'.", nameof(script));
        }

        if (transfer.From == transfer.To)
        {
            throw new ArgumentException(
                $"A transfer from '{transfer.From}' to itself would move nothing.", nameof(script));
        }

        // A line runs a fixed route. Queueing a transfer it could never make would leave a task
        // sitting in a queue that no line aboard can serve, which reads as a stalled vessel rather
        // than as the planning mistake it is.
        if (line.From != transfer.From || line.To != transfer.To)
        {
            throw new ArgumentException(
                $"Transport '{executor}' runs '{line.From}' to '{line.To}', " +
                $"not '{transfer.From}' to '{transfer.To}'.",
                nameof(executor));
        }
    }

    /// <summary>
    /// Injects a plan's proposals into executor queues, turning them into runtime tasks. Until
    /// this is called a plan is a description and nothing more.
    /// <para>
    /// A plan carrying unplannable entries still commits: the available portion begins
    /// immediately, and each entry is reported so the player can decide what to do about the
    /// rest. Tasks are enqueued in <see cref="ProductionPlan.Tasks"/> order — the determinism
    /// contract — which is transfers-then-runs per branch, exactly as the two separate lists were
    /// enqueued before Stage 5 merged them.
    /// </para>
    /// </summary>
    public IReadOnlyList<TaskId> Commit(ProductionPlan plan)
    {
        foreach (var task in plan.Tasks)
        {
            RequireQueueable(task.Script, task.Executor);
        }

        var created = new List<TaskId>(plan.Tasks.Count);
        foreach (var task in plan.Tasks)
        {
            created.Add(Queue(task.Script, task.Executor));
        }

        // The goal is the only level at which progress is legible: tasks are per-executor by
        // design, so nothing in the task list can answer "how far along is four robot frames"
        // without the plan that grouped them.
        State.Plans.Record(new CommittedPlan
        {
            Id = State.Plans.Mint(),
            Goal = plan.Goal,
            Destination = plan.Destination,
            CommittedAtTick = State.Clock.Tick,
            SpawnedTasks = created.ToList(),
        });

        // Whatever is free when a plan commits goes to its claims: every earlier plan was already
        // fully held, or had nothing outstanding there (D3, Decision 5's invariant).
        foreach (var (storage, item) in ClaimMath.Withdrawals(Catalog, State, State.Plans.Plans[^1]))
        {
            AllocateFree(storage, item);
        }

        Emit(EventCategory.Planning, EventCode.PlanCommitted, plan.Goal.Item.Value,
            new Dictionary<string, long>
            {
                ["goal"] = plan.Goal.Quantity,
                ["tasks"] = plan.Tasks.Count,
                ["unplannable"] = plan.Unplannable.Count,
            });

        foreach (var entry in plan.Unplannable)
        {
            Emit(EventCategory.Planning, EventCode.PlanUnplannable, entry.Item.Value,
                new Dictionary<string, long>
                {
                    ["quantity"] = entry.Quantity,
                    ["reason"] = (long)entry.Reason,
                });
        }

        Snapshot = BuildSnapshot();
        return created;
    }

    /// <summary>
    /// Sets one task's priority. A command, not the passage of time: it takes effect at the task's
    /// executor's next boundary (D1, Decision 3), never mid-run, and never moves cargo already on
    /// a belt. A retired task still in the registry's window accepts the value and is unaffected.
    /// </summary>
    public void SetPriority(TaskId task, Priority priority)
    {
        var instance = State.Tasks.Task(task)
            ?? throw new ArgumentException($"No task '{task}'.", nameof(task));

        if (State.Plans.Owning(task) is { } plan)
        {
            throw new ArgumentException(
                $"Task '{task}' belongs to plan '{plan.Id}', whose priority it reads. Set the " +
                "plan's priority instead; a task inside a plan carries none of its own.",
                nameof(task));
        }

        instance.Priority = priority;
        Emit(EventCategory.Planning, EventCode.PriorityChanged, instance.ExecutorId.Value,
            new Dictionary<string, long> { ["task"] = task.Value, ["priority"] = (long)priority });
        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Sets a plan's priority, which every task it spawned reads live, at every stage of its
    /// chain: promoting only the final assembly is what the design calls inadequate (D3,
    /// Decision 2). Priority changes the future and never moves anything already committed.
    /// </summary>
    public void SetPriority(PlanId plan, Priority priority)
    {
        var committed = State.Plans.Plans.FirstOrDefault(p => p.Id == plan)
            ?? throw new ArgumentException($"No plan '{plan}'.", nameof(plan));

        committed.Priority = priority;

        Emit(EventCategory.Planning, EventCode.PriorityChanged, committed.Goal.Item.Value,
            new Dictionary<string, long> { ["plan"] = plan.Value, ["priority"] = (long)priority });
        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Refuses a script whose conditions name a parameter or an unknown target. A parameter has
    /// no binding outside a program; an unknown target would postpone forever for a reason nobody
    /// can see. Both are planning mistakes, not runtime stalls.
    /// </summary>
    private void RefuseUnboundOperands(IReadOnlyList<Condition> conditions)
    {
        foreach (var condition in conditions)
        {
            foreach (var operand in condition.Operands.Append(condition.Value))
            {
                switch (operand)
                {
                    case ParameterRef parameter:
                        throw new ArgumentException(
                            $"Condition parameter '{parameter.Name}' has no binding outside a program.",
                            nameof(conditions));
                    case TargetRef target when !TargetExists(target):
                        throw new ArgumentException(
                            $"Unknown {target.Kind.ToString().ToLowerInvariant()} '{target.Id}'.",
                            nameof(conditions));
                }
            }
        }
    }

    /// <summary>
    /// Why an executor could not take a task: it does not exist, or it exists and is the other kind.
    /// <para>
    /// Facilities and lines live in separate indexes, so each entry point misses the other's
    /// executors as if they were not aboard. "No executor 'factory_a_feed'" sends the author looking
    /// for a missing line when what they have is a produce task addressed at one — the action and
    /// the executor disagree, and only the message can say so.
    /// </para>
    /// </summary>
    private string WrongKindOrMissing(ExecutorId executor, bool wantedFacility)
    {
        var otherIndexHasIt = wantedFacility
            ? _linesById.ContainsKey(executor)
            : _facilitiesById.ContainsKey(executor);

        if (!otherIndexHasIt)
        {
            return wantedFacility
                ? $"No executor '{executor}'."
                : $"No transport executor '{executor}'.";
        }

        return wantedFacility
            ? $"Executor '{executor}' is a transport line; a produce task needs a facility."
            : $"Executor '{executor}' is a facility; a transfer needs a transport line.";
    }

    private bool TargetExists(TargetRef target) =>
        target.Kind switch
        {
            TargetKind.Storage => _storagesById.ContainsKey(new StorageId(target.Id)),
            TargetKind.Item => _items.ContainsKey(new ItemId(target.Id)),
            TargetKind.Schematic => Catalog.Schematics.TryGet(new SchematicId(target.Id), out _),
            TargetKind.Executor =>
                _facilitiesById.ContainsKey(new ExecutorId(target.Id))
                || _linesById.ContainsKey(new ExecutorId(target.Id)),
            _ => false,
        };

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
                if (!executor.Built)
                {
                    continue;
                }

                // A passive source runs what it is configured with and is scheduled by nobody —
                // the same rule the content loader enforces on authored tasks. Leaving one in this
                // list lets the planner quietly pick the Emergency Hydrogen Extractor.
                var archetype = Archetype(executor);
                if (!archetype.Commandable)
                {
                    continue;
                }

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
                    if (task.Produce.Runs is { } requested)
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
                    archetype.Type,
                    executor.LocalStorage,
                    queued,
                    occupied,
                    WorkRate(executor),
                    archetype.Commandable));
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
                if (!hauler.Built)
                {
                    continue;
                }

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
                    queued,
                    Throughput(hauler),
                    hauler.LengthTicks));
            }

            return lines;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Free stock: what is in the hold less what is held there for committed plans (K6b). Stock
    /// held for another plan is stock this plan cannot take, so counting it would plan a haul that
    /// waits on <see cref="PostponeReason.MaterialClaimed"/> for good, which is the race the claims
    /// exist to end.
    /// </remarks>
    public long InHold(ItemId item) => Free(State.Vessel.Hold, item);

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
        PostponeReason.ConditionNotMet => EventCode.PostponeConditionNotMet,
        PostponeReason.Outranked => EventCode.PostponeOutranked,
        PostponeReason.MaterialClaimed => EventCode.PostponeMaterialClaimed,
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

        foreach (var item in Catalog.Items)
        {
            _lastDelta[item.Id] = TotalOf(item.Id) - before[item.Id];
        }
    }

    /// <summary>
    /// One whole construction unit in milli-units. Commissioning withdraws exactly this; more in
    /// local storage is left for a later slot or a failed haul, never consumed as change.
    /// </summary>
    private const long WholeConstructionUnit = 1000;

    /// <summary>
    /// After transport, before production: unbuilt facilities whose local storage holds a whole
    /// construction unit become Built. Local storage stands in for an upgrade socket until sockets
    /// exist — nothing here builds a line.
    /// </summary>
    private void CommissionFacilities()
    {
        foreach (var facility in State.Vessel.Facilities)
        {
            if (facility.Built)
            {
                continue;
            }

            if (Archetype(facility).ConstructionUnit is not { } unit)
            {
                continue;
            }

            if (Free(facility.LocalStorage, unit) < WholeConstructionUnit)
            {
                continue;
            }

            Withdraw(facility.LocalStorage, unit, WholeConstructionUnit);
            facility.Built = true;
            Emit(EventCategory.Production, EventCode.FacilityBuilt, facility.Id.Value, SimEvent.NoData);
        }
    }

    private void StepProducer(FacilityInstance executor)
    {
        executor.BlockReason = null;

        if (executor.SwitchOverRemaining > 0)
        {
            if (!TryAbandonSwitchOver(executor))
            {
                AdvanceSwitchOver(executor);
            }

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
        // Priority ranks first (D1, Decision 2): only the highest priority among tasks that can
        // start is considered, and within that tier today's three steps keep their order exactly.
        // An unready task never enters a tier, so an urgent order still waiting for its ore does
        // not hold the machine. With every task at the default, the tier is everything ready and
        // the steps choose what they always chose.
        if (TopReadyPriority(executor) is { } tier)
        {
            Select(executor, tier);
            RecordPassedOver(executor, tier);
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
            ReadyToStart(executor, task, out var reason);
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

    /// <summary>
    /// Today's three selection steps, run within one priority tier. The caller has established
    /// that some task in the tier can start, so one of the steps always takes it.
    /// </summary>
    private void Select(FacilityInstance executor, Priority tier)
    {
        // 1. Continue the current task when its next run can start. Preferring the work already
        //    configured is what keeps a facility producing instead of reconfiguring.
        if (CurrentJob(executor) is { } current && !current.IsFinished && PriorityOf(current) == tier
            && ReadyToStart(executor, current, out _))
        {
            StartRun(executor, current);
            return;
        }

        // 2. Any queued task using the configuration already loaded.
        if (executor.Configured is { } configured)
        {
            foreach (var task in Queued(executor))
            {
                if (!task.IsFinished && PriorityOf(task) == tier && task.Produce.Schematic == configured
                    && ReadyToStart(executor, task, out _))
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
            if (task.IsFinished || PriorityOf(task) != tier || !ReadyToStart(executor, task, out _))
            {
                continue;
            }

            executor.Current = task.Id;

            if (executor.Configured is null || SwitchOverTicks(executor) <= 0)
            {
                executor.Configured = task.Produce.Schematic;
                StartRun(executor, task);
                return;
            }

            BeginSwitchOver(executor, task);
            return;
        }
    }

    private void BeginSwitchOver(FacilityInstance executor, TaskInstance task)
    {
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
    }

    /// <summary>
    /// The highest priority among this facility's tasks that could start now, if any, optionally
    /// only among priorities strictly above <paramref name="above"/>.
    /// </summary>
    private Priority? TopReadyPriority(FacilityInstance executor, Priority? above = null)
    {
        Priority? top = null;
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || (above is { } floor && PriorityOf(task) <= floor)
                || (top is { } best && PriorityOf(task) <= best))
            {
                continue;
            }

            if (ReadyToStart(executor, task, out _))
            {
                top = PriorityOf(task);
            }
        }

        return top;
    }

    /// <summary>
    /// Says why the tasks a selection did not take are waiting, where priority is the reason. A
    /// ready task below the chosen tier was <see cref="PostponeReason.Outranked"/>. A task above it
    /// could not start, and records its own physical reason, so an urgent order that is not running
    /// says what it lacks. Tasks in the chosen tier are left alone: that is the behaviour from
    /// before priority, and keeping it is what makes default priority neutral.
    /// </summary>
    private void RecordPassedOver(FacilityInstance executor, Priority tier)
    {
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || PriorityOf(task) == tier || executor.Current == task.Id)
            {
                continue;
            }

            if (PriorityOf(task) > tier)
            {
                ReadyToStart(executor, task, out var reason);
                PostponeTask(executor.Id, task, reason, CategoryFor(reason));
            }
            else if (ReadyToStart(executor, task, out _))
            {
                PostponeTask(executor.Id, task, PostponeReason.Outranked, EventCategory.Production);
            }
        }
    }

    /// <summary>
    /// D1, Decision 4: a switch-over commits time and no material, so a ready task of strictly
    /// higher priority than its target may redirect it. The new task's schematic decides the cost.
    /// The target's schematic retargets, and the countdown runs on. The setup still loaded
    /// cancels, and the run starts this tick. Anything else restarts the full countdown. Within the
    /// winning tier a task on the loaded setup is preferred, then queue order, as in selection.
    /// Returns false, having changed nothing, when nothing outranks the target.
    /// </summary>
    private bool TryAbandonSwitchOver(FacilityInstance executor)
    {
        if (executor.SwitchTarget is not { } targetId || State.Tasks.Task(targetId) is not { } target)
        {
            return false;
        }

        if (TopReadyPriority(executor, above: PriorityOf(target)) is not { } tier)
        {
            return false;
        }

        TaskInstance? chosen = null;
        foreach (var task in Queued(executor))
        {
            if (task.IsFinished || PriorityOf(task) != tier || !ReadyToStart(executor, task, out _))
            {
                continue;
            }

            if (task.Produce.Schematic == executor.Configured)
            {
                chosen = task;
                break;
            }

            chosen ??= task;
        }

        var replacement = chosen!;
        executor.Current = replacement.Id;

        if (replacement.Produce.Schematic == target.Produce.Schematic)
        {
            executor.SwitchTarget = replacement.Id;
            AdvanceSwitchOver(executor);

            // A cancelled target waited for its switch-over; it no longer has one.
            FinishIfCutShort(target);
            RecordPassedOver(executor, tier);
            return true;
        }

        Emit(EventCategory.Production, EventCode.SwitchOverAbandoned, executor.Id.Value,
            new Dictionary<string, long>
            {
                ["task"] = target.Id.Value,
                ["for"] = replacement.Id.Value,
                ["remaining"] = executor.SwitchOverRemaining,
            });

        if (replacement.Produce.Schematic == executor.Configured)
        {
            // Configured never changes while a switch-over runs, which is what makes cancelling
            // honest: the machine is still set up for this, and only the elapsed ticks are lost.
            executor.SwitchOverRemaining = 0;
            executor.SwitchTarget = null;
            StartRun(executor, replacement);
        }
        else
        {
            BeginSwitchOver(executor, replacement);
        }

        FinishIfCutShort(target);
        RecordPassedOver(executor, tier);
        return true;
    }

    /// <summary>
    /// Conditions and physical readiness together. Both are evaluated so
    /// <see cref="PostponeReasons.RootCause"/> can pick — ConditionNotMet is last, so missing
    /// inputs beat a false gate. An early return on conditions alone would hide the ore.
    /// </summary>
    private bool ReadyToStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var reasons = new List<PostponeReason>();
        if (IsHeld(task))
        {
            reasons.Add(PostponeReason.SafetyLock);
        }

        if (!ConditionEvaluator.AllMet(task.Script.Conditions, State))
        {
            reasons.Add(PostponeReason.ConditionNotMet);
        }

        if (!CanStart(executor, task, out var physical))
        {
            reasons.Add(physical);
        }

        if (reasons.Count == 0)
        {
            reason = default;
            return true;
        }

        reason = PostponeReasons.RootCause(reasons)!.Value;
        return false;
    }

    /// <summary>
    /// One tick of one line: unload the head, advance the belt, load the tail.
    /// <para>
    /// The order is the determinism contract. Unloading first is what lets cargo reaching the
    /// destination this tick be there for the production phase that follows; advancing before
    /// loading is what makes a slot loaded now sit <c>LengthTicks</c> away from being deliverable
    /// rather than one short of it.
    /// </para>
    /// </summary>
    private void StepHauler(TransportInstance hauler)
    {
        hauler.BlockReason = null;

        if (!Unload(hauler))
        {
            // The head could not be emptied, so nothing behind it moves either: a belt is rigid,
            // not an accumulator. The line keeps exactly the fill it had and takes nothing on,
            // however little that fill is. The other direction of a two-way link is a different
            // line with a different belt and is untouched by this.
            return;
        }

        Advance(hauler);
        Load(hauler);
    }

    /// <summary>
    /// Empties the head slot into the destination, as far as it fits, and reports whether the belt
    /// may move. Anything left on the head freezes the line for this tick.
    /// <para>
    /// Conditions are deliberately not consulted here. The spec's carve-out — a run already in
    /// flight is never re-gated — now has the transfer analogue it once lacked, because cargo on a
    /// belt <i>is</i> in flight: a condition that turns false stops the next pickup and never
    /// strands what is already travelling.
    /// </para>
    /// </summary>
    private bool Unload(TransportInstance hauler)
    {
        if (hauler.Belt[0] is not { } head)
        {
            return true;
        }

        // RoomForDelivery, not Room: the reservation a facility's next run needs is held back from
        // transport at the far end of the belt exactly as it was when transport arrived instantly.
        var quantity = Math.Min(head.Quantity, RoomForDelivery(hauler.To, head.Item));
        if (quantity > 0)
        {
            Deposit(hauler.To, head.Item, quantity);
            head.Quantity -= quantity;
            hauler.DeliveredLastTick += quantity;
            Credit(hauler, head.Task, quantity);
            AllocateArrival(head.Task, hauler.To, head.Item, quantity);
        }

        if (head.Quantity > 0)
        {
            Freeze(hauler);
            return false;
        }

        hauler.Belt[0] = null;
        return true;
    }

    /// <summary>
    /// Credits a delivery to the transfer that loaded it, and finishes that transfer when the last
    /// of it has arrived. The task is found by id rather than taken from the line's current task,
    /// because by the time cargo lands the line has usually moved on to the next haul.
    /// </summary>
    private void Credit(TransportInstance hauler, TaskId id, long quantity)
    {
        if (State.Tasks.Task(id) is not { } task)
        {
            return;
        }

        task.MovedQuantity += quantity;

        if (task.Transfer.Quantity is not { } target || task.MovedQuantity < target)
        {
            return;
        }

        task.State = TaskState.Complete;
        task.CompletedAtTick = State.Clock.Tick;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);

        if (hauler.Current == task.Id)
        {
            hauler.Current = null;
        }

        Emit(EventCategory.Logistics, EventCode.TransferCompleted, hauler.Id.Value,
            new Dictionary<string, long>
            {
                ["task"] = task.Id.Value,
                ["moved"] = task.MovedQuantity,
            });
        Retire(hauler.Queue, task.Id);
    }

    /// <summary>
    /// Reports the whole line blocked on its destination. Every transfer still waiting to be
    /// picked up is postponed for the same reason, because under a frozen belt that is the truth:
    /// none of them can be loaded, whatever their own source looks like.
    /// </summary>
    private void Freeze(TransportInstance hauler)
    {
        // The one place a line reports itself blocked, and the only place its BlockReason is set.
        // A belt can be frozen with nothing left in the queue to postpone, and the line is blocked
        // all the same: the cargo stuck on the head is the fault, not the queue behind it.
        hauler.BlockReason = PostponeReason.DestinationFull;

        var pending = 0;
        foreach (var task in Queued(hauler))
        {
            if (task.IsFinished || FullyLoaded(task))
            {
                continue;
            }

            pending++;
            Postpone(hauler, task, PostponeReason.DestinationFull);
        }

        if (pending > 0 && hauler.Status != ExecutorStatus.AllQueuedTasksBlocked)
        {
            Emit(EventCategory.Logistics, EventCode.AllTasksBlocked, hauler.Id.Value,
                new Dictionary<string, long> { ["queued"] = pending });
        }

        hauler.Status = ExecutorStatus.AllQueuedTasksBlocked;
    }

    /// <summary>
    /// Moves the belt one slot toward the destination. The head is empty by the time this runs, so
    /// nothing is advanced over, and the tail is freed for this tick's intake.
    /// </summary>
    private static void Advance(TransportInstance hauler)
    {
        hauler.Belt.RemoveAt(0);
        hauler.Belt.Add(null);
    }

    /// <summary>
    /// Picks up for at most one transfer, into the tail slot. Selection is unchanged from when a
    /// line moved material outright: the transfer in hand first, then queue order.
    /// </summary>
    private void Load(TransportInstance hauler)
    {
        // Priority picks which transfer the free tail slot takes (D1, Decision 3, carried over to
        // lines): only the highest priority among transfers that could load now is considered,
        // and within it the transfer in hand, then queue order. Cargo already aboard is never
        // touched, and a transfer gives up the line exactly when it is entirely on the belt.
        if (TopReadyPriority(hauler) is { } tier)
        {
            // Continue the transfer already in hand before looking at anything else, for the same
            // reason a facility prefers its loaded configuration: finishing beats starting.
            var loaded = CurrentTransfer(hauler) is { } current && !current.IsFinished
                && PriorityOf(current) == tier && TryLoad(hauler, current);

            if (!loaded)
            {
                foreach (var task in Queued(hauler))
                {
                    if (!task.IsFinished && PriorityOf(task) == tier && TryLoad(hauler, task))
                    {
                        loaded = true;
                        break;
                    }
                }
            }

            if (loaded)
            {
                RecordPassedOver(hauler, tier);
                return;
            }
        }

        var pending = 0;
        foreach (var task in Queued(hauler))
        {
            // A transfer entirely on the belt is neither finished nor blocked — it is travelling.
            // Postponing it would report a stall that is not happening.
            if (task.IsFinished || FullyLoaded(task))
            {
                continue;
            }

            pending++;
            ReadyToLoad(hauler, task, out _, out var reason);
            Postpone(hauler, task, reason);
        }

        // Whether there was anything to pick up or not, a line still holding cargo is working: it
        // is carrying what it has toward a destination that is taking it. Only an empty belt is a
        // line doing nothing.
        if (hauler.CargoQuantity > 0)
        {
            hauler.Status = ExecutorStatus.RunningTask;
            return;
        }

        hauler.Current = null;

        // Queued work that could not be picked up is not this line being blocked. Blocked means
        // cargo aboard that the destination will not take; this belt is empty, so the line is
        // stopping nothing and has nothing stuck on it. Reporting a fault here would light up
        // every line downstream of an empty storage — a shortage the storage itself already
        // reports, and one the line has no part in.
        hauler.Status = pending > 0
            ? ExecutorStatus.NothingToCarry
            : ExecutorStatus.NoTasksQueued;
    }
    /// <summary>The highest priority among this line's transfers that could load now, if any.</summary>
    private Priority? TopReadyPriority(TransportInstance hauler)
    {
        Priority? top = null;
        foreach (var task in Queued(hauler))
        {
            if (task.IsFinished || FullyLoaded(task) || (top is { } best && PriorityOf(task) <= best))
            {
                continue;
            }

            if (ReadyToLoad(hauler, task, out _, out _))
            {
                top = PriorityOf(task);
            }
        }

        return top;
    }

    /// <inheritdoc cref="RecordPassedOver(FacilityInstance, Priority)"/>
    private void RecordPassedOver(TransportInstance hauler, Priority tier)
    {
        foreach (var task in Queued(hauler))
        {
            if (task.IsFinished || FullyLoaded(task) || PriorityOf(task) == tier || hauler.Current == task.Id)
            {
                continue;
            }

            if (PriorityOf(task) > tier)
            {
                ReadyToLoad(hauler, task, out _, out var reason);
                Postpone(hauler, task, reason);
            }
            else if (ReadyToLoad(hauler, task, out _, out _))
            {
                Postpone(hauler, task, PostponeReason.Outranked);
            }
        }
    }

    /// <summary>True when every unit a transfer asked for is on the belt or past it.</summary>
    private static bool FullyLoaded(TaskInstance task) =>
        task.Transfer.Quantity is { } target && task.LoadedQuantity >= target;

    /// <summary>
    /// Conditions and physical readiness together, the transport counterpart of
    /// <see cref="ReadyToStart"/>, and evaluated on every tick a haul picks up.
    /// <para>
    /// Gating pickup and not delivery is the transfer form of the spec's carve-out for a run in
    /// flight — see <see cref="Unload"/>. Gating only the first tick of a haul instead would leave
    /// every later tick of a long pickup unconditioned, which is the opposite of what a condition
    /// is for.
    /// </para>
    /// </summary>
    private bool ReadyToLoad(
        TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        var conditionsMet = ConditionEvaluator.AllMet(task.Script.Conditions, State);
        var canLoad = CanLoad(hauler, task, out quantity, out var physical);
        var held = IsHeld(task);
        if (conditionsMet && canLoad && !held)
        {
            reason = default;
            return true;
        }

        var reasons = new List<PostponeReason>();
        if (held)
        {
            reasons.Add(PostponeReason.SafetyLock);
        }

        if (!conditionsMet)
        {
            reasons.Add(PostponeReason.ConditionNotMet);
        }

        if (!canLoad)
        {
            reasons.Add(physical);
        }

        quantity = 0;
        reason = PostponeReasons.RootCause(reasons)!.Value;
        return false;
    }

    private bool CanLoad(TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        // A standing order is bounded by what is at the source, and by nothing else. The
        // destination does not appear here at all: room is asked for at the far end of the belt, a
        // whole LengthTicks later, and refusing to pick up now because the destination happens to
        // be full now would leave the belt empty exactly when it should be filling.
        var outstanding = task.Transfer.Quantity is { } requested
            ? requested - task.LoadedQuantity
            : long.MaxValue;
        var atSource = Spendable(task, task.Transfer.From, task.Transfer.Item);

        quantity = Math.Min(Math.Min(Throughput(hauler), outstanding), atSource);

        if (quantity > 0)
        {
            reason = PostponeReason.SafetyLock;
            return true;
        }

        reason = Available(task.Transfer.From, task.Transfer.Item) > 0
            ? PostponeReason.MaterialClaimed
            : PostponeReason.InsufficientSourceMaterial;
        return false;
    }

    private bool TryLoad(TransportInstance hauler, TaskInstance task)
    {
        if (!ReadyToLoad(hauler, task, out var quantity, out _))
        {
            return false;
        }

        Withdraw(task.Transfer.From, task.Transfer.Item, quantity);
        ConsumeHeld(task, task.Transfer.From, task.Transfer.Item, quantity);
        hauler.Belt[^1] = new BeltSlot
        {
            Task = task.Id,
            Item = task.Transfer.Item,
            Quantity = quantity,
        };

        task.LoadedQuantity += quantity;
        hauler.LoadedLastTick += quantity;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.FirstStartedAtTick ??= State.Clock.Tick;
        hauler.Current = task.Id;
        hauler.Status = ExecutorStatus.RunningTask;

        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null))
        {
            var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
            if (task.Transfer.Quantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TransferStarted, hauler.Id.Value, data);
        }

        // The line takes the next transfer on the moment this one is entirely aboard. Holding it
        // as current until delivery would park the belt for a whole LengthTicks between two hauls,
        // which is the cost having a belt exists to avoid.
        if (FullyLoaded(task))
        {
            hauler.Current = null;
        }

        return true;
    }

    /// <summary>
    /// Records why one transfer could not be picked up. It deliberately does not touch the line’s
    /// own <see cref="TransportInstance.BlockReason"/>: a postponed transfer is a fact about that
    /// transfer, and only <see cref="Freeze"/> — cargo aboard the destination will not take — is a
    /// fact about the line. Setting it here is what used to report an empty line as blocked.
    /// </summary>
    private void Postpone(TransportInstance hauler, TaskInstance task, PostponeReason reason) =>
        PostponeTask(hauler.Id, task, reason, EventCategory.Logistics);

    private void AdvanceSwitchOver(FacilityInstance executor)
    {
        executor.SwitchOverRemaining--;
        executor.Status = ExecutorStatus.SwitchingOver;

        if (executor.SwitchOverRemaining > 0)
        {
            return;
        }

        var target = State.Tasks.Task(executor.SwitchTarget!.Value)!;
        executor.Configured = target.Produce.Schematic;
        executor.SwitchTarget = null;
        Emit(EventCategory.Production, EventCode.SwitchOverCompleted, executor.Id.Value,
            new Dictionary<string, long> { ["task"] = target.Id.Value });

        // A target cancelled while the facility switched toward it finishes now, with the setup
        // loaded: the switch-over completed, as D1 requires, and there is nothing left to run.
        FinishIfCutShort(target);
    }

    private bool CanStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            if (Spendable(task, storage, input.Item) < input.Quantity)
            {
                reason = Available(storage, input.Item) >= input.Quantity
                    ? PostponeReason.MaterialClaimed
                    : PostponeReason.InsufficientInputMaterial;
                return false;
            }
        }

        // A facility whose buffer is not in the world has nowhere to put anything, which is a
        // content error rather than a shortage; it reports the blockage it actually has.
        if (!_storagesById.TryGetValue(storage, out var instance))
        {
            reason = PostponeReason.DestinationFull;
            return false;
        }

        // Room is checked before anything is consumed. A facility that cannot place its output
        // must not shred its input for nothing.
        //
        // It is measured against the volume the run's own inputs are about to free, not against
        // the volume they still occupy. A buffer full of the metal a run consumes has no room for
        // the components that metal becomes, so checking it before the withdrawal would deadlock
        // every facility whose feed line kept its buffer full. A run whose output is bulkier than
        // its inputs still needs the difference to be free, which this says and the previous
        // wording did not.
        if (RoomAfterConsuming(instance, schematic) < schematic.Output.Quantity)
        {
            reason = PostponeReason.DestinationFull;
            return false;
        }

        reason = PostponeReason.SafetyLock;
        return true;
    }

    private void StartRun(FacilityInstance executor, TaskInstance task)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            Withdraw(storage, input.Item, input.Quantity);
            ConsumeHeld(task, storage, input.Item, input.Quantity);
        }

        executor.Current = task.Id;
        task.RunActive = true;
        task.WorkDoneThisRun = 0;
        task.EnergyChargedThisRun = 0;
        task.State = TaskState.Running;
        task.LastReason = null;
        task.PostponedAtTick = null;
        task.FirstStartedAtTick ??= State.Clock.Tick;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Started, null);

        var started = new Dictionary<string, long>
        {
            ["task"] = task.Id.Value,
            ["run"] = task.CompletedRuns + 1,
        };
        if (task.Produce.Runs is { } requestedRuns)
        {
            started["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunStarted, executor.Id.Value, started);

        AdvanceRun(executor, task);
    }

    private void AdvanceRun(FacilityInstance executor, TaskInstance task)
    {
        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
        var charge = RunCharge(executor, task, out var work, out var targetTotal);

        // A run already in progress at the start of the tick was granted or refused power before
        // any facility stepped, by priority (GrantPowerToRunsInProgress). A run starting this tick
        // draws on what those grants left, in visit order, exactly as every run once did.
        long? reserve = null;
        if (_powerGrants.Remove(task.Id, out var grant))
        {
            if (grant.Granted)
            {
                _reservedPower -= charge;
            }
            else
            {
                reserve = grant.Reserve;
            }
        }
        else if (State.Vessel.Energy.DrawLastTick + _reservedPower + charge > State.Vessel.Energy.Capacity)
        {
            reserve = State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick - _reservedPower;
        }

        if (reserve is { } left)
        {
            _starvedThisTick = true;
            Postpone(executor, task, PostponeReason.InsufficientEnergy, new Dictionary<string, long>
            {
                ["required"] = charge,
                ["reserve"] = left,
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

    /// <summary>
    /// This tick's energy charge for a run, and the work it buys. Charged cumulatively rather than
    /// as a per-tick slice: the final tick's work equals the full effort, so the target lands
    /// exactly on the schematic's energy and the rounding remainder settles itself.
    /// </summary>
    private long RunCharge(FacilityInstance executor, TaskInstance task, out long work, out long targetTotal)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var effort = schematic.EffortPerRun.Value;
        work = Math.Min(WorkRate(executor), effort - task.WorkDoneThisRun);
        targetTotal = schematic.EnergyPerRun.Value * (task.WorkDoneThisRun + work) / effort;
        return targetTotal - task.EnergyChargedThisRun;
    }

    /// <summary>
    /// D3, Decision 4: when power is short, it goes by (effective priority descending, task id
    /// ascending) rather than by where a facility sits in the declaration. Every run already in
    /// progress at the start of the tick is granted or refused here, before any facility steps,
    /// skipping a charge that no longer fits as the visit-order loop always did. The facilities
    /// then step in visit order as before, and each applies its grant at its own turn.
    /// <para>
    /// A run that starts this tick is not known until its facility selects it, so it draws on what
    /// the grants left, in visit order. That is one tick of one run; from its second tick it ranks
    /// by priority like every other. D3's literal three-pass order (select every facility, then
    /// grant, then advance every facility) was rejected because it reorders the journal and lets
    /// one facility's deposit land after the next facility's selection. A tick with enough power
    /// would then no longer be identical to the code before this, which D3 itself requires it to
    /// be. Here, if every charge fits, every grant succeeds and each facility advances exactly
    /// where and when it did.
    /// </para>
    /// </summary>
    private void GrantPowerToRunsInProgress(List<FacilityInstance> producers)
    {
        _powerGrants.Clear();
        _reservedPower = 0;

        var inProgress = new List<(TaskInstance Task, long Charge)>();
        foreach (var executor in producers)
        {
            if (executor.SwitchOverRemaining == 0
                && CurrentJob(executor) is { RunActive: true, RunAwaitingDeposit: false } task)
            {
                inProgress.Add((task, RunCharge(executor, task, out _, out _)));
            }
        }

        inProgress.Sort((a, b) =>
        {
            var byPriority = PriorityOf(b.Task).CompareTo(PriorityOf(a.Task));
            return byPriority != 0 ? byPriority : a.Task.Id.Value.CompareTo(b.Task.Id.Value);
        });

        var available = State.Vessel.Energy.Capacity - State.Vessel.Energy.DrawLastTick;
        foreach (var (task, charge) in inProgress)
        {
            if (charge <= available)
            {
                available -= charge;
                _reservedPower += charge;
                _powerGrants[task.Id] = (true, 0);
            }
            else
            {
                _powerGrants[task.Id] = (false, available);
            }
        }
    }

    /// <summary>
    /// Stock at a storage that no plan holds. <see cref="Available"/> keeps meaning physically
    /// present, which is what fill, room and the snapshot read; this is what a task with no plan,
    /// commissioning and the planner may take.
    /// </summary>
    public long Free(StorageId storage, ItemId item) =>
        Math.Max(0, Available(storage, item) - State.Claims.HeldAt(storage, item));

    /// <summary>
    /// What one task may withdraw from a storage: its plan's holding there plus free stock, or free
    /// stock alone for a task with no plan. Never another plan's holding (D3, Decision 5).
    /// </summary>
    private long Spendable(TaskInstance task, StorageId storage, ItemId item)
    {
        var own = State.Plans.Owning(task.Id) is { } plan ? State.Claims.Held(plan.Id, storage, item) : 0;
        return own + Free(storage, item);
    }

    /// <summary>A withdrawal by a plan's task draws its plan's holding first.</summary>
    private void ConsumeHeld(TaskInstance task, StorageId storage, ItemId item, long quantity)
    {
        if (State.Plans.Owning(task.Id) is not { } plan)
        {
            return;
        }

        var held = State.Claims.Held(plan.Id, storage, item);
        if (held > 0)
        {
            State.Claims.Change(plan.Id, storage, item, -Math.Min(held, quantity));
        }
    }

    /// <summary>
    /// D3, Decision 5's two steps for stock that just arrived. A delivery or deposit made by a plan's
    /// task is held for that plan, up to what its own withdrawals there still need: cargo keeps its
    /// owner. Whatever is left is free, and is offered to outstanding claims.
    /// </summary>
    private void AllocateArrival(TaskId by, StorageId storage, ItemId item, long quantity)
    {
        if (State.Plans.Owning(by) is { State: PlanState.Active } owner)
        {
            var room = ClaimMath.Need(Catalog, State, owner, storage, item) - State.Claims.Held(owner.Id, storage, item);
            var take = Math.Min(Math.Min(quantity, room), Free(storage, item));
            if (take > 0)
            {
                State.Claims.Change(owner.Id, storage, item, take);
            }
        }

        AllocateFree(storage, item);
    }

    /// <summary>
    /// Offers free stock at one storage to the outstanding claims there, by priority descending,
    /// then plan id ascending, skipping held plans; each takes as much as it is still short. Run on
    /// every change that can leave free stock beside an outstanding claim, which is what keeps the
    /// invariant (<see cref="ClaimInvariantViolations"/>).
    /// </summary>
    private void AllocateFree(StorageId storage, ItemId item)
    {
        var free = Free(storage, item);
        if (free <= 0)
        {
            return;
        }

        var eligible = State.Plans.Plans
            .Where(p => p.State == PlanState.Active && !p.Held)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Id.Value)
            .ToList();

        foreach (var plan in eligible)
        {
            var outstanding = Outstanding(plan, storage, item);
            if (outstanding <= 0)
            {
                continue;
            }

            var take = Math.Min(outstanding, free);
            State.Claims.Change(plan.Id, storage, item, take);
            free -= take;
            Emit(EventCategory.Planning, EventCode.ClaimAllocated, $"{storage}/{item}",
                new Dictionary<string, long> { ["plan"] = plan.Id.Value, ["quantity"] = take });

            if (free <= 0)
            {
                return;
            }
        }
    }

    private long Outstanding(CommittedPlan plan, StorageId storage, ItemId item) =>
        Math.Max(0,
            ClaimMath.Need(Catalog, State, plan, storage, item)
            - State.Claims.Held(plan.Id, storage, item)
            - ClaimMath.Inbound(Catalog, State, plan, storage, item));

    /// <summary>
    /// D3, Decision 5's invariant: at every storage and item, either no stock is free, or no plan
    /// that is active and not held has an outstanding claim there. Also every holding is within
    /// its plan's need and the stock present. Empty when it holds. For tests and the harness; the
    /// engine maintains it and never reads this.
    /// </summary>
    public IReadOnlyList<string> ClaimInvariantViolations()
    {
        var violations = new List<string>();
        foreach (var storage in State.Vessel.Storages)
        {
            foreach (var item in Catalog.Items)
            {
                var free = Available(storage.Id, item.Id) - State.Claims.HeldAt(storage.Id, item.Id);
                if (free < 0)
                {
                    violations.Add($"{storage.Id}/{item.Id}: {-free} more held than present");
                    continue;
                }

                if (free == 0)
                {
                    continue;
                }

                foreach (var plan in State.Plans.Plans.Where(p => p.State == PlanState.Active && !p.Held))
                {
                    if (Outstanding(plan, storage.Id, item.Id) > 0)
                    {
                        violations.Add($"{storage.Id}/{item.Id}: {free} free beside plan {plan.Id}'s outstanding claim");
                    }
                }
            }
        }

        foreach (var (plan, storage, item, held) in State.Claims.Entries)
        {
            var owner = State.Plans.Plans.FirstOrDefault(p => p.Id == plan);
            var need = owner is null ? 0 : ClaimMath.Need(Catalog, State, owner, storage, item);
            if (held > need)
            {
                violations.Add($"plan {plan} holds {held} of {item} at {storage}, needing {need}");
            }
        }

        return violations;
    }

    /// <summary>
    /// Gives up some of a plan's holding, which goes back through the allocation order (D3,
    /// Decision 6) — possibly straight back to the same plan, if it still ranks first. That is the
    /// command meaning "let the current order decide again".
    /// </summary>
    public void Relinquish(PlanId plan, StorageId storage, ItemId item, long quantity)
    {
        var committed = ActivePlan(plan);
        var give = Math.Min(Math.Max(0, quantity), State.Claims.Held(committed.Id, storage, item));
        if (give > 0)
        {
            State.Claims.Change(committed.Id, storage, item, -give);
            AllocateFree(storage, item);
        }

        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Moves held stock from one plan to another, bounded by what the receiver is still short
    /// there: moving stock to a plan that does not need it would be a hoard no withdrawal backs.
    /// Any rest stays with the giver.
    /// </summary>
    public void Reassign(PlanId from, PlanId to, StorageId storage, ItemId item, long quantity)
    {
        var giver = ActivePlan(from);
        var receiver = ActivePlan(to);
        var move = Math.Min(
            Math.Min(Math.Max(0, quantity), State.Claims.Held(giver.Id, storage, item)),
            Outstanding(receiver, storage, item));

        if (move > 0)
        {
            State.Claims.Change(giver.Id, storage, item, -move);
            State.Claims.Change(receiver.Id, storage, item, move);
            Emit(EventCategory.Planning, EventCode.ClaimAllocated, $"{storage}/{item}",
                new Dictionary<string, long> { ["plan"] = receiver.Id.Value, ["quantity"] = move });
        }

        Snapshot = BuildSnapshot();
    }

    private CommittedPlan ActivePlan(PlanId plan) =>
        State.Plans.Plans.FirstOrDefault(p => p.Id == plan && p.State == PlanState.Active)
        ?? throw new ArgumentException($"No active plan '{plan}'.", nameof(plan));

    /// <summary>
    /// Holds a plan (D3, Decisions 6 and 7). Its tasks between runs, and its transfers not yet
    /// loaded, stop at their next boundary with <see cref="PostponeReason.SafetyLock"/>. A run in
    /// progress finishes and deposits, a switch-over toward it completes, and cargo aboard
    /// arrives, held for the plan. The plan keeps its claims and receives no new stock: releasing
    /// claims here would make a hold-then-release pair a quiet way to move stock between plans.
    /// </summary>
    public void Hold(PlanId plan)
    {
        var committed = ActivePlan(plan);
        if (!committed.Held)
        {
            committed.Held = true;
            Emit(EventCategory.Planning, EventCode.Held, committed.Goal.Item.Value,
                new Dictionary<string, long> { ["plan"] = plan.Value });
        }

        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Resumes a held plan. Its tasks are selectable at each executor's next boundary, and its
    /// outstanding claims rejoin allocation at once, because free stock may have arrived while it
    /// was skipped (the invariant of D3, Decision 5).
    /// </summary>
    public void Release(PlanId plan)
    {
        var committed = ActivePlan(plan);
        if (committed.Held)
        {
            committed.Held = false;
            foreach (var (storage, item) in ClaimMath.Withdrawals(Catalog, State, committed))
            {
                AllocateFree(storage, item);
            }

            Emit(EventCategory.Planning, EventCode.Released, committed.Goal.Item.Value,
                new Dictionary<string, long> { ["plan"] = plan.Value });
        }

        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Cancels a plan by truncation, not deletion (D3, Decision 7). Each task is cut back to the
    /// work it has physically started and finishes by the ordinary completion path; the plan
    /// becomes <see cref="PlanState.Abandoned"/> and its holdings go back to allocation. A run in
    /// progress still deposits and cargo aboard still arrives, as free stock. Nothing is destroyed
    /// and nothing is created: everything that existed is still in a buffer or on a belt.
    /// </summary>
    public void Cancel(PlanId plan)
    {
        var committed = ActivePlan(plan);

        // Abandoned first, so the last task finishing does not mark the plan complete.
        committed.State = PlanState.Abandoned;
        var cut = CutBack(committed.SpawnedTasks);
        FinishCutShort(cut);

        foreach (var (storage, item, _) in State.Claims.Release(committed.Id))
        {
            AllocateFree(storage, item);
        }

        Emit(EventCategory.Planning, EventCode.Cancelled, committed.Goal.Item.Value,
            new Dictionary<string, long> { ["plan"] = plan.Value });
        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// Replans a plan for a new goal quantity under the same id, priority and held flag (D3,
    /// Decision 7). Its work is cut back exactly as a cancel cuts it, the new goal is planned
    /// against the live world through the ordinary draft path, and the new tasks are appended.
    /// Last, its holdings are trimmed to the new need and the surplus goes back to allocation.
    /// Committed tasks are never edited in place: the editable-plans spec keeps them supply or
    /// demand, and amend keeps that rule rather than being an exception to it.
    /// <para>
    /// The new goal is planned as any new order is, against the main hold's free stock (CLAUDE.md,
    /// the hold-only rule), with one addition: what this plan already holds in the hold counts as
    /// its own supply, so an amend never orders production for stock it is about to keep. Work
    /// already finished, or still running, is not netted, for the reason the planner nets nothing
    /// outside the hold. A goal of 10 amended to 6 after 4 have been delivered elsewhere plans 6
    /// more.
    /// </para>
    /// <para>
    /// Returns the composer's answer. A refusal (the goal cannot be planned now, for instance a
    /// schematic was locked) changes nothing: the cut is undone before returning.
    /// </para>
    /// </summary>
    public PlanApproval Amend(PlanId plan, long quantity)
    {
        var committed = ActivePlan(plan);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "An amend asks for at least one unit; cancel the plan to ask for none.");
        }

        var scripts = committed.SpawnedTasks
            .Select(id => State.Tasks.Task(id))
            .OfType<TaskInstance>()
            .Select(t => (Task: t, t.Script))
            .ToList();
        var cut = CutBack(committed.SpawnedTasks);

        // Planned after the cut, so the facilities' queued load the planner balances on is the
        // load that will exist; the planner is pure, so undoing the cut undoes everything.
        var view = new AmendView(this, committed.Id);
        var goal = new ItemAmount(committed.Goal.Item, quantity);
        var approval = PlanDraftEditor.Approve(PlanDraftEditor.Create(goal, view, committed.Destination), view);
        if (approval is not PlanApprovalCommitted { Plan: var replanned })
        {
            foreach (var (task, script) in scripts)
            {
                task.Replace(script);
            }

            return approval;
        }

        try
        {
            foreach (var task in replanned.Tasks)
            {
                RequireQueueable(task.Script, task.Executor);
            }
        }
        catch (ArgumentException)
        {
            foreach (var (task, script) in scripts)
            {
                task.Replace(script);
            }

            throw;
        }

        var created = replanned.Tasks.Select(t => Queue(t.Script, t.Executor)).ToList();
        committed.Goal = goal;
        State.Plans.Append(committed, created);

        // After the append, so a plan whose old work is all cut short is not finished by it.
        FinishCutShort(cut);

        if (committed.State == PlanState.Active)
        {
            var touched = new SortedSet<(string Storage, string Item)>();
            foreach (var (owner, storage, item, held) in State.Claims.Entries.ToList())
            {
                if (owner != committed.Id)
                {
                    continue;
                }

                var surplus = held - ClaimMath.Need(Catalog, State, committed, storage, item);
                if (surplus > 0)
                {
                    State.Claims.Change(committed.Id, storage, item, -surplus);
                    touched.Add((storage.Value, item.Value));
                }
            }

            foreach (var (storage, item) in touched)
            {
                AllocateFree(new StorageId(storage), new ItemId(item));
            }

            foreach (var (storage, item) in ClaimMath.Withdrawals(Catalog, State, committed))
            {
                AllocateFree(storage, item);
            }
        }

        Emit(EventCategory.Planning, EventCode.PlanAmended, goal.Item.Value,
            new Dictionary<string, long>
            {
                ["plan"] = plan.Value,
                ["goal"] = quantity,
                ["tasks"] = created.Count,
            });

        foreach (var entry in replanned.Unplannable)
        {
            Emit(EventCategory.Planning, EventCode.PlanUnplannable, entry.Item.Value,
                new Dictionary<string, long>
                {
                    ["quantity"] = entry.Quantity,
                    ["reason"] = (long)entry.Reason,
                });
        }

        Snapshot = BuildSnapshot();
        return approval;
    }

    /// <summary>
    /// Holds a task queued by hand. A plan's task is refused and names the plan, for the reason
    /// <see cref="SetPriority(TaskId, Priority)"/> refuses one: holding one stage of a chain leaves
    /// the rest of it holding stock for work that cannot finish.
    /// </summary>
    public void Hold(TaskId task) => SetHeld(task, true);

    /// <summary>Releases a task queued by hand; a plan's task is refused, naming the plan.</summary>
    public void Release(TaskId task) => SetHeld(task, false);

    /// <summary>
    /// Cancels a task queued by hand by truncation, as <see cref="Cancel(PlanId)"/> cuts each task
    /// of a plan. A standing order is cut the same way, and so finishes once its run in flight
    /// does. A plan's task is refused, naming the plan.
    /// </summary>
    public void Cancel(TaskId task)
    {
        var instance = HandQueued(task);
        FinishCutShort(CutBack(new[] { instance.Id }));
        Emit(EventCategory.Planning, EventCode.Cancelled, instance.ExecutorId.Value,
            new Dictionary<string, long> { ["task"] = task.Value });
        Snapshot = BuildSnapshot();
    }

    private void SetHeld(TaskId task, bool held)
    {
        var instance = HandQueued(task);
        if (instance.Held != held)
        {
            instance.Held = held;
            Emit(EventCategory.Planning, held ? EventCode.Held : EventCode.Released, instance.ExecutorId.Value,
                new Dictionary<string, long> { ["task"] = task.Value });
        }

        Snapshot = BuildSnapshot();
    }

    private TaskInstance HandQueued(TaskId task)
    {
        var instance = State.Tasks.Task(task)
            ?? throw new ArgumentException($"No task '{task}'.", nameof(task));

        // Holding or cancelling a passive source's standing order would be scheduling it, which
        // the GDD forbids however the command is phrased.
        if (_facilitiesById.TryGetValue(instance.ExecutorId, out var facility)
            && Archetype(facility) is { Commandable: false } archetype)
        {
            throw new ArgumentException(NotCommandable(instance.ExecutorId, archetype), nameof(task));
        }

        if (State.Plans.Owning(task) is { } plan)
        {
            throw new ArgumentException(
                $"Task '{task}' belongs to plan '{plan.Id}'. Hold, release and cancel apply to the " +
                "whole plan; a task inside a plan is never commanded alone.",
                nameof(task));
        }

        return instance;
    }

    /// <summary>
    /// Cuts each unfinished task back to the work it has physically started: a production task to
    /// its completed runs plus the one in progress or awaiting deposit, a transfer to what it has
    /// loaded. Returns the tasks it cut. A standing order becomes finite here, which is how it
    /// ever finishes.
    /// </summary>
    private List<TaskInstance> CutBack(IEnumerable<TaskId> tasks)
    {
        var cut = new List<TaskInstance>();
        foreach (var id in tasks)
        {
            if (State.Tasks.Task(id) is not { IsFinished: false } task)
            {
                continue;
            }

            TaskAction action = task.Script.Action switch
            {
                Produce produce => produce with
                {
                    Runs = task.CompletedRuns + (task.RunActive || task.RunAwaitingDeposit ? 1 : 0),
                },
                Transfer transfer => transfer with { Quantity = task.LoadedQuantity },
                var other => other,
            };

            task.Replace(task.Script with { Action = action });

            // A line keeps the transfer in hand until it is entirely aboard; a cut can make it so.
            if (task.IsTransfer && _linesById.TryGetValue(task.ExecutorId, out var line)
                && line.Current == task.Id && FullyLoaded(task))
            {
                line.Current = null;
            }

            cut.Add(task);
        }

        return cut;
    }

    /// <summary>
    /// True for a task a cut has left with nothing running and nothing to come: no run in progress
    /// or awaiting deposit, no cargo aboard. Such a task has not finished on its own, because no
    /// deposit or delivery will ever come to finish it.
    /// </summary>
    private static bool CutShort(TaskInstance task) => !task.IsFinished && (task.IsProduce
        ? task.Produce.Runs is { } runs && task.CompletedRuns >= runs && !task.RunActive && !task.RunAwaitingDeposit
        : task.Transfer.Quantity is { } quantity && task.MovedQuantity >= quantity);

    /// <summary>
    /// Finishes the tasks a cut left with nothing to do, by the ordinary completion path. A task a
    /// facility is switching over toward is left to <see cref="AdvanceSwitchOver"/>: D1, Decision
    /// 4 lets a switch-over be abandoned only for a strictly higher priority, never because its
    /// target stopped being wanted, and the countdown needs its target to finish on.
    /// </summary>
    private void FinishCutShort(IEnumerable<TaskInstance> tasks)
    {
        foreach (var task in tasks)
        {
            if (_facilitiesById.TryGetValue(task.ExecutorId, out var facility) && facility.SwitchTarget == task.Id)
            {
                continue;
            }

            FinishIfCutShort(task);
        }
    }

    private void FinishIfCutShort(TaskInstance task)
    {
        if (!CutShort(task))
        {
            return;
        }

        task.State = TaskState.Complete;
        task.CompletedAtTick = State.Clock.Tick;
        task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);

        if (_facilitiesById.TryGetValue(task.ExecutorId, out var facility))
        {
            if (facility.Current == task.Id)
            {
                facility.Current = null;
            }

            Emit(EventCategory.Production, EventCode.TaskCompleted, facility.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
            Retire(facility.Queue, task.Id);
        }
        else if (_linesById.TryGetValue(task.ExecutorId, out var line))
        {
            if (line.Current == task.Id)
            {
                line.Current = null;
            }

            Emit(EventCategory.Logistics, EventCode.TransferCompleted, line.Id.Value,
                new Dictionary<string, long>
                {
                    ["task"] = task.Id.Value,
                    ["moved"] = task.MovedQuantity,
                });
            Retire(line.Queue, task.Id);
        }
    }

    /// <summary>
    /// The live world as an amend plans against it: the engine's view, except that the stock the
    /// amended plan holds in the main hold counts as supply. That stock is the plan's own, and a
    /// replan that ignored it would order production for material it is about to keep.
    /// </summary>
    private sealed class AmendView(SimulationEngine engine, PlanId plan) : IWorldView
    {
        private IWorldView Engine => engine;

        public SchematicCatalog Schematics => Engine.Schematics;

        public StorageId Hold => Engine.Hold;

        public IReadOnlyList<PlannerFacility> Facilities => Engine.Facilities;

        public IReadOnlyList<PlannerTransport> TransportLines => Engine.TransportLines;

        public long InHold(ItemId item) => Engine.InHold(item) + engine.State.Claims.Held(plan, Engine.Hold, item);

        public bool IsUnlocked(SchematicId schematic) => Engine.IsUnlocked(schematic);
    }

    /// <summary>
    /// A task's effective priority: its plan's when it has one, read live, and its own only when
    /// it was queued by hand (D3, Decision 1).
    /// </summary>
    private Priority PriorityOf(TaskInstance task) => State.Plans.Owning(task.Id)?.Priority ?? task.Priority;

    /// <summary>Whether a task's unstarted work is held: its plan's flag, or its own when it has none.</summary>
    private bool IsHeld(TaskInstance task) => State.Plans.Owning(task.Id)?.Held ?? task.Held;

    private bool TryDeposit(FacilityInstance executor, TaskInstance task)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        // The plain room, not the deliverable room: the reservation a transport line is kept out
        // of is being held for exactly this deposit.
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
        if (task.Produce.Runs is { } requestedRuns)
        {
            done["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunCompleted, executor.Id.Value, done);

        if (task.Produce.Runs is { } target && task.CompletedRuns >= target)
        {
            task.State = TaskState.Complete;
            task.CompletedAtTick = State.Clock.Tick;
            task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);
            executor.Current = null;
            Emit(EventCategory.Production, EventCode.TaskCompleted, executor.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
            Retire(executor.Queue, task.Id);
        }

        AllocateArrival(task.Id, storage, schematic.Output.Item, schematic.Output.Quantity);
        return true;
    }

    private void Postpone(FacilityInstance executor, TaskInstance task, PostponeReason reason) =>
        Postpone(executor, task, reason, SimEvent.NoData);

    private void Postpone(
        FacilityInstance executor,
        TaskInstance task,
        PostponeReason reason,
        IReadOnlyDictionary<string, long> data)
    {
        executor.BlockReason = reason;
        PostponeTask(executor.Id, task, reason, CategoryFor(reason), data);
    }

    /// <summary>
    /// Records a postponement on the task alone. The executor's <c>BlockReason</c> is not touched:
    /// a task passed over while its executor runs something else is a fact about that task, and
    /// the executor is not blocked.
    /// </summary>
    private void PostponeTask(
        ExecutorId executor,
        TaskInstance task,
        PostponeReason reason,
        EventCategory category,
        IReadOnlyDictionary<string, long>? data = null)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = State.Clock.Tick;

        // Edge-triggered. A task blocked on the same thing for a thousand ticks made one
        // decision, not a thousand, and emitting it every tick would bury everything else in the
        // console within seconds.
        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Postponed, reason))
        {
            Emit(category, CodeFor(reason), executor.Value, data ?? SimEvent.NoData);
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
                Reading(executor.Utilization)));
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
                IsHeld(task)));
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
            State.Claims.Entries.Select(c => new ClaimState(c.Plan, c.Storage, c.Item, c.Held)).ToList());
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

        var effort = Catalog.Schematics.Get(task.Produce.Schematic).EffortPerRun.Value;
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

    /// <summary>
    /// How much a line can hold: one tick of intake per slot, and one slot per tick of length.
    /// Derived rather than authored, so a capacity can never drift from the throughput and the
    /// distance it is made of.
    /// </summary>
    private long Capacity(TransportInstance line) => Throughput(line) * line.LengthTicks;

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
    private IEnumerable<TaskInstance> Queued(FacilityInstance facility)
    {
        foreach (var id in facility.Queue)
        {
            if (State.Tasks.Task(id) is { } task)
            {
                yield return task;
            }
        }
    }

    /// <inheritdoc cref="Queued(FacilityInstance)"/>
    private IEnumerable<TaskInstance> Queued(TransportInstance line)
    {
        foreach (var id in line.Queue)
        {
            if (State.Tasks.Task(id) is { } task)
            {
                yield return task;
            }
        }
    }

    private TaskInstance? CurrentJob(FacilityInstance facility) =>
        facility.Current is { } id ? State.Tasks.Task(id) : null;

    private TaskInstance? CurrentTransfer(TransportInstance line) =>
        line.Current is { } id ? State.Tasks.Task(id) : null;

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

    private long CapacityOf(StorageInstance storage, ItemDefinition item) =>
        item.HoldCapacity * Archetype(storage).CapacityPermille / StorageArchetype.FullHold;

    /// <summary>
    /// Room for a schematic's output in a facility's own buffer, measured as the run itself would
    /// leave it: inputs withdrawn first, output deposited second. The inputs come off the
    /// occupancy rather than out of the storage, so a run that turns out not to fit has changed
    /// nothing.
    /// </summary>
    private long RoomAfterConsuming(StorageInstance storage, SchematicDefinition schematic)
    {
        var occupied = OccupiedVolume(storage);

        foreach (var input in schematic.Inputs)
        {
            if (_items.TryGetValue(input.Item, out var known))
            {
                occupied -= VolumeOf(storage, known, input.Quantity);
            }
        }

        return _items.TryGetValue(schematic.Output.Item, out var output)
            ? RoomIn(storage, output, occupied)
            : 0;
    }
}
