using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>One recorded step in a task's execution history.</summary>
public sealed record TaskAttempt(long Tick, TaskAttemptOutcome Outcome, PostponeReason? Reason);
