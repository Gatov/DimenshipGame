using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// The resource strip's grouping, derived from the catalog alone, on the shipped vessel.
/// </summary>
public class ItemTiersTests
{
    private static IReadOnlyList<string> Tier(ItemTier tier) =>
        ItemTiers.Groups(Shipped.Catalog)
            .Single(group => group.Tier == tier)
            .Items.Select(item => item.Value)
            .ToList();

    [Test]
    public void RawMaterials_AreTheOnesNothingIsConsumedToMake_ExtractionIncluded()
    {
        Assert.That(Tier(ItemTier.Raw), Is.EqualTo(new[] { "matter_mix", "hydrogen" }));
    }

    [Test]
    public void ProcessedGoods_KeepCatalogOrder()
    {
        Assert.That(
            Tier(ItemTier.Processed),
            Is.EqualTo(new[]
            {
                "basic_metals", "technical_materials", "component", "module", "robot_frame", "bulkhead",
            }));
    }

    [Test]
    public void Workpieces_AreTheFlaggedItems()
    {
        Assert.That(Tier(ItemTier.Workpiece), Is.EqualTo(new[] { "plate_blank", "hardened_blank" }));
    }

    [Test]
    public void ConstructionUnits_AreTheOnesAFacilityArchetypeNames_AlthoughAFactoryMakesThem()
    {
        Assert.That(
            Tier(ItemTier.ConstructionUnit),
            Is.EqualTo(new[]
            {
                "mission_dock_construction_unit",
                "matter_reactor_construction_unit",
                "factory_construction_unit",
            }));
    }

    [Test]
    public void EveryItem_IsInExactlyOneGroup_AndGroupsFollowTierOrder()
    {
        var groups = ItemTiers.Groups(Shipped.Catalog);

        Assert.That(
            groups.SelectMany(group => group.Items),
            Is.EquivalentTo(Shipped.Catalog.Items.Select(item => item.Id)));
        Assert.That(groups.Select(group => group.Tier), Is.Ordered);
    }
}
