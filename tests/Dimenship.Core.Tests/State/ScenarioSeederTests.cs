using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.State;

/// <summary>
/// What a new campaign on the shipped vessel starts as.
/// <para>
/// These replace the fidelity tests that compared the content tree to a hand-written
/// <c>CreateDefault()</c>. That comparison had a job while both existed; now the content is the
/// vessel, and what is worth asserting is that seeding it produces the world it describes.
/// </para>
/// </summary>
public class ScenarioSeederTests
{
    [Test]
    public void EveryAuthoredSlot_BecomesAnInstance_InDeclarationOrder()
    {
        var scenario = Shipped.DefaultVessel;
        var state = Shipped.State();

        Assert.That(
            state.Vessel.Storages.Select(s => s.Id).ToList(),
            Is.EqualTo(scenario.Storages.Select(s => s.Id).ToList()));
        Assert.That(
            state.Vessel.Facilities.Select(f => f.Id).ToList(),
            Is.EqualTo(scenario.Facilities.Select(f => f.Id).ToList()));
        Assert.That(
            state.Vessel.Transports.Select(t => t.Id).ToList(),
            Is.EqualTo(scenario.Routes.Select(r => r.Id).ToList()));
        Assert.That(state.Vessel.Sinks, Is.EqualTo(scenario.Sinks));
        Assert.That(state.Vessel.Hold, Is.EqualTo(scenario.Hold));
        Assert.That(state.Vessel.Energy.Capacity, Is.EqualTo(scenario.EnergyCapacity));
    }

    [Test]
    public void OpeningStock_IsCopiedIntoTheInstance_NotReadBackFromContent()
    {
        var state = Shipped.State();
        var hold = state.Vessel.Storages.Single(s => s.Id == DefaultVessel.ResourceStorage);

        Assert.That(
            hold.Stock.Single(s => s.Item == DefaultVessel.MatterMix).Amount,
            Is.EqualTo(3_600_000));
        Assert.That(
            hold.Stock.Single(s => s.Item == DefaultVessel.BasicMetals).Amount,
            Is.EqualTo(40_000));
    }

    [Test]
    public void APassiveFacility_GetsAStandingOrderFromItsConfiguration_WithNoAuthoredTask()
    {
        // The extractor's archetype is not commandable, so no scenario may queue a task on it.
        // What it produces is what it is configured with, and that configuration is its standing
        // order — the read-only source the GDD describes rather than a job nobody ordered. If the
        // seeder forgot this, the vessel would simply stop extracting.
        var scenario = Shipped.DefaultVessel;
        var state = Shipped.State();

        Assert.That(
            scenario.InitialTasks.Any(t => t.Executor == DefaultVessel.Extractor01),
            Is.False,
            "a task queued on a passive facility should not have loaded");

        var extractor = state.Vessel.Facilities.Single(f => f.Id == DefaultVessel.Extractor01);
        var task = state.Tasks.Job(extractor.Queue.Single())!;

        Assert.That(task.SchematicId, Is.EqualTo(DefaultVessel.ExtractHydrogen));
        Assert.That(task.RequestedRuns, Is.Null, "a standing order has no run count");
    }

    [Test]
    public void EveryOpeningTask_IsAStandingOrder()
    {
        var state = Shipped.State();

        Assert.That(state.Tasks.Production.All(t => t.RequestedRuns is null), Is.True);
        Assert.That(state.Tasks.Transport.All(t => t.RequestedQuantity is null), Is.True);
    }

    [Test]
    public void TheUnlockSet_IsTheCampaignsRatherThanTheCatalogs()
    {
        var state = Shipped.State();

        Assert.That(
            state.Progress.UnlockedSchematics,
            Is.EquivalentTo(Shipped.DefaultVessel.UnlockedSchematics));
    }

    [Test]
    public void EveryUpgradePermille_StartsAtOneThousand()
    {
        // Nothing moves these yet. They exist so that when something does, it moves one number on
        // one instance and no schematic, task or run in flight is touched.
        var state = Shipped.State();

        foreach (var facility in state.Vessel.Facilities)
        {
            Assert.That(facility.WorkRatePermille, Is.EqualTo(1000), facility.Id.Value);
            Assert.That(facility.EnergyEfficiencyPermille, Is.EqualTo(1000), facility.Id.Value);
            Assert.That(facility.IntegrityPermille, Is.EqualTo(1000), facility.Id.Value);
            Assert.That(facility.Built, Is.True, facility.Id.Value);
        }
    }

    [Test]
    public void EveryFacilityBuffer_TakesItsCapacityFromTheFacilityThatWorksIt()
    {
        // BufferPermille is on the facility archetype and the buffer's capacity is on the storage
        // archetype, so the two could drift. A change to one that forgot the other shows up here
        // rather than as a buffer that holds the wrong amount.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();

        foreach (var facility in state.Vessel.Facilities)
        {
            var archetype = catalog.Facility(facility.Archetype)!;
            var buffer = state.Vessel.Storages.Single(s => s.Id == facility.LocalStorage);
            var storage = catalog.Storage(buffer.Archetype)!;

            Assert.That(
                storage.CapacityPermille,
                Is.EqualTo(archetype.BufferPermille),
                $"'{facility.Id}' works a {storage.CapacityPermille} permille buffer, and its "
                + $"archetype says {archetype.BufferPermille}");
        }
    }

    [Test]
    public void AWorld_CarriesNoSaveVersionAndNoContentVersion()
    {
        // They describe the file, not the world. Holding them here as well would be holding two
        // answers to one question, and the day they disagreed nothing could say which was right.
        var names = typeof(WorldState).GetProperties().Select(p => p.Name).ToList();

        Assert.That(names, Does.Not.Contain("SaveVersion"));
        Assert.That(names, Does.Not.Contain("ContentVersion"));
        Assert.That(Shipped.State().ScenarioId, Is.EqualTo("default_vessel"));
    }

    [Test]
    public void TheShippedContent_HasAVersion_AndNoReactorsOrStrata()
    {
        Assert.That(Shipped.Catalog.ContentVersion, Is.Not.Empty);
        Assert.That(Shipped.Catalog.Reactors, Is.Empty, "energy is still a constant");
        Assert.That(Shipped.Catalog.Strata, Is.Empty, "acquisition is still not a system");
    }

    [Test]
    public void EveryRegistryThatMintsAnId_CarriesItsOwnCounter()
    {
        // A counter that restarts from zero after a load mints an id that is already in use, which
        // is not a crash and not visible until two entities that were never the same are
        // indistinguishable. Stating it over registries is what makes it hold for the fifth as
        // well as for the first.
        var state = Shipped.State();

        Assert.That(
            state.Tasks.NextTaskId,
            Is.EqualTo(state.Tasks.Production.Count + state.Tasks.Transport.Count));
        Assert.That(state.Plans.NextPlanId, Is.Zero);
        Assert.That(state.Missions.NextMissionId, Is.Zero);
        Assert.That(state.Alerts.NextAlertId, Is.Zero);
        Assert.That(state.Programs.NextInstanceId, Is.Zero);
        Assert.That(state.Robots.NextRobotId, Is.Zero);
    }

    [Test]
    public void EveryRngDomain_GetsAStreamOfItsOwn()
    {
        // Per domain rather than one global generator, so drawing a mission result cannot shift
        // what a later production tie-break returns.
        var state = Shipped.State();
        var domains = Enum.GetValues<RngDomain>().Length;

        Assert.That(state.Random.Streams, Has.Length.EqualTo(domains));
        Assert.That(state.Random.Streams.Distinct().Count(), Is.EqualTo(domains));
    }

    [Test]
    public void ASaveMadeBeforeADomainWasAppended_ExtendsRatherThanFails()
    {
        var older = new RandomState { Streams = new ulong[] { 7 } };

        var extended = older.Extended(seed: 99);

        Assert.That(extended.Streams, Has.Length.EqualTo(Enum.GetValues<RngDomain>().Length));
        Assert.That(extended.Streams[0], Is.EqualTo(7), "an existing domain keeps its generator");
    }

    [Test]
    public void TheClock_StartsPaused_AndAtTickZero()
    {
        var state = Shipped.State();

        Assert.That(state.Clock.Tick, Is.Zero);
        Assert.That(state.Clock.Flow, Is.EqualTo(TimeFlow.Paused));
    }
}
