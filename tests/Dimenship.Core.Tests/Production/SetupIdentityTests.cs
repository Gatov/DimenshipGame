using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// Setup identity is the schematic (D1, Decision 1:
/// <c>2026-09-24-setup-identity-and-interruption-boundary-design.md</c>). A changeover is due
/// exactly when the next run's schematic differs from the one the facility is configured for — not
/// when the order changes, and not when only the output item stays the same.
/// </summary>
public class SetupIdentityTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly ItemId Chip = WorldBuilder.Chip;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly SchematicId Synthesize = new("synthesize");
    private static readonly ExecutorId Reactor = new("reactor");

    [Test]
    public void TwoSeparateTasks_OnOneSchematic_RunBackToBack_WithNoSwitchOver()
    {
        // Two orders, not one order of two runs: the existing single-task test cannot tell a
        // schematic-keyed setup from an order-keyed one, and the design forbids charging merely for
        // a different order id.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null, switchOverTicks: 30)
            .Task(Smelt, 2, Reactor)
            .Task(Smelt, 2, Reactor)
            .Engine();

        engine.Advance(4);

        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(4), "four runs in four ticks across two orders");
        Assert.That(engine.State.Tasks.All.Count(t => t.IsFinished), Is.EqualTo(2), "both orders finished");
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.SwitchOverStarted), Is.False,
            "a second order on the configured schematic paid for a changeover");
    }

    [Test]
    public void TwoSchematics_WithTheSameOutput_CostAFullSwitchOver()
    {
        // The shipped reactor makes basic metals from matter mix or from hydrogen, at a fourfold
        // difference in energy. Those are two processes; letting the shared output id make the move
        // free would erase the choice the two recipes exist to offer.
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Item(Chip)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000), new ItemAmount(Chip, 1_000))
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Schematic(Synthesize, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Chip, 10))
            .Producer(Reactor, FacilityType.MatterReactor, null, switchOverTicks: 30)
            .Task(Smelt, 1, Reactor)
            .Task(Synthesize, 1, Reactor)
            .Engine();

        engine.Advance(1);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1));

        engine.Advance(30);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(1), "the second process ran inside its changeover");
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.SwitchOverStarted), Is.EqualTo(1));

        engine.Advance(1);
        Assert.That(engine.Available(Hold, Alloy), Is.EqualTo(2), "the run starts the tick after the changeover");
    }

    [Test]
    public void TheShippedReactor_HasTwoProcesses_ForOneOutput()
    {
        // The property the test above models, pinned on the content it is about. If the shipped
        // catalog ever stops having two recipes for basic metals, the model test stops describing
        // the vessel.
        var producers = Shipped.Catalog.Schematics.ForOutput(DefaultVessel.BasicMetals)
            .Where(s => s.RequiredFacilityType == FacilityType.MatterReactor)
            .Select(s => s.Id.Value)
            .ToList();

        Assert.That(producers, Is.SupersetOf(new[] { "separate_basic", "synthesize_basic" }));
    }
}
