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
    /// </summary>
    public static string Issue(DraftIssueKind kind) => kind switch
    {
        DraftIssueKind.IncompatibleExecutor => "INCOMPATIBLE EXECUTOR",
        DraftIssueKind.NoSuchRoute => "NO ROUTE",
        DraftIssueKind.UnknownEndpoint => "UNKNOWN ENDPOINT",
        DraftIssueKind.NonPositiveQuantity => "NON-POSITIVE QUANTITY",
        DraftIssueKind.UnbuiltExecutor => "UNBUILT EXECUTOR",
        DraftIssueKind.NotCommandable => "NOT COMMANDABLE",
        DraftIssueKind.MaterialShortage => "NOT IN HOLD — ACQUIRE",
        DraftIssueKind.WorkpieceNotAccepted => "WORKPIECE NOT ACCEPTED THERE",
        _ => kind.ToString().ToUpperInvariant(),
    };
}
