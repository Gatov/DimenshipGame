using Dimenship.Core.Content;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// Where an item sits in the vessel's chain, for a surface that lists every item and wants the
/// list to read as a chain rather than as one undifferentiated row. Declared in the order a
/// surface shows them: what the vessel starts from, what it makes, what it makes on the way, and
/// what it builds itself with.
/// </summary>
public enum ItemTier
{
    /// <summary>Made by no schematic that consumes anything: recovered, or extracted from nothing.</summary>
    Raw,

    /// <summary>Made from other items, and stored like any good.</summary>
    Processed,

    /// <summary>An intermediate that is never in Resource Storage (<see cref="ItemDefinition.Workpiece"/>).</summary>
    Workpiece,

    /// <summary>Named by a facility archetype as the unit that commissions it.</summary>
    ConstructionUnit,
}

/// <summary>One tier and its items, in catalog declaration order.</summary>
public sealed record ItemTierGroup(ItemTier Tier, IReadOnlyList<ItemId> Items);

/// <summary>
/// Sorts the catalog's items into <see cref="ItemTier"/>s, derived from the catalog and never
/// authored. An authored tier would be one more field per item that can disagree with the
/// schematics it summarises, and the shell would be naming shipped ids to group them otherwise,
/// which it is not allowed to (R7). The rules are checked in tier order from the most specific:
/// a construction unit is processed too, and a workpiece is made from something, but the narrower
/// answer is the one a reader needs.
/// <para>
/// A read projection over the catalog, like <see cref="StockLocations"/> is over a snapshot. It
/// reorders nothing within a tier: declaration order is the determinism contract, and the order an
/// author wrote items in is the order a surface lists them.
/// </para>
/// </summary>
public static class ItemTiers
{
    public static ItemTier Of(ContentCatalog catalog, ItemDefinition item)
    {
        foreach (var facility in catalog.Facilities)
        {
            if (facility.ConstructionUnit == item.Id)
            {
                return ItemTier.ConstructionUnit;
            }
        }

        if (item.Workpiece)
        {
            return ItemTier.Workpiece;
        }

        // An extraction schematic has no inputs and makes its output from nothing aboard, so
        // Hydrogen is as raw as Matter Mix even though a schematic names it.
        foreach (var schematic in catalog.Schematics.ForOutput(item.Id))
        {
            if (schematic.Inputs.Count > 0)
            {
                return ItemTier.Processed;
            }
        }

        return ItemTier.Raw;
    }

    /// <summary>Every non-empty tier, in <see cref="ItemTier"/> order, holding every catalog item exactly once.</summary>
    public static IReadOnlyList<ItemTierGroup> Groups(ContentCatalog catalog)
    {
        var byTier = new Dictionary<ItemTier, List<ItemId>>();
        foreach (var item in catalog.Items)
        {
            var tier = Of(catalog, item);
            if (!byTier.TryGetValue(tier, out var items))
            {
                items = new List<ItemId>();
                byTier[tier] = items;
            }

            items.Add(item.Id);
        }

        var groups = new List<ItemTierGroup>();
        foreach (var tier in Enum.GetValues<ItemTier>())
        {
            if (byTier.TryGetValue(tier, out var items))
            {
                groups.Add(new ItemTierGroup(tier, items));
            }
        }

        return groups;
    }
}
