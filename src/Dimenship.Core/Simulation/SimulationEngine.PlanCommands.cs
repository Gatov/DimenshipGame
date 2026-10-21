using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Programs;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
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

            // Time spent held is the player's decision, not the plan starving; the stall the
            // waiting alert measures starts again from the release.
            committed.LastProgressAtTick = State.Clock.Tick;
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
        committed.LastProgressAtTick = State.Clock.Tick;
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
    /// Moves a plan's unstarted runs of one schematic to another facility, and their legs with
    /// them (K6d; <c>docs/superpowers/specs/2026-10-10-moving-committed-work-design.md</c>). It is
    /// the design's <i>assign eligible work</i> after commit. The composer's picker makes the same
    /// choice before approval, and without this a facility built in the middle of a campaign could
    /// never take work already bound to another.
    /// <para>
    /// Only work whose material has not left its source moves. A run in progress stays, and so
    /// does a run whose input is already loaded toward the old buffer or whose output leg has
    /// already loaded. Moving such a run would strand its input in a buffer no line joins, or
    /// order it twice from the hold. Per old facility, the runs moved are the least of its
    /// unstarted runs, each input's unloaded inbound remainder over the input per run, and the
    /// output's unloaded outbound remainder over the output per run. Material is fungible inside
    /// a buffer, so a leg two stages share is cut by quantity without asking whose it was.
    /// </para>
    /// <para>
    /// The old tasks are cut, never redirected, as a cancel cuts them: the produce task loses the
    /// moved runs, and its legs the moved quantity, last task first. New tasks are appended, which
    /// keeps the plan's id, priority and held flag: an inbound leg per source and item, the runs at
    /// <paramref name="to"/>, and an outbound leg per destination and item. A line is chosen by the
    /// planner's route rule. Every check, a missing line included, runs before anything changes.
    /// </para>
    /// <para>
    /// Claims need no settling, unlike an amend's. At the old buffer a move lowers the plan's
    /// need and its inbound by the same quantity, because only runs whose material is unloaded
    /// move. At the new buffer it raises both by the same quantity, and in the hold it changes
    /// neither. So no holding exceeds its need afterwards, and no outstanding claim is new.
    /// <c>MoveWorkTests</c> checks the invariant on every tick after a move.
    /// </para>
    /// </summary>
    public IReadOnlyList<TaskId> Move(PlanId plan, SchematicId schematic, ExecutorId to)
    {
        var committed = ActivePlan(plan);
        if (!Catalog.Schematics.TryGet(schematic, out var definition))
        {
            throw new ArgumentException($"No schematic '{schematic}' in this catalog.", nameof(schematic));
        }

        // The checks Enqueue makes on a production task, so the sentences are the same ones.
        var runsThere = new Produce(schematic, 1);
        RequireProducible(new TaskScript(Array.Empty<Condition>(), runsThere), runsThere, to);
        var target = _facilitiesById[to];

        var live = committed.SpawnedTasks
            .Select(id => State.Tasks.Task(id))
            .OfType<TaskInstance>()
            .Where(t => !t.IsFinished)
            .ToList();
        var sources = live
            .Where(t => t.IsProduce && t.Produce.Schematic == schematic && t.ExecutorId != to && Unstarted(t) > 0)
            .ToList();
        if (sources.Count == 0)
        {
            throw new ArgumentException(
                $"Plan '{plan}' has no unstarted runs of '{schematic}' anywhere but '{to}'.", nameof(plan));
        }

        var cuts = new List<(TaskInstance Task, long By)>();
        var inbound = new List<(StorageId Storage, ItemId Item, long Quantity)>();
        var outbound = new List<(StorageId Storage, ItemId Item, long Quantity)>();
        var moved = 0L;
        foreach (var facility in State.Vessel.Facilities)
        {
            var mine = sources.Where(t => t.ExecutorId == facility.Id).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var buffer = facility.LocalStorage;
            var arriving = live.Where(t => t.IsTransfer && t.Transfer.To == buffer).ToList();
            var leaving = live.Where(t => t.IsTransfer && t.Transfer.From == buffer).ToList();

            var runs = mine.Sum(Unstarted);
            foreach (var input in definition.Inputs)
            {
                runs = Math.Min(runs, Unloaded(arriving, input.Item) / input.Quantity);
            }

            runs = Math.Min(runs, Unloaded(leaving, definition.Output.Item) / definition.Output.Quantity);
            if (runs <= 0)
            {
                continue;
            }

            moved += runs;
            var left = runs;
            for (var i = mine.Count - 1; i >= 0 && left > 0; i--)
            {
                var by = Math.Min(left, Unstarted(mine[i]));
                cuts.Add((mine[i], by));
                left -= by;
            }

            foreach (var input in definition.Inputs)
            {
                foreach (var (leg, by) in CutLegs(arriving, input.Item, runs * input.Quantity))
                {
                    cuts.Add((leg, by));
                    Add(inbound, leg.Transfer.From, input.Item, by);
                }
            }

            foreach (var (leg, by) in CutLegs(leaving, definition.Output.Item, runs * definition.Output.Quantity))
            {
                cuts.Add((leg, by));
                Add(outbound, leg.Transfer.To, definition.Output.Item, by);
            }
        }

        if (moved == 0)
        {
            throw new ArgumentException(
                $"None of plan '{plan}''s unstarted runs of '{schematic}' can move: the material for each " +
                "has already been sent to the facility it is queued at, or its output leg has already loaded.",
                nameof(plan));
        }

        // Every new task is scripted and checked before any task is touched, a missing line
        // included, so a refusal leaves the plan exactly as it was (C0, Decision 2).
        var lines = ((IWorldView)this).TransportLines;
        var load = new Dictionary<ExecutorId, long>();
        var scripts = new List<(TaskScript Script, ExecutorId Executor)>();
        foreach (var (from, item, quantity) in inbound)
        {
            scripts.Add(Leg(lines, load, item, quantity, from, target.LocalStorage));
        }

        scripts.Add((new TaskScript(Array.Empty<Condition>(), new Produce(schematic, (int)moved)), to));
        foreach (var (destination, item, quantity) in outbound)
        {
            scripts.Add(Leg(lines, load, item, quantity, target.LocalStorage, destination));
        }

        foreach (var (script, executor) in scripts)
        {
            RequireQueueable(script, executor);
        }

        var cut = new List<TaskInstance>();
        foreach (var (task, by) in cuts)
        {
            TaskAction action = task.Script.Action switch
            {
                Produce produce => produce with { Runs = produce.Runs - (int)by },
                Transfer transfer => transfer with { Quantity = transfer.Quantity - by },
                var other => other,
            };

            task.Replace(task.Script with { Action = action });

            // A line keeps the transfer in hand until it is entirely aboard; a cut can make it so.
            if (task.IsTransfer && _linesById.TryGetValue(task.ExecutorId, out var line)
                && line.Current == task.Id && FullyLoaded(task))
            {
                line.Current = null;
            }

            if (!cut.Contains(task))
            {
                cut.Add(task);
            }
        }

        var created = scripts.Select(s => Queue(s.Script, s.Executor)).ToList();
        committed.LastProgressAtTick = State.Clock.Tick;
        State.Plans.Append(committed, created);

        // After the append, so a plan whose old work is all cut short is not finished by it.
        FinishCutShort(cut);

        Emit(EventCategory.Planning, EventCode.WorkMoved, to.Value,
            new Dictionary<string, long>
            {
                ["plan"] = plan.Value,
                ["runs"] = moved,
                ["tasks"] = created.Count,
            });

        Snapshot = BuildSnapshot();
        return created;

        static long Unstarted(TaskInstance task) => task.Produce.Runs is { } requested
            ? requested - task.CompletedRuns - (task.RunActive || task.RunAwaitingDeposit ? 1 : 0)
            : 0;

        static long Unloaded(IEnumerable<TaskInstance> legs, ItemId item) =>
            legs.Where(t => t.Transfer.Item == item && t.Transfer.Quantity is not null)
                .Sum(t => t.Transfer.Quantity!.Value - t.LoadedQuantity);

        static List<(TaskInstance Leg, long By)> CutLegs(List<TaskInstance> legs, ItemId item, long quantity)
        {
            var taken = new List<(TaskInstance, long)>();
            for (var i = legs.Count - 1; i >= 0 && quantity > 0; i--)
            {
                var leg = legs[i];
                if (leg.Transfer.Item != item || leg.Transfer.Quantity is not { } requested)
                {
                    continue;
                }

                var by = Math.Min(quantity, requested - leg.LoadedQuantity);
                if (by > 0)
                {
                    taken.Add((leg, by));
                    quantity -= by;
                }
            }

            return taken;
        }

        static void Add(List<(StorageId Storage, ItemId Item, long Quantity)> legs, StorageId storage, ItemId item, long by)
        {
            var index = legs.FindIndex(l => l.Storage == storage && l.Item == item);
            if (index < 0)
            {
                legs.Add((storage, item, by));
            }
            else
            {
                legs[index] = (storage, item, legs[index].Quantity + by);
            }
        }
    }

    /// <summary>
    /// A moved leg's transfer, on the line the planner would choose (<see cref="LineChoice.Best"/>).
    /// A leg the same move already placed counts toward its line's load. With no built line on the
    /// route the move is refused, naming the route.
    /// </summary>
    private static (TaskScript Script, ExecutorId Executor) Leg(
        IReadOnlyList<PlannerTransport> lines, Dictionary<ExecutorId, long> load, ItemId item, long quantity,
        StorageId from, StorageId to)
    {
        if (LineChoice.Best(lines, from, to, load) is not { } best)
        {
            throw new ArgumentException(
                $"No built line runs '{from}' to '{to}', so the {item} for the moved work has no way there.",
                nameof(to));
        }

        load[best.Id] = load.GetValueOrDefault(best.Id) + 1;
        return (new TaskScript(Array.Empty<Condition>(), new Transfer(item, quantity, from, to)), best.Id);
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
}
