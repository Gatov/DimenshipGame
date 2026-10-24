using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;

namespace Dimenship.Ui;

/// <summary>
/// Small, shared catalog-label lookups. Falling back to the raw id when the catalog has no label
/// for it, then upper-casing the result, is a real policy decision — it lived as two byte-for-byte
/// identical private copies, in <see cref="OperationsFocus"/> and
/// <see cref="FacilityInspectorPanel"/>, before this. Only <c>ItemLabel</c> is consolidated here:
/// each panel's own <c>StorageLabel</c> / <c>FactoryLabel</c> / <c>TransportLabel</c> closes over
/// its own <c>_lastSnapshot</c> field and has exactly one call site, and the two archetype-lookup
/// call sites elsewhere resolve a facility to its archetype in genuinely different shapes — neither
/// is this kind of drift-prone duplication, so neither is folded in here.
/// </summary>
public static class Labels
{
    public static string Item(ItemId id) =>
        (ShellContent.Catalog.Item(id)?.Label ?? id.Value).ToUpperInvariant();

    /// <summary>
    /// A draft issue in the shell's words. One copy, shared by the composer, the plan controls and
    /// the inspector's build quote (U4), so one issue is never named two ways.
    /// <para>
    /// Every kind has its own sentence, in the player's vocabulary (facility, line, storage) rather
    /// than the kernel's (executor, endpoint) (UX-09). Four kinds once fell through to the enum's
    /// own name, so a player short of a line read <c>NOEXECUTORORLINE</c>. The planning kinds share
    /// their wording with <see cref="Unplannable"/>, since a committed plan reports the same
    /// conditions after the fact. The fallback still names the kind, so a kind appended to the
    /// kernel without a sentence here is visibly unworded rather than silently blank.
    /// </para>
    /// </summary>
    public static string Issue(DraftIssueKind kind) => kind switch
    {
        DraftIssueKind.IncompatibleExecutor => "THAT FACILITY CANNOT RUN THIS",
        DraftIssueKind.NoSuchRoute => "NO LINE RUNS THAT ROUTE",
        DraftIssueKind.UnknownEndpoint => "UNKNOWN STORAGE",
        DraftIssueKind.NonPositiveQuantity => "QUANTITY MUST BE ABOVE ZERO",
        DraftIssueKind.UnbuiltExecutor => "FACILITY NOT BUILT YET",
        DraftIssueKind.NotCommandable => "PASSIVE SOURCE — IT CANNOT BE ORDERED",
        DraftIssueKind.MaterialShortage => "NOT IN HOLD — ACQUIRE",
        DraftIssueKind.GoalShortfall => "THIS ORDER FALLS SHORT OF ITS GOAL",
        DraftIssueKind.LockedSchematic => Unplannable(UnplannableReason.LockedSchematic),
        DraftIssueKind.NoExecutorOrLine => Unplannable(UnplannableReason.NoExecutorOrLine),
        DraftIssueKind.CyclicSchematic => Unplannable(UnplannableReason.CyclicSchematic),
        DraftIssueKind.WorkpieceNotAccepted => "WORKPIECE NOT ACCEPTED THERE",
        _ => $"UNWORDED ISSUE ({kind.ToString().ToUpperInvariant()})",
    };

    /// <summary>
    /// A command the kernel refused: its own reason, then the planner's issues when an amend or a
    /// move could not be planned. One copy beside <see cref="Issue"/>, for the same reason: the plan
    /// controls and the inspector each wrote their own, and they disagreed, one leading with the
    /// kernel's reason and the other showing the issues alone.
    /// </summary>
    public static string Refusal(CommandRefused refused) => Refusal(refused.Reason, refused.Issues);

    /// <summary>
    /// A draft approval refused: there is no kernel sentence, only the draft's issues. The
    /// inspector's BUILD shows this.
    /// </summary>
    public static string Refusal(PlanApprovalRefused refused) => Refusal(null, refused.Issues);

    private static string Refusal(string? reason, IReadOnlyList<DraftIssue> issues)
    {
        var named = string.Join(", ", issues.Select(i => Issue(i.Kind)).Distinct());
        return (reason, named.Length > 0) switch
        {
            (null, true) => $"REFUSED: {named}",
            (null, false) => "REFUSED",
            (_, true) => $"REFUSED: {reason} ({named})",
            (_, false) => $"REFUSED: {reason}",
        };
    }

    /// <summary>Why a committed plan could not supply something, in the same words a draft uses.</summary>
    public static string Unplannable(UnplannableReason reason) => reason switch
    {
        UnplannableReason.LockedSchematic => "NO UNLOCKED SCHEMATIC MAKES THIS",
        UnplannableReason.NoExecutorOrLine => "NO BUILT FACILITY OR LINE CAN MAKE OR MOVE THIS",
        UnplannableReason.CyclicSchematic => "ITS SCHEMATIC NEEDS ITS OWN OUTPUT",
        _ => $"UNWORDED REASON ({reason.ToString().ToUpperInvariant()})",
    };
}
