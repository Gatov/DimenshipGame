namespace Dimenship.Core.Programs;

/// <summary>
/// A gate on starting a task. Lifted from the programming-view design so a task script and a
/// program share one condition language rather than inventing a second one later.
/// <para>
/// Only two kinds ship in this step; the rest stay on that document. Evaluated against live
/// engine state — a task is gated by what is true now, not by what the last snapshot published.
/// </para>
/// </summary>
public sealed record Condition(
    ConditionKind Kind,
    IReadOnlyList<Operand> Operands,
    Comparison Op,
    Operand Value);

/// <summary>
/// What a condition reads. Closed, and every member names a field live state already carries.
/// <para>
/// <see cref="ExecutorStatus"/> is the programming-view's <c>ExecutorStatusIs</c>, renamed to the
/// shorter member that reads an executor's status rather than asserting an is-check in the name.
/// </para>
/// </summary>
public enum ConditionKind
{
    /// <summary>Amount of one item in one storage.</summary>
    StorageItemAmount,

    /// <summary>An executor's <c>Status</c>.</summary>
    ExecutorStatus,
}

/// <summary>How the left-hand reading is compared to the right-hand value.</summary>
public enum Comparison
{
    LessThan,
    LessOrEqual,
    Equal,
    NotEqual,
    GreaterOrEqual,
    GreaterThan,
}
