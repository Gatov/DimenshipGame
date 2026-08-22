using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.State;

/// <summary>
/// The five tests the save format exists to satisfy. They are the acceptance criteria for the
/// whole migration, not just for this stage: each of the three stages before it is only correct
/// if these pass, and each of the stages after it has to keep them passing.
/// </summary>
public class WorldSaveTests
{
    private static IReadOnlyList<Core.Content.Scenario> Scenarios => new[] { Shipped.DefaultVessel };

    private static WorldState Load(string json, ContentCatalog catalog)
    {
        var result = WorldSave.Read(json, catalog, Scenarios);

        Assert.That(
            result.Errors.Select(e => e.ToString()).ToList(),
            Is.Empty,
            "the save did not load");

        return result.State!;
    }

    /// <summary>
    /// A vessel run far enough to have something in every interesting condition: a run in progress,
    /// a postponed task, a switch-over under way, a partially-moved transfer and a committed plan.
    /// A round-trip that only ever saw an idle world would prove very little.
    /// </summary>
    private static SimulationEngine Busy(long ticks = 300)
    {
        var engine = Shipped.Engine();
        engine.Advance(ticks);

        // A dock haul for modules, which never reach Resource Storage: the line postpones for want
        // of source material and keeps saying so. A facility will not do for this — a reactor with
        // a standing order always has something else it can run, which is the behaviour, not a
        // problem with the fixture.
        engine.EnqueueTransfer(
            DefaultVessel.Module, 10, DefaultVessel.ResourceStorage, DefaultVessel.DockAHold,
            DefaultVessel.DockASupply);

        engine.Commit(ProductionPlanner.Plan(new ItemAmount(DefaultVessel.Component, 40), engine));
        engine.Advance(5);
        return engine;
    }

    /// <summary>
    /// A canonical rendering of everything the shell can see. Used to compare two snapshots, which
    /// record equality cannot do: the record holds lists, and a list compares by reference.
    /// </summary>
    private static string Describe(WorldSnapshot snapshot)
    {
        var lines = new List<string> { $"tick={snapshot.Tick}", $"events={snapshot.TotalEventsEmitted}" };

        lines.AddRange(snapshot.Resources.Select(r =>
            $"resource {r.Id} {r.Amount}/{r.Capacity} {r.NetRatePerTick}"));

        lines.AddRange(snapshot.Storages.Select(s =>
            $"storage {s.Id} '{s.Label}' {s.FillPermille} "
            + string.Join(",", s.Items.Select(i => $"{i.Id}:{i.Amount}/{i.Capacity}"))));

        lines.Add($"energy {snapshot.Energy.Capacity} {snapshot.Energy.Draw} {snapshot.Energy.Reserve} "
            + $"{snapshot.Energy.CapHits} {snapshot.Energy.StarvedTicks}");

        lines.AddRange(snapshot.Executors.Select(e =>
            $"executor {e.Id} '{e.Label}' {e.Type} {e.LocalStorage} {e.Status} {e.Configured} "
            + $"{e.CurrentTask} {e.PowerDraw} {e.RunTicksRemaining}/{e.RunTicksTotal} "
            + $"{e.SwitchOverTicksRemaining} {e.BlockReason}"));

        lines.AddRange(snapshot.Transports.Select(t =>
            $"line {t.Id} '{t.Label}' {t.From}->{t.To} {t.Status} {t.CurrentTask} {t.CarriedItem} "
            + $"{t.ThroughputPerTick} {t.MovedLastTick} {t.PowerDraw} {t.BlockReason}"));

        lines.AddRange(snapshot.Sinks.Select(s => $"sink {s.Id} '{s.Label}' {s.PowerDraw}"));

        lines.AddRange(snapshot.ProductionTasks.Select(t =>
            $"task {t.Id} {t.Schematic} {t.Executor} {t.CompletedRuns}/{t.RequestedRuns} "
            + $"{t.State} {t.LastReason} {t.PostponedAtTick}"));

        lines.AddRange(snapshot.TransportTasks.Select(t =>
            $"transfer {t.Id} {t.Item} {t.Executor} {t.Source}->{t.Destination} "
            + $"{t.MovedQuantity}/{t.RequestedQuantity} {t.State} {t.LastReason} {t.PostponedAtTick}"));

        lines.AddRange(snapshot.RecentEvents.Select(e =>
            $"event {e.Tick}|{e.Category}|{e.Code}|{e.Subject}|"
            + string.Join(",", e.Data.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"))));

        return string.Join("\n", lines);
    }

    [Test]
    public void RoundTrip_ReproducesTheWorldExactly()
    {
        var engine = Busy();
        var catalog = Shipped.Catalog;

        // Every condition the round-trip is supposed to survive, asserted to actually be present:
        // a fixture that quietly stopped exercising one of them would still pass.
        Assert.That(
            engine.State.Tasks.Production.Any(t => t.RunActive),
            Is.True,
            "no run is in progress, so the round-trip proves less than it claims");
        Assert.That(
            engine.State.Tasks.Production.Any(t => t.State == TaskState.Postponed)
            || engine.State.Tasks.Transport.Any(t => t.State == TaskState.Postponed),
            Is.True,
            "nothing is postponed");
        Assert.That(
            engine.State.Tasks.Transport.Any(t => t.MovedQuantity > 0),
            Is.True,
            "no transfer is part-moved");
        Assert.That(engine.State.Plans.Plans, Is.Not.Empty, "no plan was committed");

        var written = WorldSave.Write(catalog, engine.State);
        var reloaded = Load(written, catalog);

        // Deep equality, by the only definition that is meaningful for a save: the world writes
        // the same bytes the second time. Canonical output is what makes this a real comparison —
        // sets are sorted and every ordered collection is an array.
        Assert.That(WorldSave.Write(catalog, reloaded), Is.EqualTo(written));

        // And the same world to anything reading it, not merely the same file.
        Assert.That(
            Describe(new SimulationEngine(catalog, reloaded).Snapshot),
            Is.EqualTo(Describe(engine.Snapshot)));
    }

    [Test]
    public void RoundTrip_KeepsASwitchOverUnderWay()
    {
        // The one condition the shipped vessel cannot be made to hold: every facility on it runs a
        // standing order, so its current task can always continue and it never has to reconfigure.
        // A reconfiguration in flight is real state — ticks elapsed, and the task being switched to
        // — and a save that dropped it would resume a facility set up for the wrong schematic.
        var reactor = new ExecutorId("reactor");
        var smelt = new SchematicId("smelt");
        var forge = new SchematicId("forge");

        var builder = new WorldBuilder()
            .Item(WorldBuilder.Ore)
            .Item(WorldBuilder.Alloy)
            .Item(WorldBuilder.Chip)
            .Storage(WorldBuilder.Hold, StorageArchetype.FullHold, new ItemAmount(WorldBuilder.Ore, 1_000))
            .Schematic(smelt, new ItemAmount(WorldBuilder.Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(WorldBuilder.Ore, 1))
            .Schematic(forge, new ItemAmount(WorldBuilder.Chip, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(WorldBuilder.Ore, 1))
            .Producer(reactor, FacilityType.MatterReactor, smelt, switchOverTicks: 30)
            .Task(smelt, 1, reactor)
            .Task(forge, 5, reactor);

        var catalog = builder.Catalog();
        var engine = builder.Engine();
        engine.Advance(5);

        var switching = engine.State.Vessel.Facilities.Single();
        Assert.That(switching.SwitchOverRemaining, Is.GreaterThan(0), "nothing is reconfiguring");
        Assert.That(switching.SwitchTarget, Is.Not.Null);

        var written = WorldSave.Write(catalog, engine.State);
        var result = WorldSave.Read(written, catalog, new[] { builder.Scenario() });

        Assert.That(result.Errors.Select(e => e.ToString()).ToList(), Is.Empty);
        Assert.That(WorldSave.Write(catalog, result.State!), Is.EqualTo(written));

        var resumed = new SimulationEngine(catalog, result.State!);
        resumed.Advance(40);
        engine.Advance(40);

        Assert.That(Describe(resumed.Snapshot), Is.EqualTo(Describe(engine.Snapshot)));
        Assert.That(
            resumed.State.Vessel.Facilities.Single().Configured,
            Is.EqualTo(forge),
            "the reconfiguration did not finish on the far side of the save");
    }

    [Test]
    public void DeterminismSurvivesASave_WhichIsWhatCatchesAFieldLivingOnlyInTheEngine()
    {
        // Five hundred ticks, a save, a load, five hundred more — against a straight thousand. Any
        // value the engine kept to itself rather than putting in the world diverges here, and the
        // divergence is a behaviour change rather than a cosmetic one.
        var catalog = Shipped.Catalog;

        var interrupted = Shipped.Engine();
        interrupted.Advance(500);
        var resumed = new SimulationEngine(catalog, Load(WorldSave.Write(catalog, interrupted.State), catalog));
        resumed.Advance(500);

        var straight = Shipped.Engine();
        straight.Advance(1_000);

        Assert.That(Describe(resumed.Snapshot), Is.EqualTo(Describe(straight.Snapshot)));
        Assert.That(
            WorldSave.Write(catalog, resumed.State),
            Is.EqualTo(WorldSave.Write(catalog, straight.State)),
            "the two worlds differ somewhere the snapshot does not show");
    }

    [Test]
    public void ContentStillReachesAnOldSave()
    {
        // The test whose absence let a capacity and three labels into the first draft of the state
        // tree. A value the catalog can answer must not be copied into a save, or rebalancing the
        // game would never reach anyone already playing it.
        var catalog = Shipped.Catalog;
        var engine = Shipped.Engine();
        engine.State.Vessel.Facilities
            .Single(f => f.Id == DefaultVessel.FactoryA).NameOverride = "Old Faithful";
        engine.Advance(50);

        var written = WorldSave.Write(catalog, engine.State);

        // The extractor is the one facility the scenario gives no name of its own, so it is the one
        // whose label can only have come from its archetype.
        var edited = catalog with
        {
            Facilities = catalog.Facilities
                .Select(f => f.Id.Value == "hydrogen_extractor"
                    ? f with { Label = "Scoop", WorkRatePerTick = f.WorkRatePerTick * 2 }
                    : f)
                .ToList(),
            Storages = catalog.Storages
                .Select(s => s.Id.Value == "facility_buffer" ? s with { CapacityPermille = 50 } : s)
                .ToList(),
        };

        var loaded = new SimulationEngine(edited, Load(written, edited));

        var extractor = loaded.Snapshot.Executors.Single(e => e.Id == DefaultVessel.Extractor01);
        Assert.That(extractor.Label, Is.EqualTo("Scoop"), "a renamed archetype did not reach the save");

        var factoryA = loaded.Snapshot.Executors.Single(e => e.Id == DefaultVessel.FactoryA);
        Assert.That(
            factoryA.Label,
            Is.EqualTo("Old Faithful"),
            "content overwrote a name the player chose");

        var buffer = loaded.Snapshot.Storages.Single(s => s.Id == DefaultVessel.FactoryABuffer);
        var component = buffer.Items.Single(i => i.Id == DefaultVessel.Component);
        Assert.That(
            component.Capacity,
            Is.EqualTo(Shipped.Catalog.Item(DefaultVessel.Component)!.HoldCapacity * 50 / 1000),
            "a changed storage capacity did not reach the save");

        // The doubled work rate is visible as fewer ticks in a run, which is where an execution
        // number surfaces without advancing time. Read through the archetype at the point of use,
        // so it reaches a campaign that was saved before the change.
        Assert.That(extractor.RunTicksTotal, Is.LessThan(
            new SimulationEngine(catalog, Load(written, catalog)).Snapshot.Executors
                .Single(e => e.Id == DefaultVessel.Extractor01).RunTicksTotal));
    }

    [Test]
    public void EveryValueTheSnapshotShows_ComesFromTheCatalogAndTheState()
    {
        // The guard behind the other four. A value read from an engine field, a static, or a clock
        // is invisible until a load, and then it is a behaviour change on the very first tick —
        // programs evaluate against the previous tick's snapshot.
        var catalog = Shipped.Catalog;
        var engine = Busy();

        var direct = Describe(engine.Snapshot);
        var rebuilt = Describe(
            new SimulationEngine(catalog, Load(WorldSave.Write(catalog, engine.State), catalog)).Snapshot);

        Assert.That(rebuilt, Is.EqualTo(direct));
    }

    [Test]
    public void ANewerSave_IsRefusedRatherThanAttempted()
    {
        var catalog = Shipped.Catalog;
        var written = WorldSave.Write(catalog, Shipped.State())
            .Replace($"\"saveVersion\": {WorldSave.CurrentVersion}", "\"saveVersion\": 99");

        var result = WorldSave.Read(written, catalog, Scenarios);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.State, Is.Null);
        Assert.That(result.Errors.Single().Message, Does.Contain("newer version"));
    }

    [Test]
    public void TheUpgraderChain_ExistsBeforeItIsNeeded()
    {
        // Empty, and present. Retrofitting it later means guessing what a version-1 save looked
        // like from whatever happened to survive.
        Assert.That(WorldSave.CurrentVersion, Is.EqualTo(1));
        Assert.That(WorldSave.Upgraders, Is.Empty);
    }

    [Test]
    public void ContentDrift_IsReportedWithEveryMissingIdRatherThanAbsorbed()
    {
        var catalog = Shipped.Catalog;
        var written = WorldSave.Write(catalog, Shipped.State());

        var stripped = catalog with
        {
            Facilities = catalog.Facilities.Where(f => f.Id.Value != "factory").ToList(),
            Items = catalog.Items.Where(i => i.Id != DefaultVessel.Component).ToList(),
        };

        var result = WorldSave.Read(written, stripped, Scenarios);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(
            result.Errors.Count(e => e.Message.Contains("no facility archetype 'factory'")),
            Is.EqualTo(3),
            "three factories name it, and a load should say so once for each");
        Assert.That(result.Errors.Any(e => e.Message.Contains("no item 'component'")), Is.True);
    }

    [Test]
    public void TheScenarioIsPinnedById_AndReReadRatherThanRestored()
    {
        var catalog = Shipped.Catalog;
        var written = WorldSave.Write(catalog, Shipped.State());

        var result = WorldSave.Read(written, catalog, Array.Empty<Core.Content.Scenario>());

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors.Any(e => e.Path == "scenarioId"), Is.True);
    }

    [Test]
    public void TimeFlowIsNotSaved_AndTheAutoPausePreferenceIs()
    {
        // A load resumes at 0×, which is the one state where every action is available. A save that
        // resumed at 4× would resume a vessel moving before its owner had looked at it. Auto-pause
        // is the other way round: a preference the player set, not a speed they left running.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();
        state.Clock.Flow = TimeFlow.X4;
        state.Clock.AutoPauseOnCriticalAlert = true;

        var reloaded = Load(WorldSave.Write(catalog, state), catalog);

        Assert.That(reloaded.Clock.Flow, Is.EqualTo(TimeFlow.Paused));
        Assert.That(reloaded.Clock.AutoPauseOnCriticalAlert, Is.True);
    }

    [Test]
    public void TheJournalSurvivesInFull()
    {
        // A console that goes blank on load is a bug report.
        var catalog = Shipped.Catalog;
        var engine = Shipped.Engine();
        engine.Advance(JournalLedger.Capacity * 2);

        var reloaded = Load(WorldSave.Write(catalog, engine.State), catalog);

        Assert.That(reloaded.Journal.Events, Has.Count.EqualTo(JournalLedger.Capacity));
        Assert.That(reloaded.Journal.TotalEmitted, Is.EqualTo(engine.State.Journal.TotalEmitted));
    }

    [Test]
    public void ASetIsWrittenSorted_SoTwoSavesOfOneWorldAreByteIdentical()
    {
        var catalog = Shipped.Catalog;
        var first = Shipped.State();
        var second = Shipped.State();

        // Same members, inserted in the opposite order. A hash set has no order of its own, and
        // writing one in enumeration order would make a diff between two saves meaningless.
        foreach (var flag in new[] { "zulu", "alpha", "mike" })
        {
            first.Progress.Flags.Add(flag);
        }

        foreach (var flag in new[] { "mike", "alpha", "zulu" })
        {
            second.Progress.Flags.Add(flag);
        }

        Assert.That(WorldSave.Write(catalog, second), Is.EqualTo(WorldSave.Write(catalog, first)));
    }

    [Test]
    public void AStreamIsSavedWhereItStands_NotWhereItStarted()
    {
        // Saving the seed would replay every draw the world has already made the next time it
        // loads, which is the quietest possible way for a deterministic simulation to stop being
        // reproducible.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();
        state.Random.Streams[(int)RngDomain.Mission] = 123_456;

        var reloaded = Load(WorldSave.Write(catalog, state), catalog);

        Assert.That(reloaded.Random.Streams[(int)RngDomain.Mission], Is.EqualTo(123_456));
    }

    [Test]
    public void ARobotKeepsItsEmptySockets_WhichAListOfFittedIdsCouldNotHave()
    {
        // Why the loadout is socket-shaped rather than a list of what is installed. This robot is
        // mid-refit: both sockets exist and neither holds anything. A save that wrote only the
        // fitted ids would write nothing at all here, and the machine would come back off the save
        // reading as a frame with no sockets — indistinguishable from one that never had any.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();
        var robot = new Robot
        {
            Id = state.Robots.Mint(),
            Frame = new RobotFrameId("utility_frame"),
        };

        robot.Sockets.Add(new RobotSocket(new SocketId("mobility"), new StorageId("scout_1_mobility")));
        robot.Sockets.Add(new RobotSocket(new SocketId("payload"), new StorageId("scout_1_payload")));
        state.Robots.Robots.Add(robot);

        var reloaded = Load(WorldSave.Write(catalog, state), catalog);

        Assert.That(reloaded.Robots.NextRobotId, Is.EqualTo(1));
        Assert.That(
            reloaded.Robots.Robots.Single().Sockets.Select(s => $"{s.Socket}={s.Storage}").ToList(),
            Is.EqualTo(new[] { "mobility=scout_1_mobility", "payload=scout_1_payload" }));
    }

    [Test]
    public void SocketOrderIsTheFramesOrder_NotSortedLikeASet()
    {
        // Sockets are an ordered collection, not a set: the frame declares them in the order a
        // loadout panel reads them, so they are written as an array in that order rather than
        // sorted. Sorting them here would put a payload socket before a mobility one and quietly
        // rewrite what the panel shows.
        var catalog = Shipped.Catalog;
        var state = Shipped.State();
        var robot = new Robot
        {
            Id = state.Robots.Mint(),
            Frame = new RobotFrameId("light_frame"),
        };

        foreach (var socket in new[] { "mobility", "hardpoint_1", "systems", "payload" })
        {
            robot.Sockets.Add(new RobotSocket(new SocketId(socket), new StorageId($"scout_1_{socket}")));
        }

        state.Robots.Robots.Add(robot);

        var reloaded = Load(WorldSave.Write(catalog, state), catalog);

        Assert.That(
            reloaded.Robots.Robots.Single().Sockets.Select(s => s.Socket.Value).ToList(),
            Is.EqualTo(new[] { "mobility", "hardpoint_1", "systems", "payload" }));
    }
}
