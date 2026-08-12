using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// The default vessel is content, and its numbers are claims: that the chain keeps running, that
/// the opening stock lasts, and that the two systems which do not exist yet are visibly absent
/// rather than quietly faked. Each of those is asserted here rather than left to be discovered by
/// watching the console for an hour.
/// </summary>
public class DefaultVesselTests
{
    /// <summary>One operational hour. A tick is a simulated second.</summary>
    private const int AnHour = (int)Units.TicksPerHour;

    [Test]
    public void AfterAnOperationalHour_EveryStageOfTheChainHasProduced()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        // The end of the chain is the honest test of all of it: a drone frame in central storage
        // means raw matter was extracted, hauled, refined, hauled back, pressed, built and
        // assembled, across seven routes and six facilities.
        Assert.That(
            engine.Available(WorldDefinition.CentralStorage, WorldDefinition.Frame),
            Is.GreaterThan(0),
            "nothing finished the factory array in an hour");

        Assert.That(
            engine.Snapshot.Executors
                .Where(e => e.Type != FacilityType.LaunchBay)
                .All(e => e.Status != ExecutorStatus.AllQueuedTasksBlocked),
            Is.True,
            "a facility was stalled an hour in, so the chain is not balanced the way it claims");
    }

    [Test]
    public void TheOpeningStock_OutlastsAnOperationalHour()
    {
        // The extractor is deliberately slower than the reactors it feeds, so the vessel lives off
        // its opening stock and the shortage is a thing that happens later rather than at once.
        // If this fails, the first session ends in a stalled vessel with no way to restock it.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        Assert.That(
            engine.Available(WorldDefinition.CentralStorage, WorldDefinition.Ore),
            Is.GreaterThan(0),
            "central storage ran dry inside an hour");
    }

    [Test]
    public void ALaunchBay_StaysIdle_BecauseAcquisitionDoesNotExist()
    {
        // Not a limitation to work around: a bay reporting anything but idle would be reporting
        // work no system in the game can do.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        foreach (var bay in engine.Snapshot.Executors.Where(e => e.Type == FacilityType.LaunchBay))
        {
            Assert.That(bay.Status, Is.EqualTo(ExecutorStatus.NoTasksQueued), $"'{bay.Id}'");
            Assert.That(bay.Configured, Is.Null, $"'{bay.Id}' is configured for a schematic");
        }
    }

    [Test]
    public void EnergyDraw_ReachesMostOfCapacity_WithoutEverStarvingAFacility()
    {
        // The vessel is tuned to run close to its cap so the energy panel says something, and
        // below it so that nothing is refused. Both halves matter: a vessel that never approaches
        // the cap makes energy a decoration, and one that exceeds it stalls a facility for a
        // reason the player cannot act on until reactors actually make power.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        var energy = engine.Snapshot.Energy;

        Assert.That(energy.Draw, Is.GreaterThan(energy.Capacity * 9 / 10));
        Assert.That(energy.Draw, Is.LessThanOrEqualTo(energy.Capacity));
        Assert.That(energy.StarvedTicks, Is.Zero, "a facility was refused energy");
    }
}
