using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

public class SimulationEngineTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly SchematicId Mine = new("mine");

    /// <summary>One extractor with plenty of headroom and no competition for power.</summary>
    private static WorldBuilder ExtractorOnly(long oreCapacity = 1_000_000, long energyPerRun = 0)
    {
        var extractor = new ExecutorId("extractor");
        return new WorldBuilder()
            .Item(Ore, oreCapacity)
            .Storage(Hold)
            .Schematic(Mine, new ItemAmount(Ore, 100), FacilityType.Extractor, energy: energyPerRun)
            .Producer(extractor, FacilityType.Extractor, Mine)
            .Task(Mine, 1_000_000, extractor);
    }

    private static List<string> Describe(IReadOnlyList<StorageState> storages) =>
        storages
            .Select(s => $"{s.Id}|" + string.Join(",", s.Items.Select(i => $"{i.Id}={i.Amount}/{i.Capacity}")))
            .ToList();

    private static List<string> Describe(IReadOnlyList<SimEvent> events) =>
        events
            .Select(e =>
                $"{e.Tick}|{e.Category}|{e.Code}|{e.Subject}|" +
                string.Join(",", e.Data.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")))
            .ToList();

    [Test]
    public void Advance_ProducesOutputEveryTick()
    {
        var engine = ExtractorOnly().Engine();

        engine.Advance(3);

        Assert.That(engine.Snapshot.Tick, Is.EqualTo(3));
        Assert.That(engine.Snapshot.Resources[0].Amount, Is.EqualTo(300));
        Assert.That(engine.Snapshot.Resources[0].NetRatePerTick, Is.EqualTo(100));
    }

    [Test]
    public void Advance_InOneCall_MatchesManySingleTickCalls()
    {
        // The default world (not ExtractorOnly) so this exercises postponement, a power cap hit,
        // and an item touched by two executors in the same tick — a world where every field is
        // constant tick-to-tick would leave state-leak bugs invisible.
        const int ticks = 60;
        var bulk = new SimulationEngine(WorldDefinition.CreateDefault());
        var single = new SimulationEngine(WorldDefinition.CreateDefault());

        bulk.Advance(ticks);
        for (var i = 0; i < ticks; i++)
        {
            single.Advance(1);
        }

        Assert.That(bulk.Snapshot.Tick, Is.EqualTo(single.Snapshot.Tick));
        Assert.That(bulk.Snapshot.Resources, Is.EqualTo(single.Snapshot.Resources));
        // Storages are compared as projections for the same reason the events are: StorageState
        // holds an IReadOnlyList, and record equality compares that by reference.
        Assert.That(Describe(bulk.Snapshot.Storages), Is.EqualTo(Describe(single.Snapshot.Storages)));
        Assert.That(bulk.Snapshot.Energy, Is.EqualTo(single.Snapshot.Energy));
        Assert.That(bulk.Snapshot.Executors, Is.EqualTo(single.Snapshot.Executors));
        Assert.That(bulk.Snapshot.ProductionTasks, Is.EqualTo(single.Snapshot.ProductionTasks));
        Assert.That(bulk.Snapshot.TotalEventsEmitted, Is.EqualTo(single.Snapshot.TotalEventsEmitted));
        // Compared as projections, not as records: SimEvent carries an IReadOnlyDictionary, and
        // record equality compares that by reference, so two structurally identical event
        // streams would never be Is.EqualTo each other.
        Assert.That(Describe(bulk.Snapshot.RecentEvents), Is.EqualTo(Describe(single.Snapshot.RecentEvents)));
    }

    [Test]
    public void DefaultWorld_FirstTick_EmitsExactEventSequence()
    {
        // Pins the concrete event sequence (codes, subjects, order and payload) for the first
        // tick of the default world, so that swapping the executor foreach for a Dictionary
        // iteration — which would not preserve WorldDefinition.Producers order — fails here.
        //
        // Every task the default vessel starts with is a standing order, so no queue or run event
        // carries a requested count: event payloads are a long map, and an indefinite task omits
        // the key rather than carrying a sentinel that every reader would have to know about.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(1);

        Assert.That(Describe(engine.Snapshot.RecentEvents), Is.EqualTo(new List<string>
        {
            "0|Production|TaskQueued|extractor_01|task=1",
            "0|Production|TaskQueued|reactor_a|task=2",
            "0|Production|TaskQueued|reactor_b|task=3",
            "0|Production|TaskQueued|factory_a|task=4",
            "0|Production|TaskQueued|factory_b|task=5",
            "0|Production|TaskQueued|factory_c|task=6",
            // The mission docks queue nothing: they carry no schematic, because acquisition is not
            // a system yet. A dock appearing here would mean one had been given fake work.
            "0|Logistics|TaskQueued|extractor_out|task=7",
            "0|Logistics|TaskQueued|reactor_a_feed|task=8",
            "0|Logistics|TaskQueued|reactor_a_return|task=9",
            "0|Logistics|TaskQueued|reactor_b_feed|task=10",
            "0|Logistics|TaskQueued|reactor_b_return|task=11",
            "0|Logistics|TaskQueued|factory_a_feed|task=12",
            "0|Logistics|TaskQueued|factory_b_feed|task=13",
            "0|Logistics|TaskQueued|factory_link_ab|task=14",
            "0|Logistics|TaskQueued|factory_link_bc|task=15",
            "0|Logistics|TaskQueued|factory_c_return|task=16",
            // Transport is stepped before production, so every line reports before any facility
            // does. The three that move something are the three drawing on the opening stock in
            // Resource Storage; the rest have empty buffers behind them on tick one.
            //
            // A line carries at most its throughput per tick, and every line is sized to the stage
            // it serves rather than to a whole run, so a facility's first run waits several ticks
            // for its buffer to fill. Every facility but the extractor is therefore blocked for
            // want of input on tick one, which is the vessel starting cold rather than a fault.
            "1|Logistics|PostponeInsufficientSource|extractor_out|",
            "1|Logistics|AllTasksBlocked|extractor_out|queued=1",
            "1|Logistics|TransferStarted|reactor_a_feed|task=8",
            "1|Logistics|PostponeInsufficientSource|reactor_a_return|",
            "1|Logistics|AllTasksBlocked|reactor_a_return|queued=1",
            "1|Logistics|TransferStarted|reactor_b_feed|task=10",
            "1|Logistics|PostponeInsufficientSource|reactor_b_return|",
            "1|Logistics|AllTasksBlocked|reactor_b_return|queued=1",
            "1|Logistics|TransferStarted|factory_a_feed|task=12",
            "1|Logistics|PostponeInsufficientSource|factory_b_feed|",
            "1|Logistics|AllTasksBlocked|factory_b_feed|queued=1",
            "1|Logistics|PostponeInsufficientSource|factory_link_ab|",
            "1|Logistics|AllTasksBlocked|factory_link_ab|queued=1",
            "1|Logistics|PostponeInsufficientSource|factory_link_bc|",
            "1|Logistics|AllTasksBlocked|factory_link_bc|queued=1",
            "1|Logistics|PostponeInsufficientSource|factory_c_return|",
            "1|Logistics|AllTasksBlocked|factory_c_return|queued=1",
            "1|Production|RunStarted|extractor_01|run=1,task=1",
            "1|Production|PostponeInsufficientInput|reactor_a|",
            "1|Production|AllTasksBlocked|reactor_a|queued=1",
            "1|Production|PostponeInsufficientInput|reactor_b|",
            "1|Production|AllTasksBlocked|reactor_b|queued=1",
            "1|Production|PostponeInsufficientInput|factory_a|",
            "1|Production|AllTasksBlocked|factory_a|queued=1",
            "1|Production|PostponeInsufficientInput|factory_b|",
            "1|Production|AllTasksBlocked|factory_b|queued=1",
            "1|Production|PostponeInsufficientInput|factory_c|",
            "1|Production|AllTasksBlocked|factory_c|queued=1",
        }));
    }

    [Test]
    public void TwoEnginesFromTheSameDefinition_ProduceIdenticalEventStreams()
    {
        var a = new SimulationEngine(WorldDefinition.CreateDefault());
        var b = new SimulationEngine(WorldDefinition.CreateDefault());

        a.Advance(200);
        b.Advance(200);

        Assert.That(Describe(a.Snapshot.RecentEvents), Is.EqualTo(Describe(b.Snapshot.RecentEvents)));
        Assert.That(a.Snapshot.TotalEventsEmitted, Is.EqualTo(b.Snapshot.TotalEventsEmitted));
    }

    [Test]
    public void Advance_Zero_DoesNothing()
    {
        var engine = ExtractorOnly().Engine();
        var before = engine.Snapshot;

        engine.Advance(0);

        Assert.That(engine.Snapshot, Is.SameAs(before), "no tick means no new snapshot");
    }

    [Test]
    public void UnlockingASchematicTheWorldDoesNotHave_ThrowsNamingIt()
    {
        // The unlock set is the world's, not the catalog's, so this is the world's check. A typo
        // in it would otherwise sit silently until a player wondered why a mission reward never
        // appeared in the planner.
        var world = ExtractorOnly().Unlock(new SchematicId("mien"));

        var thrown = Assert.Throws<ArgumentException>(() => world.Engine());

        Assert.That(thrown!.Message, Does.Contain("mien"));
    }

    [Test]
    public void Advance_NegativeTicks_Throws()
    {
        var engine = ExtractorOnly().Engine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Advance(-1));
    }

    [Test]
    public void InitialSnapshot_HasRunNothing()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        Assert.That(engine.Snapshot.Tick, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(0));
        Assert.That(
            engine.Snapshot.ProductionTasks.Select(t => t.State),
            Is.All.EqualTo(TaskState.NotStarted));
        Assert.That(
            engine.Snapshot.TransportTasks.Select(t => t.State),
            Is.All.EqualTo(TaskState.NotStarted));
        Assert.That(
            engine.Snapshot.TotalEventsEmitted, Is.EqualTo(16),
            "six production tasks and ten transfers were queued, and nothing else has happened");
    }

    [Test]
    public void ExecutorOrder_DeterminesWhichExecutorWinsThePowerCap()
    {
        // Neither can run if the other already has: 6,000 + 6,000 > 10,000. Whichever is listed
        // first in WorldDefinition.Producers gets power; the other is refused. Ids are
        // deliberately NOT alphabetical relative to list position ("zulu" before "alpha"), so an
        // iteration sorted by id — or reversed — would pick the same winner in both orderings
        // and fail this test.
        var zulu = new ExecutorId("zulu");
        var alpha = new ExecutorId("alpha");

        SimulationEngine EngineWith(ExecutorId first, ExecutorId second) =>
            new WorldBuilder()
                .Energy(10_000)
                .Item(Ore)
                .Storage(Hold)
                .Schematic(Mine, new ItemAmount(Ore, 100), FacilityType.Extractor, energy: 6_000)
                .Producer(first, FacilityType.Extractor, Mine)
                .Producer(second, FacilityType.Extractor, Mine)
                .Task(Mine, 5, first)
                .Task(Mine, 5, second)
                .Engine();

        var zuluFirst = EngineWith(zulu, alpha);
        zuluFirst.Advance(1);
        Assert.That(
            zuluFirst.Snapshot.Executors.Single(e => e.Id == zulu).Status,
            Is.EqualTo(ExecutorStatus.RunningTask), "zulu, listed first, should win");
        Assert.That(
            zuluFirst.Snapshot.Executors.Single(e => e.Id == alpha).BlockReason,
            Is.EqualTo(PostponeReason.InsufficientEnergy));

        var alphaFirst = EngineWith(alpha, zulu);
        alphaFirst.Advance(1);
        Assert.That(
            alphaFirst.Snapshot.Executors.Single(e => e.Id == alpha).Status,
            Is.EqualTo(ExecutorStatus.RunningTask), "alpha, now listed first, should win");
        Assert.That(
            alphaFirst.Snapshot.Executors.Single(e => e.Id == zulu).BlockReason,
            Is.EqualTo(PostponeReason.InsufficientEnergy));
    }

    [Test]
    public void StarvationIsCounted_EvenThoughGrantedDrawNeverReachesCapacity()
    {
        // The blind spot this pins: the second executor is refused power on every tick, but
        // because a refused charge is never granted, Draw settles at 6,000 of 10,000 and Reserve
        // reads a healthy 4,000. CapHits therefore stays at 0 for the whole run. Anything
        // watching CapHits or Reserve alone concludes the vessel has headroom while an executor
        // starves continuously; StarvedTicks is the only signal that contradicts that.
        var fed = new ExecutorId("fed");
        var starved = new ExecutorId("starved");
        var engine = new WorldBuilder()
            .Energy(10_000)
            .Item(Ore)
            .Storage(Hold)
            .Schematic(Mine, new ItemAmount(Ore, 100), FacilityType.Extractor, energy: 6_000)
            .Producer(fed, FacilityType.Extractor, Mine)
            .Producer(starved, FacilityType.Extractor, Mine)
            .Task(Mine, 100, fed)
            .Task(Mine, 100, starved)
            .Engine();

        engine.Advance(10);

        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(10));
        Assert.That(engine.Snapshot.Energy.CapHits, Is.EqualTo(0), "granted draw never reached capacity");
        Assert.That(engine.Snapshot.Energy.Reserve, Is.EqualTo(4_000), "and reserve looks healthy throughout");
    }

    [Test]
    public void StarvedTicks_CountsTicksNotExecutors()
    {
        // Three executors want 6,000 each against a 10,000 cap, so two are refused every tick.
        // StarvedTicks must still read 1 per tick, or it stops being comparable with CapHits.
        var builder = new WorldBuilder()
            .Energy(10_000)
            .Item(Ore)
            .Storage(Hold)
            .Schematic(Mine, new ItemAmount(Ore, 100), FacilityType.Extractor, energy: 6_000);

        foreach (var name in new[] { "a", "b", "c" })
        {
            builder
                .Producer(new ExecutorId(name), FacilityType.Extractor, Mine)
                .Task(Mine, 100, new ExecutorId(name));
        }

        var engine = builder.Engine();

        engine.Advance(4);

        Assert.That(
            engine.Snapshot.Executors.Count(e => e.BlockReason == PostponeReason.InsufficientEnergy),
            Is.EqualTo(2),
            "two executors refused on the final tick");
        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(4), "but four starved ticks, not eight");
    }

    [Test]
    public void StarvedTicks_StaysZeroWhenEveryExecutorGetsPower()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(500);

        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(0));

        // The vessel runs just under its cap rather than at it, and the reserve is deliberate: it
        // is the room a fuel-burning power core will need when capacity stops being a constant.
        // Reaching capacity exactly is covered by ReachingCapacityExactly_… on a built world.
        Assert.That(engine.Snapshot.Energy.CapHits, Is.Zero, "the default world stays under its cap");
        Assert.That(
            engine.Snapshot.Energy.Draw,
            Is.GreaterThan(engine.Snapshot.Energy.Capacity * 9 / 10),
            "but close enough to it that the energy budget is a real constraint");
    }

    [Test]
    public void ReachingCapacityExactly_EmitsPowerCapReachedAndCountsIt()
    {
        var engine = ExtractorOnly(energyPerRun: 4_000).Energy(4_000).Engine();

        engine.Advance(2);

        Assert.That(engine.Snapshot.Energy.CapHits, Is.EqualTo(2));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PowerCapReached),
            Is.EqualTo(2));
    }

    [Test]
    public void EventBuffer_IsBoundedButTheTotalKeepsCounting()
    {
        var engine = ExtractorOnly().Engine();

        engine.Advance(SimulationEngine.EventBufferCapacity);

        Assert.That(engine.Snapshot.RecentEvents, Has.Count.EqualTo(SimulationEngine.EventBufferCapacity));
        Assert.That(
            engine.Snapshot.TotalEventsEmitted,
            Is.EqualTo(SimulationEngine.EventBufferCapacity * 2 + 1),
            "a started and a completed run every tick, plus the one task queued before any of "
            + "them, and none of them forgotten by the counter");
        Assert.That(
            engine.Snapshot.RecentEvents[0].Tick,
            Is.GreaterThan(1),
            "the oldest events were evicted");
    }

    [Test]
    public void DefaultWorld_EventuallyRunsTheReactors()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(60);

        Assert.That(
            engine.Snapshot.Resources.Single(r => r.Id == WorldDefinition.BasicMetals).Amount,
            Is.GreaterThan(40_000),
            "the feed lines should have carried Matter Mix for at least one reactor run, and the "
            + "return line brought back more Basic Metals than the vessel opened with");
    }

    [Test]
    public void NetRatePerTick_CanBeNegativeWhenTwoExecutorsTouchTheSameItemInOneTick()
    {
        // +2,400 on a single unblocked extractor is the one number every plausible-but-wrong
        // implementation still gets right. Here an extractor and a refinery touch ore on the same
        // tick — one run each, both a single tick long — so ore is produced (+2,400 into the hold)
        // and consumed (-40,000 out of the buffer) at once: net -37,600. The roll-up spans
        // storages, so hauling between them nets to zero and only production and consumption move
        // this number.
        //
        // Built here rather than taken from the default vessel: this asserts on the roll-up, and
        // pinning it to whichever tick of a nine-facility world happens to align two runs would be
        // asserting on that world's tuning instead.
        var buffer = new StorageId("buffer");
        var smelt = new SchematicId("smelt");
        var extract = new SchematicId("extract");

        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 2_000_000)
            .Item(Alloy)
            .Storage(Hold)
            .Storage(buffer, initial: new ItemAmount(Ore, 40_000))
            .Schematic(extract, new ItemAmount(Ore, 2_400), FacilityType.Extractor)
            .Schematic(
                smelt, new ItemAmount(Alloy, 8_000), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 40_000))
            .Producer(new ExecutorId("extractor"), FacilityType.Extractor, extract)
            .Producer(
                new ExecutorId("smelter"), FacilityType.MatterReactor, smelt, storage: buffer)
            .Task(extract, 10, new ExecutorId("extractor"))
            .Task(smelt, 1, new ExecutorId("smelter"))
            .Engine();

        engine.Advance(1);

        var ore = engine.Snapshot.Resources.Single(r => r.Id == Ore);
        Assert.That(ore.NetRatePerTick, Is.EqualTo(-37_600));
    }
}
