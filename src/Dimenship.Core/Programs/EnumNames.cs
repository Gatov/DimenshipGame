using Dimenship.Core.Simulation;

namespace Dimenship.Core.Programs;

/// <summary>
/// The wire vocabulary for <see cref="EnumRef"/>: which enums a condition may name, and the
/// translation between a member's name and the ordinal the evaluator compares.
/// <para>
/// It exists because <see cref="EnumRef"/> carries <c>(Kind, Value)</c> in memory — an integer, so
/// <c>ConditionEvaluator</c> can compare it against a status without a per-enum branch — while a
/// save carries the member's <b>name</b>. Writing the ordinal instead would put a number on disk
/// whose meaning lives in a C# declaration order: append a member ahead of another and every
/// saved condition quietly means something else, with nothing in the file to notice. The two
/// directions live together here so a kind cannot be readable and unwritable, or the reverse.
/// </para>
/// <para>
/// One kind ships, because one <see cref="ConditionKind"/> reads an enum. A second kind is a case
/// added to both switches; an unknown kind resolves to null, and the caller reports it, rather
/// than throwing at a player holding a save.
/// </para>
/// </summary>
public static class EnumNames
{
    /// <summary>The kind string for <see cref="ExecutorStatus"/>, spelled once.</summary>
    public const string ExecutorStatusKind = nameof(ExecutorStatus);

    /// <summary>
    /// The member name for a reference, or null when its kind is unknown or its ordinal names no
    /// member. Null is a reportable condition, not a crash: the reference came off a file.
    /// </summary>
    public static string? Name(EnumRef reference) =>
        reference.Kind switch
        {
            ExecutorStatusKind when Enum.IsDefined(typeof(ExecutorStatus), reference.Value) =>
                ((ExecutorStatus)reference.Value).ToString(),
            _ => null,
        };

    /// <summary>The ordinal for a member name, or null when the kind or the member is unknown.</summary>
    public static int? Value(string kind, string member) =>
        kind switch
        {
            ExecutorStatusKind when Enum.TryParse<ExecutorStatus>(member, out var status) =>
                (int)status,
            _ => null,
        };
}
