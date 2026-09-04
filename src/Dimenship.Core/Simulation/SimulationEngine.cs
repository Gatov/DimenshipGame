using Dimenship.Core.Content;
using Dimenship.Core.Planning;
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
        RefuseUnboundOperands(script.Conditions);

        return script.Action switch
        {
            Produce produce => EnqueueProduce(script, produce, executor),
            Transfer transfer => EnqueueHaul(script, transfer, executor),
            _ => throw new ArgumentException(
                $"Unknown task action '{script.Action.GetType().Name}'.", nameof(script)),
        };
    }

    private TaskId EnqueueProduce(TaskScript script, Produce produce, ExecutorId executor)
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

        var definition = Catalog.Schematics.Get(produce.Schematic);
        if (!IsUnlocked(produce.Schematic))
        {
            throw new ArgumentException(
                $"Schematic '{produce.Schematic}' is not unlocked.", nameof(script));
        }

        RequireCompatible(definition, target);

        var task = new TaskInstance
        {
            Id = State.Tasks.Mint(),
            Script = script,
            ExecutorId = executor,
        };

        State.Tasks.Add(task);
        target.Queue.Add(task.Id);

        // Event data is a plain long map, so a standing order omits the count rather than carrying
        // a sentinel that every reader would have to know about.
        var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
        if (produce.Runs is { } requested)
        {
            data["runs"] = requested;
        }

        Emit(EventCategory.Production, EventCode.TaskQueued, executor.Value, data);

        return task.Id;
    }

    private TaskId EnqueueHaul(TaskScript script, Transfer transfer, ExecutorId executor)
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

        var task = new TaskInstance
        {
            Id = State.Tasks.Mint(),
            Script = script,
            ExecutorId = executor,
        };

        State.Tasks.Add(task);
        line.Queue.Add(task.Id);

        var data = new Dictionary<string, long> { ["task"] = task.Id.Value };
        if (transfer.Quantity is { } requested)
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
    /// A plan carrying unplannable entries still commits: the available portion begins
    /// immediately, and each entry is reported so the player can decide what to do about the
    /// rest. Tasks are enqueued in <see cref="ProductionPlan.Tasks"/> order — the determinism
    /// contract — which is transfers-then-runs per branch, exactly as the two separate lists were
    /// enqueued before Stage 5 merged them.
    /// </para>
    /// </summary>
    public IReadOnlyList<TaskId> Commit(ProductionPlan plan)
    {
        var created = new List<TaskId>(plan.Tasks.Count);

        foreach (var task in plan.Tasks)
        {
            created.Add(Enqueue(task.Script, task.Executor));
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
                    Archetype(executor).Type,
                    executor.LocalStorage,
                    queued,
                    occupied,
                    WorkRate(executor)));
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
                    Throughput(hauler)));
            }

            return lines;
        }
    }

    /// <inheritdoc />
    public long Uncommitted(ItemId item)
    {
        var total = TotalOf(item);

        foreach (var task in State.Tasks.All.Where(t => t.IsProduce))
        {
            if (task.IsFinished)
            {
                continue;
            }

            var schematic = Catalog.Schematics.Get(task.Produce.Schematic);

            // A standing order is not a claim on a finite quantity: it consumes whatever arrives,
            // for as long as it arrives. Counting a future it has not committed to is what made
            // the default vessel's opening stock read as a deficit of eight billion.
            var remaining = task.Produce.Runs is { } requested
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
        PostponeReason.ConditionNotMet => EventCode.PostponeConditionNotMet,
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
                hauler.MovedLastTick = 0;
                continue;
            }

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

        foreach (var executor in producers)
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

            if (Available(facility.LocalStorage, unit) < WholeConstructionUnit)
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
        if (CurrentJob(executor) is { } current && !current.IsFinished && ReadyToStart(executor, current, out _))
        {
            StartRun(executor, current);
            return;
        }

        // 2. Any queued task using the configuration already loaded.
        if (executor.Configured is { } configured)
        {
            foreach (var task in Queued(executor))
            {
                if (!task.IsFinished && task.Produce.Schematic == configured && ReadyToStart(executor, task, out _))
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
            if (task.IsFinished || !ReadyToStart(executor, task, out _))
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
    /// Conditions and physical readiness together. Both are evaluated so
    /// <see cref="PostponeReasons.RootCause"/> can pick — ConditionNotMet is last, so missing
    /// inputs beat a false gate. An early return on conditions alone would hide the ore.
    /// </summary>
    private bool ReadyToStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var reasons = new List<PostponeReason>();
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
            ReadyToMove(hauler, task, out _, out var reason);
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

    /// <summary>
    /// Conditions and physical readiness together, the transport counterpart of
    /// <see cref="ReadyToStart"/>, and evaluated on every tick a haul moves.
    /// <para>
    /// The spec's carve-out — conditions never touch a run already in flight — has no transfer
    /// analogue, because a transfer is not in flight between ticks: <see cref="TryMove"/> withdraws
    /// and deposits within the same tick, so a partly-moved haul is a task that has started this
    /// many times, not cargo hanging in a tube. Gating only the first tick would leave every tick
    /// of a long haul after the first unconditioned, which is the opposite of what a condition is
    /// for. A producer's run <i>is</i> in flight across ticks, and
    /// that carve-out stays where it belongs: <see cref="StepProducer"/> returns before selection
    /// while a run is active.
    /// </para>
    /// </summary>
    private bool ReadyToMove(
        TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        var conditionsMet = ConditionEvaluator.AllMet(task.Script.Conditions, State);
        var canMove = CanMove(hauler, task, out quantity, out var physical);
        if (conditionsMet && canMove)
        {
            reason = default;
            return true;
        }

        var reasons = new List<PostponeReason>();
        if (!conditionsMet)
        {
            reasons.Add(PostponeReason.ConditionNotMet);
        }

        if (!canMove)
        {
            reasons.Add(physical);
        }

        quantity = 0;
        reason = PostponeReasons.RootCause(reasons)!.Value;
        return false;
    }

    private bool CanMove(TransportInstance hauler, TaskInstance task, out long quantity, out PostponeReason reason)
    {
        // A standing order is bounded by what is at the source and what fits at the destination,
        // and by nothing else.
        var outstanding = task.Transfer.Quantity is { } requested
            ? requested - task.MovedQuantity
            : long.MaxValue;
        var atSource = Available(task.Transfer.From, task.Transfer.Item);
        var room = RoomForDelivery(task.Transfer.To, task.Transfer.Item);

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

    private bool TryMove(TransportInstance hauler, TaskInstance task)
    {
        if (!ReadyToMove(hauler, task, out var quantity, out _))
        {
            return false;
        }

        Withdraw(task.Transfer.From, task.Transfer.Item, quantity);
        Deposit(task.Transfer.To, task.Transfer.Item, quantity);
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
            if (task.Transfer.Quantity is { } requested)
            {
                data["quantity"] = requested;
            }

            Emit(EventCategory.Logistics, EventCode.TransferStarted, hauler.Id.Value, data);
        }

        if (task.Transfer.Quantity is { } target && task.MovedQuantity >= target)
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

    private void Postpone(TransportInstance hauler, TaskInstance task, PostponeReason reason)
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

        var target = State.Tasks.Task(executor.SwitchTarget!.Value)!;
        executor.Configured = target.Produce.Schematic;
        executor.SwitchTarget = null;
        Emit(EventCategory.Production, EventCode.SwitchOverCompleted, executor.Id.Value,
            new Dictionary<string, long> { ["task"] = target.Id.Value });
    }

    private bool CanStart(FacilityInstance executor, TaskInstance task, out PostponeReason reason)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
        var storage = executor.LocalStorage;

        foreach (var input in schematic.Inputs)
        {
            if (Available(storage, input.Item) < input.Quantity)
            {
                reason = PostponeReason.InsufficientInputMaterial;
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
        if (task.Produce.Runs is { } requestedRuns)
        {
            started["of"] = requestedRuns;
        }

        Emit(EventCategory.Production, EventCode.RunStarted, executor.Id.Value, started);

        AdvanceRun(executor, task);
    }

    private void AdvanceRun(FacilityInstance executor, TaskInstance task)
    {
        var schematic = Catalog.Schematics.Get(task.Produce.Schematic);
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
            task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Completed, null);
            executor.Current = null;
            Emit(EventCategory.Production, EventCode.TaskCompleted, executor.Id.Value,
                new Dictionary<string, long> { ["task"] = task.Id.Value });
            Retire(executor.Queue, task.Id);
        }

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
            foreach (var item in Catalog.Items)
            {
                contents.Add(new ItemStock(
                    item.Id, Available(storage.Id, item.Id), CapacityOf(storage, item)));
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
                hauler.Built,
                hauler.Status,
                hauler.Current,
                CurrentTransfer(hauler)?.Transfer.Item,
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
                task.MovedQuantity));
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
                plan.State));
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
