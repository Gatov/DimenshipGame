using Dimenship.Core.Simulation;
using Dimenship.Core.State;

namespace Dimenship.Core.Production;

/// <summary>
/// One recorded step in a task's execution history.
/// <para>
/// A postponement can name who it is waiting behind (K8; D3, Decision 4): <paramref name="ByTask"/>
/// is the task chosen instead, for <see cref="PostponeReason.Outranked"/>, and
/// <paramref name="ByPlan"/> is the plan holding the stock, for
/// <see cref="PostponeReason.MaterialClaimed"/>, or holding the room, for
/// <see cref="PostponeReason.DestinationFull"/>. A reason alone says a repair is waiting; the
/// cause says it waits because its components are held for the upgrade, which is the report the
/// design asks for. Both are null when the reason has no other party, which is most of them.
/// </para>
/// </summary>
public sealed record TaskAttempt(
    long Tick,
    TaskAttemptOutcome Outcome,
    PostponeReason? Reason,
    TaskId? ByTask = null,
    PlanId? ByPlan = null);
