using Dimenship.Core.Content;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Presentation;

/// <summary>
/// <see cref="BuildQuote"/>: what the inspector says a construction will take before the player
/// commits it (U4). It reads the composer's own draft, so the inspector and Build mode cannot
/// disagree about a cost or a shortage.
/// </summary>
public class BuildQuoteTests
{
    private static readonly ItemId Unit = new("unit");
    private static readonly ItemId Metal = new("metal");
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId FactoryStorage = new("factory_storage");
    private static readonly StorageId SlotStorage = new("slot_storage");
    private static readonly ExecutorId Factory = new("factory");
    private static readonly ExecutorId Slot = new("slot");
    private static readonly SchematicId Assemble = new("assemble_unit");

    /// <summary>
    /// A factory that makes a slot's unit from metal, which nothing aboard produces. The hold
    /// holds <paramref name="metal"/>. An ordinary item travels through the hold, so the unit goes
    /// factory, hold, slot; <paramref name="routed"/> false leaves no line from the hold to the slot.
    /// </summary>
    private static SimulationEngine World(long metal, bool routed = true)
    {
        var builder = new WorldBuilder()
            .Item(Unit)
            .Item(Metal)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Metal, metal))
            .Storage(FactoryStorage)
            .Storage(SlotStorage)
            .Schematic(Assemble, new ItemAmount(Unit, 1000), FacilityType.Factory, effort: 1600,
                inputs: new ItemAmount(Metal, 400))
            .Producer(Factory, FacilityType.Factory, null, storage: FactoryStorage)
            .Producer(Slot, FacilityType.MissionDock, null,
                storage: SlotStorage, builtAtStart: false, constructionUnit: Unit)
            .Transport(new ExecutorId("feed"), Hold, FactoryStorage, throughputPerTick: 50)
            .Transport(new ExecutorId("return"), FactoryStorage, Hold, throughputPerTick: 50);
        if (routed)
        {
            builder.Transport(new ExecutorId("deliver"), Hold, SlotStorage, throughputPerTick: 50);
        }

        return builder.Engine();
    }

    private static BuildQuote Quote(SimulationEngine engine, StorageId slotStorage, ExecutorId slot, ItemId unit) =>
        BuildQuote.For(
            PlanDraftEditor.Create(new ItemAmount(unit, 1000), engine, slotStorage, slot),
            engine.Catalog.Schematics,
            engine.State.Vessel.Hold);

    [Test]
    public void FactoryBeta_IsQuotedAtFactoryAlpha_WithItsMetalsFromTheHold_AndAnEstimate()
    {
        var engine = Shipped.Engine();

        var quote = Quote(engine, DefaultVessel.FactoryBBuffer, DefaultVessel.FactoryB,
            DefaultVessel.FactoryConstructionUnit);

        Assert.That(quote.Slot, Is.EqualTo(DefaultVessel.FactoryB));
        Assert.That(quote.Unit, Is.EqualTo(DefaultVessel.FactoryConstructionUnit));
        Assert.That(quote.MadeAt, Is.EqualTo(DefaultVessel.FactoryA));
        Assert.That(quote.FromHold, Is.EqualTo(new[] { new ItemAmount(DefaultVessel.BasicMetals, 400) }));
        Assert.That(quote.Short, Is.Empty);
        Assert.That(quote.Warnings, Is.Empty);
        Assert.That(quote.Blocking, Is.Empty);
        Assert.That(quote.Committable, Is.True);
        Assert.That(quote.EstimatedTicks, Is.GreaterThan(0));
    }

    [Test]
    public void AShortHold_IsQuotedAsAWait_NotABlock()
    {
        var quote = Quote(World(metal: 100), SlotStorage, Slot, Unit);

        Assert.That(quote.Short.Select(s => s.Item), Is.EqualTo(new[] { Metal }));
        Assert.That(quote.Short.Single().Quantity, Is.GreaterThan(0));
        Assert.That(quote.Committable, Is.True, "a shortage waits; it never blocks approval");
        Assert.That(quote.Warnings, Is.Empty, "a shortage is listed once, as a shortage");
    }

    [Test]
    public void AUnitWithNoWayToItsSlot_IsAWarning_SoTheInspectorCanSaySo()
    {
        // NoExecutorOrLine is not structural, so the draft would commit. A clean BUILD over a plan
        // that can never deliver is the failure this pins.
        var quote = Quote(World(metal: 1000, routed: false), SlotStorage, Slot, Unit);

        Assert.That(quote.Warnings.Select(w => w.Kind), Has.Member(DraftIssueKind.NoExecutorOrLine));
    }

    [Test]
    public void ADraftThatBuildsNothing_IsNotAQuote()
    {
        var engine = Shipped.Engine();
        var draft = PlanDraftEditor.Create(new ItemAmount(DefaultVessel.Component, 1000), engine);

        Assert.Throws<ArgumentException>(() =>
            BuildQuote.For(draft, engine.Catalog.Schematics, engine.State.Vessel.Hold));
    }
}
