using Dimenship.Core.Planning;

namespace Dimenship.Core.Planning.Draft;

/// <summary>
/// What <see cref="PlanDraftEditor.Approve"/> returns. Refusal carries the issues that blocked
/// commit so the shell can show them in the same frame rather than parking them on context.
/// </summary>
public abstract record PlanApproval;

public sealed record PlanApprovalCommitted(ProductionPlan Plan) : PlanApproval;

public sealed record PlanApprovalRefused(IReadOnlyList<DraftIssue> Issues) : PlanApproval;
