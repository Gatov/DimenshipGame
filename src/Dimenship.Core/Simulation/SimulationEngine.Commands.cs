using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
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
                return Done(ordered.Plan, ordered.Tasks);
            }

            case CommitPlan commit:
                var created = Commit(commit.Plan);
                return Done(created.Plan, created.Tasks);

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

            case MoveWork move:
                return Done(move.Plan, Move(move.Plan, move.Schematic, move.To));

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
    public Committed Commit(ProductionPlan plan)
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
        var committed = new CommittedPlan
        {
            Id = State.Plans.Mint(),
            Goal = plan.Goal,
            Destination = plan.Destination,
            CommittedAtTick = State.Clock.Tick,
            LastProgressAtTick = State.Clock.Tick,
            SpawnedTasks = created.ToList(),
        };
        State.Plans.Record(committed);

        // Whatever is free when a plan commits goes to its claims: every earlier plan was already
        // fully held, or had nothing outstanding there (D3, Decision 5's invariant).
        foreach (var (storage, item) in ClaimMath.Withdrawals(Catalog, State, committed))
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
        return new Committed(committed.Id, created);
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
}
