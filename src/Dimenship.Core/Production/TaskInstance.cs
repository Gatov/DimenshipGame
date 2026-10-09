using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// A queued request sitting on one executor — produce or transfer, one mutable body.
/// <para>
/// Progress is a flat union (runs / work / energy / moved quantity) rather than a nested
/// <c>Progress</c> record: one save DTO is what makes the file diffable, and a nested optional
/// would put half the fields behind a null check for every reader.
/// </para>
/// <para>
/// Mutable, and mutated only by the engine. The snapshot carries an immutable projection.
/// </para>
/// </summary>
public sealed class TaskInstance
{
    /// <summary>
    /// How many attempts a task remembers. Bounded for the same reason the engine's event buffer
    /// is: a task postponed and retried for an hour of simulated time is ordinary operation, not
    /// a pathological case, and an unbounded list would grow for as long as the session lasts.
    /// </summary>
    public const int AttemptHistoryCapacity = 64;

    private readonly List<TaskAttempt> _history = new();

    private TaskScript _script = null!;

    /// <summary>
    /// What the task was asked to do. Replaced only by a cancel or an amend (D3, Decision 7), which
    /// cuts a request back to the work already physically started: the runs completed plus the one
    /// in progress, or what a transfer has loaded. The truncated script is the saved one, so a
    /// cancelled task needs no state of its own and finishes by the ordinary completion path.
    /// </summary>
    public required TaskScript Script { get => _script; init => _script = value; }

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

    /// <summary>
    /// How much of a transfer has arrived at its destination. Progress counts deliveries, not
    /// pickups: cargo still on the belt has not been moved yet, and completing a transfer when the
    /// last of it was picked up would tell a plan its material was in place a whole belt early.
    /// </summary>
    public long MovedQuantity { get; internal set; }

    /// <summary>
    /// How much of a transfer has been put on the belt. What the line loads against, so a haul
    /// whose cargo is all in flight stops being picked up without being finished.
    /// </summary>
    public long LoadedQuantity { get; internal set; }

    /// <summary>
    /// How urgent this task is, when it was queued by hand. A task inside a plan reads its plan's
    /// priority instead (<c>CommittedPlan.Priority</c>) and this stays at the default, unsaved:
    /// the engine's <c>PriorityOf</c> is the one place that chooses between the two.
    /// </summary>
    public Priority Priority { get; internal set; } = Priority.Normal;

    /// <summary>
    /// Whether a task queued by hand is held: between runs, or with nothing loaded, it postpones
    /// with <see cref="PostponeReason.SafetyLock"/>, and what is physically committed finishes. A
    /// task inside a plan reads its plan's <c>Held</c> instead and this stays false, unsaved, for
    /// the reason <see cref="Priority"/> does: one answer to one question.
    /// </summary>
    public bool Held { get; internal set; }

    public PostponeReason? LastReason { get; internal set; }

    public long? PostponedAtTick { get; internal set; }

    /// <summary>
    /// The tick the task was queued at. Tick 0 for a scenario's opening work, which is queued
    /// before the first tick runs. Null only on a save from before the field existed.
    /// <para>
    /// The three lifecycle ticks are state rather than a scan of <see cref="History"/>, because the
    /// history is bounded and de-duplicated: an hour of postponements rolls the first start out of
    /// it. They are what queue wait (start minus queue) and a plan's readiness (its last
    /// completion) are measured from, and an executor-level window cannot attribute either to a
    /// task.
    /// </para>
    /// </summary>
    public long? EnqueuedAtTick { get; internal set; }

    /// <summary>
    /// The tick the first run started, or the first cargo of a transfer was picked up. Set once and
    /// never moved by a later run.
    /// </summary>
    public long? FirstStartedAtTick { get; internal set; }

    /// <summary>
    /// The tick the last requested run deposited, or the last of a transfer was delivered. Never
    /// set on a standing order, which does not complete.
    /// </summary>
    public long? CompletedAtTick { get; internal set; }

    public IReadOnlyList<TaskAttempt> History => _history;

    public bool IsFinished => State == TaskState.Complete;

    /// <summary>The produce action. Only valid on a production task — callers have already dispatched.</summary>
    public Produce Produce => (Produce)Script.Action;

    /// <summary>The transfer action. Only valid on a haul — callers have already dispatched.</summary>
    public Transfer Transfer => (Transfer)Script.Action;

    /// <summary>True when this task's action is a produce run.</summary>
    public bool IsProduce => Script.Action is Produce;

    /// <summary>True when this task's action is a transfer.</summary>
    public bool IsTransfer => Script.Action is Transfer;

    /// <summary>Replaces the script, for a cancel or an amend cutting the task back.</summary>
    internal void Replace(TaskScript script) => _script = script;

    /// <summary>
    /// Replaces the history wholesale, for a load. <see cref="RecordAttempt"/> de-duplicates, which
    /// is right while a task is running and wrong when replaying what already happened: a save that
    /// went back through it would collapse entries the original never merged.
    /// </summary>
    internal void RestoreHistory(IEnumerable<TaskAttempt> history)
    {
        _history.Clear();
        _history.AddRange(history);
    }

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
