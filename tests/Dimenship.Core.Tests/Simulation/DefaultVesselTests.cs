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

        // The end of the chain is the honest test of all of it: a robot frame in Resource Storage
        // means Matter Mix was separated by both reactors, hauled back, pressed into components,
        // assembled into modules with Technical Materials, and framed — across eight routes and
        // five facilities.
        Assert.That(
            engine.Available(WorldDefinition.ResourceStorage, WorldDefinition.RobotFrame),
            Is.GreaterThan(0),
            "nothing finished the factory array in an hour");

        Assert.That(
            engine.Snapshot.Executors
                .Where(e => e.Type != FacilityType.MissionDock)
                .All(e => e.Status != ExecutorStatus.AllQueuedTasksBlocked),
            Is.True,
            "a facility was stalled an hour in, so the chain is not balanced the way it claims");
    }

    [Test]
    public void TheOpeningStock_OutlastsAnOperationalHour()
    {
        // Nothing replaces Matter Mix: the extractor gathers hydrogen, and missions do not exist.
        // The vessel lives off its opening stock, and the shortage is a thing that happens later
        // rather than at once. If this fails, the first session ends in a stalled vessel.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        Assert.That(
            engine.Available(WorldDefinition.ResourceStorage, WorldDefinition.MatterMix),
            Is.GreaterThan(0),
            "Resource Storage ran dry of Matter Mix inside an hour");
    }

    [Test]
    public void AMissionDock_StaysIdle_BecauseAcquisitionDoesNotExist()
    {
        // Not a limitation to work around: a dock reporting anything but idle would be reporting
        // work no system in the game can do.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(AnHour);

        foreach (var dock in engine.Snapshot.Executors.Where(e => e.Type == FacilityType.MissionDock))
        {
            Assert.That(dock.Status, Is.EqualTo(ExecutorStatus.NoTasksQueued), $"'{dock.Id}'");
            Assert.That(dock.Configured, Is.Null, $"'{dock.Id}' is configured for a schematic");
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
