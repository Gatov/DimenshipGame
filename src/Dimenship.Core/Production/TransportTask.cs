using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// A request to move a quantity of one item between two storages, sitting in one transport
/// executor's queue.
/// <para>
/// It behaves the way a production task does: it moves what is there rather than waiting for the
/// whole quantity to exist, postpones with a reason when it cannot, and resumes without losing
/// what it already moved.
/// </para>
/// <para>
/// Mutable, and mutated only by the engine. The snapshot carries an immutable projection.
/// </para>
/// </summary>
public sealed class TransportTask
{
    private readonly List<TaskAttempt> _history = new();

    public required ItemId Item { get; init; }

    /// <summary>
    /// How much to move, or null for a standing order — haul this item down this line for as long
    /// as there is any to haul. An indefinite transfer never completes.
    /// </summary>
    public required long? RequestedQuantity { get; init; }

    /// <summary>True when this transfer hauls for as long as there is anything to haul.</summary>
    public bool IsIndefinite => RequestedQuantity is null;

    public required StorageId Source { get; init; }

    public required StorageId Destination { get; init; }

    public required ExecutorId ExecutorId { get; init; }

    public TaskId Id { get; internal set; }

    public TaskState State { get; internal set; } = TaskState.NotStarted;

    public long MovedQuantity { get; internal set; }

    public PostponeReason? LastReason { get; internal set; }

    public long? PostponedAtTick { get; internal set; }

    public IReadOnlyList<TaskAttempt> History => _history;

    public bool IsFinished => State == TaskState.Complete;

    /// <inheritdoc cref="ProductionTask.RecordAttempt"/>
    internal bool RecordAttempt(long tick, TaskAttemptOutcome outcome, PostponeReason? reason)
    {
        if (_history.Count > 0)
        {
            var last = _history[^1];
            if (last.Outcome == outcome && last.Reason == reason)
            {
                return false;
            }
        }

        _history.Add(new TaskAttempt(tick, outcome, reason));
        while (_history.Count > ProductionTask.AttemptHistoryCapacity)
        {
            _history.RemoveAt(0);
        }

        return true;
    }
}
