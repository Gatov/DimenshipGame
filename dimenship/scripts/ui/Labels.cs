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
}
