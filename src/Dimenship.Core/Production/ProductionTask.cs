using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// A request for a number of executions of one schematic, sitting in one executor's queue.
/// <para>
/// It references the authoritative production instructions by id: inputs, output, effort and
/// energy are never copied here, so a facility upgrade changes execution without touching any
/// task in flight.
/// </para>
/// <para>
/// Mutable, and mutated only by the engine. The snapshot carries an immutable projection.
/// </para>
/// </summary>
public sealed class ProductionTask
{
    /// <summary>
    /// How many attempts a task remembers. Bounded for the same reason the engine's event buffer
    /// is: a task postponed and retried for an hour of simulated time is ordinary operation, not
    /// a pathological case, and an unbounded list would grow for as long as the session lasts.
    /// </summary>
    public const int AttemptHistoryCapacity = 64;

    private readonly List<TaskAttempt> _history = new();

    public required SchematicId SchematicId { get; init; }

    /// <summary>Number of schematic executions requested.</summary>
    public required int RequestedRuns { get; init; }

    /// <summary>The executor whose queue this task was injected into.</summary>
    public required ExecutorId ExecutorId { get; init; }

    public TaskId Id { get; internal set; }

    public TaskState State { get; internal set; } = TaskState.NotStarted;

    public int CompletedRuns { get; internal set; }

    /// <summary>True from the moment a run's inputs are consumed until its output is deposited.</summary>
    public bool RunActive { get; internal set; }

    /// <summary>
    /// True when a run has finished its work but its output would not fit. The run is held —
    /// neither the work nor the consumed inputs are lost — until room appears.
    /// </summary>
    public bool RunAwaitingDeposit { get; internal set; }

    /// <summary>Work completed on the run in progress. Resets when a run is deposited.</summary>
    public long WorkDoneThisRun { get; internal set; }

    /// <summary>
    /// Energy charged so far for the run in progress. Tracked cumulatively rather than as a
    /// per-tick slice so the rounding remainder settles itself on the final tick and a completed
    /// run has charged exactly the schematic's energy, however the effort divides by work rate.
    /// </summary>
    public long EnergyChargedThisRun { get; internal set; }

    public PostponeReason? LastReason { get; internal set; }

    public long? PostponedAtTick { get; internal set; }

    public IReadOnlyList<TaskAttempt> History => _history;

    public bool IsFinished => State == TaskState.Complete;

    /// <summary>
    /// Records an attempt, ignoring one that repeats the previous entry's outcome and reason.
    /// A task blocked on the same thing for a thousand ticks made one decision, not a thousand,
    /// and a history of a thousand identical rows would say less than a history of one.
    /// Returns whether anything was recorded.
    /// </summary>
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
        while (_history.Count > AttemptHistoryCapacity)
        {
            _history.RemoveAt(0);
        }

        return true;
    }
}
