using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The default vessel is content, and its numbers are claims: that it opens quiet, that the
/// opening stock lasts, that standing draw fits under the cap, and that the systems which do not
/// exist yet are visibly absent rather than quietly faked.
/// </summary>
public class DefaultVesselTests
{
    /// <summary>One operational hour. A tick is a simulated second.</summary>
    private const int AnHour = (int)Units.TicksPerHour;

    [Test]
    public void TheVesselOpensQuiet_ExceptForTheExtractor()
    {
        var engine = Shipped.Engine();

        engine.Advance(AnHour);

        Assert.That(
            engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.RobotFrame),
            Is.Zero,
            "nobody ordered frames, so none should appear");

        foreach (var executor in engine.Snapshot.Executors.Where(e => e.Id != DefaultVessel.Extractor01))
        {
            Assert.That(
                executor.Status, Is.EqualTo(ExecutorStatus.NoTasksQueued),
                $"'{executor.Id}' should be idle until a plan queues work");
        }

        Assert.That(
            engine.Snapshot.Executors.Single(e => e.Id == DefaultVessel.Extractor01).Status,
            Is.EqualTo(ExecutorStatus.RunningTask));
        Assert.That(
            engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.Hydrogen),
            Is.GreaterThan(0),
            "the extractor is the one standing order that remains");
    }

    [Test]
    public void TheOpeningStock_OutlastsAnOperationalHour()
    {
        // Nothing replaces Matter Mix: the extractor gathers hydrogen, and missions do not exist.
        // With the chain quiet the stock is not being spent, and that is the point of a quiet open.
        var engine = Shipped.Engine();

        engine.Advance(AnHour);

        Assert.That(
            engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.MatterMix),
            Is.EqualTo(3_600_000),
            "quiet opening must not spend Matter Mix");
    }

    [Test]
    public void AuthoredUnbuiltSlots_StayUnbuiltAndDrawNothing()
    {
        var engine = Shipped.Engine();
        var state = Shipped.State();
        var unbuilt = new[]
        {
            DefaultVessel.ReactorB, DefaultVessel.FactoryB, DefaultVessel.FactoryC,
            DefaultVessel.DockA, DefaultVessel.DockB,
        };

        engine.Advance(AnHour);

        foreach (var id in unbuilt)
        {
            Assert.That(
                state.Vessel.Facilities.Single(f => f.Id == id).Built, Is.False, $"'{id}'");
        }

        foreach (var executor in engine.Snapshot.Executors.Where(e => unbuilt.Contains(e.Id)))
        {
            Assert.That(executor.Status, Is.EqualTo(ExecutorStatus.NoTasksQueued), $"'{executor.Id}'");
            Assert.That(executor.PowerDraw, Is.Zero, $"unbuilt '{executor.Id}' must draw nothing");
        }

        foreach (var dock in engine.Snapshot.Executors.Where(e => e.Type == FacilityType.MissionDock))
        {
            Assert.That(dock.Configured, Is.Null, $"'{dock.Id}' is configured for a schematic");
        }
    }

    [Test]
    public void FactoryAndReactorArchetypes_NameConstructionUnits()
    {
        var catalog = Shipped.Catalog;

        Assert.That(
            catalog.Facility(new FacilityArchetypeId("factory"))!.ConstructionUnit,
            Is.EqualTo(DefaultVessel.FactoryConstructionUnit));
        Assert.That(
            catalog.Facility(new FacilityArchetypeId("matter_reactor"))!.ConstructionUnit,
            Is.EqualTo(DefaultVessel.MatterReactorConstructionUnit));
        Assert.That(
            catalog.Facility(new FacilityArchetypeId("hydrogen_extractor"))!.ConstructionUnit,
            Is.Null,
            "the extractor opens built and is never commissioned this way");
    }

    [Test]
    public void StandingDraw_FitsUnderCapacity_AndNeverStarves()
    {
        // Quiet opening: standing draw only. CapHits under an approved plan are a later check.
        var engine = Shipped.Engine();

        engine.Advance(AnHour);

        var energy = engine.Snapshot.Energy;

        Assert.That(energy.Draw, Is.LessThanOrEqualTo(energy.Capacity));
        Assert.That(energy.Draw, Is.GreaterThan(energy.Capacity * 7 / 10),
            "standing draw should still be the bulk of the budget");
        Assert.That(energy.StarvedTicks, Is.Zero, "a facility was refused energy");
        Assert.That(energy.CapHits, Is.Zero);
    }

    [Test]
    public void InterconnectsAreUnbuilt_AndHoldStarLegsAreBuilt()
    {
        var state = Shipped.State();

        Assert.That(
            state.Vessel.Transports.Single(t => t.Id == DefaultVessel.FactoryLinkAb).Built,
            Is.False);
        Assert.That(
            state.Vessel.Transports.Single(t => t.Id == DefaultVessel.FactoryLinkBc).Built,
            Is.False);

        foreach (var id in new[]
                 {
                     DefaultVessel.FactoryAFeed, DefaultVessel.FactoryAReturn,
                     DefaultVessel.FactoryBFeed, DefaultVessel.FactoryBFeedComponents,
                     DefaultVessel.FactoryBReturn, DefaultVessel.FactoryCFeedModules,
                     DefaultVessel.FactoryCReturn,
                     DefaultVessel.DockASupply, DefaultVessel.DockAReturn,
                     DefaultVessel.DockBSupply, DefaultVessel.DockBReturn,
                 })
        {
            Assert.That(
                state.Vessel.Transports.Single(t => t.Id == id).Built, Is.True, id.Value);
        }
    }
}
