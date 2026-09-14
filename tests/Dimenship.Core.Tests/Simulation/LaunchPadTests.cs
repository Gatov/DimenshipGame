using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The vessel's first plan, end to end on the content the game ships. Every other planner and
/// commissioning test builds its own world, which is exactly how the shipped vessel came to hold a
/// construction schematic nobody had unlocked and a construction unit that filled a facility buffer
/// to the millilitre — both invisible to a world a builder made.
/// </summary>
public class LaunchPadTests
{
    /// <summary>Long enough for the whole plan with the slowest leg at 50 a tick, and no longer.</summary>
    private const int LongEnough = 300;

    private static ProductionPlan PlanLaunchPad(SimulationEngine engine) =>
        ProductionPlanner.Plan(
            new ItemAmount(DefaultVessel.MissionDockConstructionUnit, 1_000), engine,
            destination: DefaultVessel.DockAHold);

    [Test]
    public void TheShippedVessel_CanPlanItsFirstLaunchPad()
    {
        var engine = Shipped.Engine();

        var plan = PlanLaunchPad(engine);

        // Before assemble_dock_unit was unlocked this came back as one LockedSchematic entry with
        // no runs and no transfers: the headline plan could not be composed at all.
        Assert.That(plan.Unplannable, Is.Empty, "the first plan should not be missing anything");
        Assert.That(
            plan.Runs().Select(r => r.Schematic).ToList(),
            Is.EqualTo(new[] { DefaultVessel.AssembleDockUnit }),
            "one factory run, because the unit's only input is Basic Metals");
        Assert.That(plan.Runs().Single().Executor, Is.EqualTo(DefaultVessel.FactoryA));
        Assert.That(plan.Transfers(), Is.Not.Empty);

        // Destination appends one final transfer, hold to the pad's own hold, for the goal amount.
        var delivery = plan.Transfers().Single(t => t.To == DefaultVessel.DockAHold);
        Assert.That(delivery.Item, Is.EqualTo(DefaultVessel.MissionDockConstructionUnit));
        Assert.That(delivery.Quantity, Is.EqualTo(1_000));
        Assert.That(delivery.Executor, Is.EqualTo(DefaultVessel.DockASupply));
    }

    /// <summary>
    /// A whole unit is 1,000 milli-units and a facility buffer is 25 permille of a hold, so the
    /// item's hold capacity is what decides how much of a buffer one unit is. This pins it at half.
    /// </summary>
    [Test]
    public void AWholeConstructionUnit_IsHalfAFacilityBuffer()
    {
        var engine = Shipped.Engine();

        Assert.That(
            engine.Room(DefaultVessel.FactoryABuffer, DefaultVessel.MissionDockConstructionUnit),
            Is.EqualTo(2_000));
    }

    /// <summary>
    /// A facility's buffer holds back one run's output at the schematic it is set up for, and
    /// transport subtracts that reservation. When a whole unit filled the buffer, the reservation
    /// was the whole buffer: the factory making the unit reported zero room for every item,
    /// including the Basic Metals its next run needs, and <c>Configured</c> never clears — so the
    /// factory could not be fed, could not be re-tasked, and stayed that way.
    /// </summary>
    [Test]
    public void AFactoryConfiguredForTheDockUnit_StillHasRoomForItsOwnInputs()
    {
        var engine = Shipped.Engine();
        engine.State.Vessel.Facilities.Single(f => f.Id == DefaultVessel.FactoryA).Configured =
            DefaultVessel.AssembleDockUnit;

        Assert.That(
            engine.RoomForDelivery(DefaultVessel.FactoryABuffer, DefaultVessel.BasicMetals),
            Is.GreaterThanOrEqualTo(400),
            "the reservation must leave room for the inputs of the run it is reserving for");
    }

    /// <summary>
    /// Every slot the vessel ships unbuilt, read off the state rather than listed, so a slot added
    /// in content is covered without anyone remembering to add it here.
    /// <para>
    /// All four construction schematics are the same one-run recipe from opening stock, and every
    /// slot's local storage has a hold-star line at least as fast as a factory feed, so each Build
    /// plan should estimate exactly what the first Launch Pad's does. Factory Beta is why that is
    /// asserted: two lines feed its buffer, and the planner used to hand its unit to the 7-a-tick
    /// Technical Materials feed rather than the 50-a-tick one beside it — 145 ticks against 23,
    /// commissioning at 213 against 90, and inside <see cref="LongEnough"/> all the same, which is
    /// why commissioning alone would not have caught it.
    /// </para>
    /// </summary>
    [Test]
    public void EveryShippedUnbuiltSlot_CommissionsFromAQuietVessel_AsFastAsTheFirstLaunchPad()
    {
        var launchPadEstimate = PlanLaunchPad(Shipped.Engine()).EstimatedTicks;
        var slots = Shipped.State().Vessel.Facilities.Where(f => !f.Built).Select(f => f.Id).ToList();

        Assert.That(slots, Has.Count.EqualTo(5), "the thin opening build ships five slots unbuilt");

        foreach (var slot in slots)
        {
            var engine = Shipped.Engine();
            var facility = engine.State.Vessel.Facilities.Single(f => f.Id == slot);
            var unit = engine.Catalog.Facility(facility.Archetype)!.ConstructionUnit;
            Assert.That(unit, Is.Not.Null, $"'{slot}' ships unbuilt with nothing that commissions it");

            var plan = ProductionPlanner.Plan(
                new ItemAmount(unit!.Value, 1_000), engine, destination: facility.LocalStorage);

            Assert.That(plan.Unplannable, Is.Empty, $"'{slot}'");
            Assert.That(plan.EstimatedTicks, Is.EqualTo(launchPadEstimate), $"'{slot}'");

            engine.Commit(plan);
            engine.Advance(LongEnough);

            Assert.That(facility.Built, Is.True, $"'{slot}' did not commission");
        }
    }

    [Test]
    public void TheFirstLaunchPad_CommissionsFromAQuietVessel()
    {
        var engine = Shipped.Engine();

        engine.Commit(PlanLaunchPad(engine));

        var dock = engine.State.Vessel.Facilities.Single(f => f.Id == DefaultVessel.DockA);
        Assert.That(dock.Built, Is.False, "the pad is authored unbuilt");

        engine.Advance(LongEnough);

        Assert.That(
            dock.Built,
            Is.True,
            "the plan ran to commissioning without stalling on a full buffer");
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e =>
                e.Code == EventCode.FacilityBuilt && e.Subject == DefaultVessel.DockA.Value),
            Is.True);
        Assert.That(
            engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.PlanCompleted),
            Is.True,
            "the last spawned task — the pad's own delivery — retiring finishes the plan");
    }
}
