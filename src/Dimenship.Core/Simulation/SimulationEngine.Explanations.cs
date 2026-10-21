using Dimenship.Core.Production;
using Dimenship.Core.State;

namespace Dimenship.Core.Simulation;

public sealed partial class SimulationEngine
{
    private static EventCode CodeFor(PostponeReason reason) => PostponeReasons.EventCodeOf(reason);

    private static EventCategory CategoryFor(PostponeReason reason) =>
        reason == PostponeReason.InsufficientEnergy ? EventCategory.Power : EventCategory.Production;

    /// <summary>
    /// How long a plan may make no progress while waiting behind another before it raises
    /// <see cref="AlertCode.PlanWaiting"/>: one operational hour, D3's first value. Tuning, to be
    /// measured against the scripted situations rather than guessed again.
    /// </summary>
    public const long WaitingPlanAlertTicks = Units.TicksPerHour;

    /// <summary>A plan's task moved, which is what the waiting-plan alert measures stalls from.</summary>
    private void Progressed(TaskInstance task)
    {
        if (State.Plans.Owning(task.Id) is { } plan)
        {
            plan.LastProgressAtTick = State.Clock.Tick;
        }
    }

    /// <summary>
    /// Raises and clears <see cref="AlertCode.PlanWaiting"/> (K8; D3, Decision 4). A plan that is
    /// active, not held, has made no progress for <see cref="WaitingPlanAlertTicks"/>, and has a task
    /// waiting behind another plan, is starving. The kernel says so and names who it waits behind,
    /// and corrects nothing: aging was rejected, because a silent correction is exactly the hidden
    /// arbitration the design forbids. The alert clears the tick the condition does.
    /// <para>
    /// A held plan raises nothing. The player stopped it, and an alert for it would be noise.
    /// </para>
    /// </summary>
    private void ReviewWaitingPlans()
    {
        foreach (var plan in State.Plans.Plans)
        {
            var subject = $"plan:{plan.Id}";
            var alert = State.Alerts.Alerts.FirstOrDefault(a => a.Code == AlertCode.PlanWaiting && a.SubjectId == subject);

            var waiting = plan.State == PlanState.Active && !plan.Held
                && State.Clock.Tick - plan.LastProgressAtTick >= WaitingPlanAlertTicks
                    ? WaitingBehind(plan)
                    : null;

            if (waiting is { } behind)
            {
                if (alert is null)
                {
                    State.Alerts.Alerts.Add(new Alert
                    {
                        Id = State.Alerts.Mint(),
                        Severity = AlertSeverity.Warning,
                        Code = AlertCode.PlanWaiting,
                        SubjectId = subject,
                        RaisedAtTick = State.Clock.Tick,
                        RootCause = behind.Reason,
                        RelatedSubjectId = behind.Other,
                    });
                    Emit(EventCategory.Planning, EventCode.AlertRaised, subject,
                        new Dictionary<string, long>
                        {
                            ["plan"] = plan.Id.Value,
                            ["reason"] = (long)behind.Reason,
                        });
                }
                else
                {
                    alert.RootCause = behind.Reason;
                    alert.RelatedSubjectId = behind.Other;
                }
            }
            else if (alert is not null)
            {
                State.Alerts.Alerts.Remove(alert);
                Emit(EventCategory.Planning, EventCode.AlertCleared, subject,
                    new Dictionary<string, long> { ["plan"] = plan.Id.Value });
            }
        }
    }

    /// <summary>
    /// Who a plan waits behind, read off its tasks' last postponements in commit order: the plan
    /// whose task outranked one of them, or which holds the stock or the room one of them needs.
    /// A task outranked by another task of the same plan is not waiting behind anybody. A winner
    /// queued by hand is named as a task, since it has no plan.
    /// </summary>
    private (PostponeReason Reason, string Other)? WaitingBehind(CommittedPlan plan)
    {
        foreach (var id in plan.SpawnedTasks)
        {
            if (State.Tasks.Task(id) is not { IsFinished: false, LastReason: { } reason } task
                || task.History.Count == 0 || task.History[^1].Outcome != TaskAttemptOutcome.Postponed)
            {
                continue;
            }

            var last = task.History[^1];
            if (last.ByPlan is { } holder && holder != plan.Id)
            {
                return (reason, $"plan:{holder}");
            }

            if (last.ByTask is { } winner)
            {
                var owner = State.Plans.Owning(winner);
                if (owner is null)
                {
                    return (reason, $"task:{winner}");
                }

                if (owner.Id != plan.Id)
                {
                    return (reason, $"plan:{owner.Id}");
                }
            }
        }

        return null;
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
        IReadOnlyDictionary<string, long>? data = null,
        TaskId? byTask = null)
    {
        task.State = TaskState.Postponed;
        task.LastReason = reason;
        task.PostponedAtTick = State.Clock.Tick;

        // The other party, where the reason has one (K8). Read here, from the world as it stands at
        // the moment of postponing, so every path that postpones for these reasons names its cause
        // the same way.
        var byPlan = reason switch
        {
            PostponeReason.MaterialClaimed => ClaimantOf(task),
            PostponeReason.DestinationFull => HolderOfRoom(task),
            _ => null,
        };

        // Edge-triggered. A task blocked on the same thing for a thousand ticks made one
        // decision, not a thousand, and emitting it every tick would bury everything else in the
        // console within seconds.
        if (task.RecordAttempt(State.Clock.Tick, TaskAttemptOutcome.Postponed, reason, byTask, byPlan))
        {
            if (byTask is null && byPlan is null)
            {
                Emit(category, CodeFor(reason), executor.Value, data ?? SimEvent.NoData);
                return;
            }

            var named = new Dictionary<string, long>(data ?? SimEvent.NoData) { ["task"] = task.Id.Value };
            if (byTask is { } winner)
            {
                named["by"] = winner.Value;
            }

            if (byPlan is { } holder)
            {
                named["plan"] = holder.Value;
            }

            Emit(category, CodeFor(reason), executor.Value, named);
        }
    }

    /// <summary>
    /// The plan holding the stock a task found present but could not take: for a run, the first
    /// input short of what it may spend; for a transfer, its item at its source. When several
    /// plans hold it, the one holding most, then the oldest.
    /// </summary>
    private PlanId? ClaimantOf(TaskInstance task)
    {
        if (task.IsTransfer)
        {
            return LargestHolder(task, task.Transfer.From, task.Transfer.Item);
        }

        if (!_facilitiesById.TryGetValue(task.ExecutorId, out var facility))
        {
            return null;
        }

        foreach (var input in Catalog.Schematics.Get(task.Produce.Schematic).Inputs)
        {
            if (Spendable(task, facility.LocalStorage, input.Item) < input.Quantity)
            {
                return LargestHolder(task, facility.LocalStorage, input.Item);
            }
        }

        return null;
    }

    /// <summary>
    /// The plan, other than the task's own, holding the most stock in the storage the task could
    /// not put its output into: a run's own buffer, or a transfer's destination. Null when no other
    /// plan holds anything there, which is the ordinary case of a buffer full of free stock. This
    /// is the report a held plan owes the plans beside it: its stock keeps its space (C0's review).
    /// </summary>
    private PlanId? HolderOfRoom(TaskInstance task)
    {
        if (task.IsTransfer)
        {
            return LargestHolder(task, task.Transfer.To, item: null);
        }

        return _facilitiesById.TryGetValue(task.ExecutorId, out var facility)
            ? LargestHolder(task, facility.LocalStorage, item: null)
            : null;
    }

    private PlanId? LargestHolder(TaskInstance task, StorageId storage, ItemId? item)
    {
        var own = State.Plans.Owning(task.Id)?.Id;
        var totals = new Dictionary<PlanId, long>();
        foreach (var (plan, at, held, quantity) in State.Claims.Entries)
        {
            if (at == storage && (item is null || held == item) && plan != own)
            {
                totals[plan] = totals.GetValueOrDefault(plan) + quantity;
            }
        }

        return totals.Count == 0
            ? null
            : totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key.Value).First().Key;
    }
}
