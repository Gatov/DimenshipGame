using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Presentation;

/// <summary>What a production facility is doing, in the shell's words (U3).</summary>
public enum ExecutorStanding
{
    /// <summary>Not commissioned. <see cref="ConstructionProgress"/> says where its build stands.</summary>
    Unbuilt,

    /// <summary>Nothing queued.</summary>
    Idle,

    /// <summary>A run is in progress.</summary>
    Working,

    /// <summary>A changeover is loading another setup.</summary>
    ChangingOver,

    /// <summary>Work is queued and its input is not here, or is held for another plan.</summary>
    Waiting,

    /// <summary>Work is queued and energy, fuel or compute is short.</summary>
    Throttled,

    /// <summary>Work is queued and a hold, a gate or a missing prerequisite keeps it from starting.</summary>
    Held,

    /// <summary>Output it cannot put down. The only standing that is a fault.</summary>
    Blocked,
}

/// <summary>
/// A production facility's standing, the reason behind it, and its setup, projected from a
/// snapshot and thrown away (U3), like <see cref="ConstructionProgress"/> and
/// <see cref="WaitCause"/>. The executor card and the inspector both read it, so the two cannot
/// name one condition two ways.
/// <para>
/// <b>Blocked means output it cannot put down, and nothing else.</b> That is the rule a transport
/// line already follows, where blocked is cargo at a frozen head. Before this, every facility whose
/// queued work all postponed read "Blocked" in the fault colour, so a factory waiting for ore it
/// had asked for looked the same as one choking on output nobody would take. The categories are
/// <see cref="UtilizationWindow.CategoryOf"/>'s, and not a second mapping, so the utilization
/// window and the card cannot file one stall under two causes.
/// </para>
/// <para>
/// A task that cannot deposit outranks the facility's own reading. The engine reports the root
/// cause by declaration order, which puts a held task's <see cref="PostponeReason.SafetyLock"/>
/// ahead of another task's <see cref="PostponeReason.DestinationFull"/>. That ordering is right for
/// the journal and wrong for the card: the full buffer is the one the player has to act on (K8
/// review, Open).
/// </para>
/// </summary>
/// <param name="Setup">The schematic the facility is set up for, or null if never configured.</param>
/// <param name="SwitchingTo">The setup a changeover in progress is loading.</param>
/// <param name="ChangeoverTicksRemaining">Zero unless a changeover is in progress.</param>
public sealed record ExecutorCondition(
    ExecutorStanding Standing,
    PostponeReason? Reason,
    SchematicId? Setup,
    SchematicId? SwitchingTo,
    long ChangeoverTicksRemaining,
    long ChangeoverTicksTotal)
{
    public static ExecutorCondition For(WorldSnapshot snapshot, ExecutorId id)
    {
        var executor = snapshot.Executors.FirstOrDefault(e => e.Id == id)
            ?? throw new ArgumentException($"The snapshot has no facility '{id}'.", nameof(id));

        var (standing, reason) = StandingOf(snapshot, executor);
        var changing = executor.SwitchOverTicksRemaining > 0;

        return new ExecutorCondition(
            standing,
            reason,
            executor.Configured,
            changing ? executor.SwitchingTo : null,
            changing ? executor.SwitchOverTicksRemaining : 0,
            executor.SwitchOverTicksTotal);
    }

    private static (ExecutorStanding, PostponeReason?) StandingOf(WorldSnapshot snapshot, ExecutorState executor)
    {
        if (!executor.Built)
        {
            return (ExecutorStanding.Unbuilt, null);
        }

        switch (executor.Status)
        {
            case ExecutorStatus.RunningTask:
                return (ExecutorStanding.Working, null);
            case ExecutorStatus.SwitchingOver:
                return (ExecutorStanding.ChangingOver, null);
            case ExecutorStatus.AllQueuedTasksBlocked:
                break;
            default:
                return (ExecutorStanding.Idle, null);
        }

        foreach (var task in snapshot.Tasks)
        {
            if (task.Executor == executor.Id
                && task is { State: TaskState.Postponed, LastReason: { } taskReason }
                && Category(taskReason) == UtilizationCategory.WaitingOutput)
            {
                return (ExecutorStanding.Blocked, taskReason);
            }
        }

        if (executor.BlockReason is not { } reason)
        {
            return (ExecutorStanding.Held, null);
        }

        return Category(reason) switch
        {
            UtilizationCategory.WaitingInput => (ExecutorStanding.Waiting, reason),
            UtilizationCategory.WaitingOutput => (ExecutorStanding.Blocked, reason),
            UtilizationCategory.Throttled => (ExecutorStanding.Throttled, reason),
            _ => (ExecutorStanding.Held, reason),
        };
    }

    private static UtilizationCategory Category(PostponeReason reason) =>
        UtilizationWindow.CategoryOf(ExecutorStatus.AllQueuedTasksBlocked, reason);
}
