using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>Why part of a goal could not be planned at all — nothing is queued for it.</summary>
public enum UnplannableReason
{
    /// <summary>A schematic produces it, but the player has not unlocked one. No branch is built.</summary>
    LockedSchematic,

    /// <summary>The vessel has no facility of the required type, or no transport line to route it.</summary>
    NoExecutorOrLine,

    /// <summary>The schematic chain re-entered itself. A content error, not a supply problem.</summary>
    CyclicSchematic,
}

/// <summary>
/// One script the plan proposes, and the executor it is proposed for. Nothing here has an
/// execution state, and nothing exists in the world until
/// <see cref="Simulation.SimulationEngine.Commit"/> injects it into an executor's queue.
/// <para>
/// <paramref name="AvailableAtSource"/> is how much of the task's own material need was already
/// aboard, uncommitted, at the moment the plan was built — the same number a shortage used to
/// report, now carried per task instead of summed into a separate list. A haul asking to move
/// more than this is asking for material that still has to be produced or acquired; the task
/// still moves the full amount, and the engine postpones it on
/// <see cref="Simulation.PostponeReason.InsufficientSourceMaterial"/> until the rest arrives.
/// </para>
/// </summary>
public sealed record PlannedTask(TaskScript Script, ExecutorId Executor, long AvailableAtSource);

/// <summary>Something the plan could not supply at all, and why. No task was queued for it.</summary>
public sealed record Unplannable(ItemId Item, long Quantity, UnplannableReason Reason);

/// <summary>
/// How a goal may be fulfilled. A proposal, not tasks: nothing here has an execution state, and
/// nothing exists in the world until <see cref="Simulation.SimulationEngine.Commit"/> injects it
/// into executor queues.
/// <para>
/// A plan carrying unplannable entries is still committable. The available portion may begin
/// immediately while the player decides what to do about the rest.
/// </para>
/// </summary>
public sealed record ProductionPlan(
    ItemAmount Goal,
    StorageId? Destination,
    IReadOnlyList<PlannedTask> Tasks,
    IReadOnlyList<Unplannable> Unplannable,
    long EstimatedTicks)
{
    /// <summary>True when the plan can be carried out in full with what the vessel has.</summary>
    public bool IsComplete => Unplannable.Count == 0;
}
