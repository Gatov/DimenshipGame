using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>One unfinished production task on one facility, as the inspector's QUEUE lists it (U4).</summary>
/// <param name="Plan">The plan the task is for; null for a task queued by hand, which no row can open.</param>
/// <param name="Runs">Null for a standing order, which has no total to show progress against.</param>
/// <param name="Current">The run in progress, or the task a switch-over is loading.</param>
/// <param name="Priority">The effective priority, the plan's when there is one, as selection reads it.</param>
/// <param name="Reason">Why its last attempt was postponed, if it was.</param>
/// <param name="Waiting">Who it waits behind (K8), when its reason has another party.</param>
public sealed record FacilityQueueRow(
    TaskId Task,
    PlanId? Plan,
    SchematicId Schematic,
    int CompletedRuns,
    int? Runs,
    bool Current,
    ItemAmount? PlanGoal,
    Priority Priority,
    bool Held,
    PostponeReason? Reason,
    WaitCause? Waiting);

/// <summary>
/// A facility's work, current task first, then the rest in queue order, projected from a snapshot
/// and thrown away (U4; <c>2026-10-11-playable-inspector-and-move-control-design.md</c>). Before
/// it the inspector said a facility was busy and not with what, or for which order. The order is
/// the one selection reads, so the list does not promise a sequence the engine will not follow.
/// Finished tasks are left out: the queue is what is still to come.
/// </summary>
public static class FacilityQueue
{
    public static IReadOnlyList<FacilityQueueRow> For(WorldSnapshot snapshot, ExecutorId facility)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == facility)
            ?? throw new ArgumentException($"No facility '{facility}' in this snapshot.", nameof(facility));

        var owners = new Dictionary<TaskId, CommittedPlanState>();
        foreach (var plan in snapshot.Plans)
        {
            foreach (var id in plan.SpawnedTasks)
            {
                owners.TryAdd(id, plan);
            }
        }

        var rows = new List<FacilityQueueRow>();
        foreach (var task in snapshot.Tasks)
        {
            if (task.Executor != facility || task.State == TaskState.Complete || task.Action is not Produce produce)
            {
                continue;
            }

            var owner = owners.GetValueOrDefault(task.Id);
            var row = new FacilityQueueRow(
                task.Id,
                owner?.Id,
                produce.Schematic,
                task.CompletedRuns,
                produce.Runs,
                task.Id == executor.CurrentTask,
                owner?.Goal,
                task.Priority,
                task.Held,
                task.LastReason,
                WaitCause.For(snapshot, task.Id));

            if (row.Current)
            {
                rows.Insert(0, row);
            }
            else
            {
                rows.Add(row);
            }
        }

        return rows;
    }
}
