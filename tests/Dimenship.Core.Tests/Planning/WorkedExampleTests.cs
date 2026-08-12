using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

/// <summary>
/// The Planning specification's §2 example, end to end. It is the acceptance test for the whole
/// change: goal, arithmetic, transfers and the single shortage all come from the document.
/// </summary>
public class WorkedExampleTests
{
    private static readonly ItemId RawMaterial = new("raw_material");
    private static readonly ItemId Alloy = new("alloy");
    private static readonly ItemId Chips = new("chips");
    private static readonly ItemId Silicon = new("refined_silicon");
    private static readonly ItemId Conductive = new("conductive_material");
    private static readonly ItemId ArmorPlate = new("armor_plate");

    private static readonly StorageId Hold = new("main_hold");
    private static readonly StorageId RefineryBuffer = new("refinery_a_buffer");
    private static readonly StorageId ArmorBuffer = new("armor_factory_buffer");
    private static readonly StorageId FactoryBuffer = new("factory_c_buffer");

    private static readonly SchematicId MakeArmorPlate = new("armor_plate");
    private static readonly SchematicId MakeAlloy = new("alloy");
    private static readonly SchematicId MakeChip = new("chip");

    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId ArmorFactory = new("armor_factory");
    private static readonly ExecutorId FactoryC = new("factory_c");

    // A line runs one route, so the specification's single hauler becomes one line per leg: out
    // to each buffer and back again. The plan this vessel produces is unchanged by the split —
    // which is the point, and what the transfer assertions below check.
    private static readonly ExecutorId FeedRefinery = new("feed_refinery");
    private static readonly ExecutorId ReturnRefinery = new("return_refinery");
    private static readonly ExecutorId FeedArmor = new("feed_armor");
    private static readonly ExecutorId ReturnArmor = new("return_armor");
    private static readonly ExecutorId FeedFactory = new("feed_factory");
    private static readonly ExecutorId ReturnFactory = new("return_factory");

    /// <summary>
    /// The vessel the specification describes: Alloy 5, Chips 10 and Raw Material 19 on hand,
    /// with silicon and conductive material stocked so that raw material is the only shortage.
    /// <para>
    /// Raw material has no schematic. It is a resource that must be acquired, which is exactly
    /// what makes the specification's 41-unit shortage a shortage rather than more production.
    /// </para>
    /// </summary>
    private static SimulationEngine Vessel(long rawOnHand = 19) =>
        new WorldBuilder()
            .Item(RawMaterial)
            .Item(Alloy)
            .Item(Chips)
            .Item(Silicon)
            .Item(Conductive)
            .Item(ArmorPlate)
            .Storage(
                Hold, StorageArchetype.FullHold,
                new ItemAmount(Alloy, 5),
                new ItemAmount(Chips, 10),
                new ItemAmount(RawMaterial, rawOnHand),
                new ItemAmount(Silicon, 20),
                new ItemAmount(Conductive, 20))
            .Storage(RefineryBuffer, 100)
            .Storage(ArmorBuffer, 100)
            .Storage(FactoryBuffer, 100)
            .Schematic(MakeArmorPlate, new ItemAmount(ArmorPlate, 4), FacilityType.Factory,
                inputs: new[] { new ItemAmount(Alloy, 25), new ItemAmount(Chips, 15) })
            .Schematic(MakeAlloy, new ItemAmount(Alloy, 5), FacilityType.MatterReactor,
                inputs: new ItemAmount(RawMaterial, 15))
            .Schematic(MakeChip, new ItemAmount(Chips, 5), FacilityType.Factory,
                inputs: new[] { new ItemAmount(Silicon, 5), new ItemAmount(Conductive, 5) })
            .Producer(RefineryA, FacilityType.MatterReactor, MakeAlloy, storage: RefineryBuffer)
            // Listed before factory_c, so the goal's own run claims it and the chip branch, which
            // is expanded second, spreads onto the other factory.
            .Producer(ArmorFactory, FacilityType.Factory, MakeArmorPlate, storage: ArmorBuffer)
            .Producer(FactoryC, FacilityType.Factory, MakeChip, storage: FactoryBuffer)
            .Transport(FeedRefinery, Hold, RefineryBuffer, 1_000)
            .Transport(ReturnRefinery, RefineryBuffer, Hold, 1_000)
            .Transport(FeedArmor, Hold, ArmorBuffer, 1_000)
            .Transport(ReturnArmor, ArmorBuffer, Hold, 1_000)
            .Transport(FeedFactory, Hold, FactoryBuffer, 1_000)
            .Transport(ReturnFactory, FactoryBuffer, Hold, 1_000)
            .Engine();

    private static ProductionPlan PlanFourArmorPlates(SimulationEngine engine) =>
        ProductionPlanner.Plan(new ItemAmount(ArmorPlate, 4), engine);

    [Test]
    public void FourArmorPlates_ProduceTheAdditionalQuantitiesTheSpecificationGives()
    {
        var plan = PlanFourArmorPlates(Vessel());

        // 25 alloy required against 5 on hand is 20 more, which is four runs of five.
        // 15 chips required against 10 on hand is 5 more, which is one run of five.
        Assert.That(
            plan.Runs.Select(r => $"{r.Schematic}|{r.Executor}|{r.Runs}").ToList(),
            Is.EqualTo(new List<string>
            {
                "alloy|refinery_a|4",
                "chip|factory_c|1",
                "armor_plate|armor_factory|1",
            }),
            "deepest branch first, the goal's own run last");
    }

    [Test]
    public void FourArmorPlates_LeaveExactlyOneShortage_Of41RawMaterial()
    {
        var plan = PlanFourArmorPlates(Vessel());

        Assert.That(plan.Shortages, Has.Count.EqualTo(1));
        var shortage = plan.Shortages[0];
        Assert.That(shortage.Item, Is.EqualTo(RawMaterial));
        Assert.That(shortage.Missing, Is.EqualTo(41), "60 required for four alloy runs, 19 on hand");
        Assert.That(shortage.Kind, Is.EqualTo(ShortageKind.RawResource));
        Assert.That(plan.NeedsAcquisition, Is.True, "which is what suggests an expedition");
        Assert.That(plan.IsComplete, Is.False);
    }

    [Test]
    public void FourArmorPlates_IncludeEveryTransferTheSpecificationNames()
    {
        // The specification's list is illustrative rather than exhaustive — it omits the chip
        // branch's legs entirely. These four must be present; the count is deliberately not
        // asserted, because doing so would encode the document's abbreviation as a requirement.
        var plan = PlanFourArmorPlates(Vessel());

        var transfers = plan.Transfers
            .Select(t => $"{t.Quantity} {t.Item}: {t.From} -> {t.To}")
            .ToList();

        Assert.That(transfers, Does.Contain("60 raw_material: main_hold -> refinery_a_buffer"));
        Assert.That(transfers, Does.Contain("20 alloy: refinery_a_buffer -> main_hold"));
        Assert.That(transfers, Does.Contain("25 alloy: main_hold -> armor_factory_buffer"));
        Assert.That(transfers, Does.Contain("15 chips: main_hold -> armor_factory_buffer"));
    }

    [Test]
    public void FourArmorPlates_AlsoRouteTheLegsTheSpecificationOmits()
    {
        // A plan that moved the chips' inputs nowhere and left the finished chips in factory C
        // would not actually work, however faithfully it matched the document's four lines.
        var plan = PlanFourArmorPlates(Vessel());

        var transfers = plan.Transfers
            .Select(t => $"{t.Quantity} {t.Item}: {t.From} -> {t.To}")
            .ToList();

        Assert.That(transfers, Does.Contain("5 refined_silicon: main_hold -> factory_c_buffer"));
        Assert.That(transfers, Does.Contain("5 conductive_material: main_hold -> factory_c_buffer"));
        Assert.That(transfers, Does.Contain("5 chips: factory_c_buffer -> main_hold"));
        Assert.That(transfers, Does.Contain("4 armor_plate: armor_factory_buffer -> main_hold"));
    }

    [Test]
    public void APlanWithAShortage_StillCommits_AndTheAvailablePortionBegins()
    {
        // "The plan may be accepted even when it cannot currently be completed." The nineteen
        // raw material on hand is one alloy run's worth, and that run must not wait for the
        // other forty-one to arrive.
        var engine = Vessel();
        var plan = PlanFourArmorPlates(engine);

        var created = engine.Commit(plan);

        Assert.That(created, Is.Not.Empty);
        engine.Advance(50);

        Assert.That(
            engine.Snapshot.Resources.Single(r => r.Id == Alloy).Amount,
            Is.GreaterThan(5),
            "the alloy the vessel could make was made while the shortage stood");
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PlanShortage),
            Is.EqualTo(1));
    }

    [Test]
    public void WithTheMissingRawMaterialAboard_ThePlanRunsToCompletion()
    {
        // The same goal on a vessel that has the 60 raw material: no shortage, and four armour
        // plates actually exist in the hold when the queues drain.
        var engine = Vessel(rawOnHand: 60);
        var plan = PlanFourArmorPlates(engine);

        Assert.That(plan.IsComplete, Is.True, "nothing is missing this time");

        engine.Commit(plan);
        engine.Advance(200);

        Assert.That(
            engine.Available(Hold, ArmorPlate),
            Is.EqualTo(4),
            "planned, hauled, produced, and hauled back — the whole chain agreed with the plan");
    }
}
