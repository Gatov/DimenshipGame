using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning.Draft;

/// <summary>
/// One player edit and its automatic consequences. <see cref="PlanDraftEditor.Adjust"/> applies
/// exactly one per revision so Undo restores a whole draft, not one side-effect at a time.
/// </summary>
public abstract record DraftEdit;

public sealed record SetQuantity(DraftStepId Step, long Quantity) : DraftEdit;

public sealed record SetExecutor(DraftStepId Step, ExecutorId Executor) : DraftEdit;

public sealed record SetLock(DraftStepId Step, DraftField Field, bool Locked) : DraftEdit;

public sealed record AddMove(
    ItemId Item,
    StorageId From,
    StorageId To,
    long Quantity,
    ExecutorId Line) : DraftEdit;

public sealed record RemoveStep(DraftStepId Step) : DraftEdit;

public sealed record ReAdjust : DraftEdit;

public sealed record UnlockAll : DraftEdit;
