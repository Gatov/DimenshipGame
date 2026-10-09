using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>
/// Who a postponed task is waiting behind, in a sentence, projected from a snapshot and thrown
/// away (K8). The design's §5 asks for exactly this report: <i>"a repair waited because its
/// components were allocated to an upgrade"</i>.
/// <para>
/// It explains the other party and nothing else. A task short of ore, or gated by a condition, has
/// no other party, and its reason alone is already the whole story the shell tells; this returns
/// null for it, rather than a second wording of a reason the shell already words.
/// </para>
/// <para>
/// A pure projection beside <see cref="ConstructionProgress"/>, for the same reason: the inspector,
/// the Operations detail and the replay report read one answer, and the kernel owns no interface
/// vocabulary on its live types.
/// </para>
/// </summary>
/// <param name="Plan">The plan waited behind, when there is one.</param>
/// <param name="Task">The task chosen instead, for an outranked task.</param>
public sealed record WaitCause(PostponeReason Reason, PlanId? Plan, TaskId? Task, string Text)
{
    public static WaitCause? For(WorldSnapshot snapshot, TaskId id)
    {
        var task = snapshot.Tasks.FirstOrDefault(t => t.Id == id);
        if (task is not { LastReason: { } reason } || (task.WaitingOnTask is null && task.WaitingOnPlan is null))
        {
            return null;
        }

        var plans = snapshot.Plans.ToDictionary(p => p.Id);
        string Describe(PlanId plan) => plans.TryGetValue(plan, out var p)
            ? $"plan {plan} ({Units.Format(p.Goal.Quantity)} {p.Goal.Item}, {p.Priority}{(p.Held ? ", held" : string.Empty)})"
            : $"plan {plan}";

        switch (reason)
        {
            case PostponeReason.Outranked when task.WaitingOnTask is { } winner:
            {
                var owner = snapshot.Plans.FirstOrDefault(p => p.SpawnedTasks.Contains(winner));
                var chosen = snapshot.Tasks.FirstOrDefault(t => t.Id == winner);
                var by = owner is not null
                    ? $"task {winner} of {Describe(owner.Id)}"
                    : $"task {winner}, queued by hand at {chosen?.Priority.ToString() ?? "a higher priority"}";
                return new WaitCause(reason, owner?.Id, winner, $"Ready, but {by} ranks higher.");
            }

            case PostponeReason.MaterialClaimed when task.WaitingOnPlan is { } holder:
                return new WaitCause(reason, holder, null, $"The stock it needs is held for {Describe(holder)}.");

            case PostponeReason.DestinationFull when task.WaitingOnPlan is { } holder:
                return new WaitCause(
                    reason, holder, null, $"No room for its output: {Describe(holder)} holds stock there.");

            default:
                return null;
        }
    }
}
