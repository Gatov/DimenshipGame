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
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(1);

        Assert.That(Describe(engine.Snapshot.RecentEvents), Is.EqualTo(new List<string>
        {
            "0|Production|TaskQueued|extractor_01|runs=1000000,task=1",
            "0|Production|TaskQueued|smelter_a|runs=1000000,task=2",
            "0|Logistics|TaskQueued|feed_line|quantity=1000000000,task=3",
            "0|Logistics|TaskQueued|return_line|quantity=1000000000,task=4",
            // Transport is stepped before production, so both lines report before the
            // extractor does — and on tick one there is nothing anywhere for them to carry.
            "1|Logistics|PostponeInsufficientSource|feed_line|",
            "1|Logistics|AllTasksBlocked|feed_line|queued=1",
            "1|Logistics|PostponeInsufficientSource|return_line|",
            "1|Logistics|AllTasksBlocked|return_line|queued=1",
            "1|Production|RunStarted|extractor_01|of=1000000,run=1,task=1",
            "1|Production|RunCompleted|extractor_01|done=1,of=1000000,task=1",
            "1|Production|PostponeInsufficientInput|smelter_a|",
            "1|Production|AllTasksBlocked|smelter_a|queued=1",
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
            engine.Snapshot.TotalEventsEmitted, Is.EqualTo(4),
            "two production tasks and two transfers were queued, and nothing else has happened");
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
        Assert.That(engine.Snapshot.Energy.CapHits, Is.GreaterThan(0), "the default world does reach cap");
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
    public void DefaultWorld_EventuallyRunsTheSmelter()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(60);

        Assert.That(
            engine.Snapshot.Resources.Single(r => r.Id == Alloy).Amount,
            Is.GreaterThan(0),
            "the extractor should have accumulated enough ore for at least one smelter run");
    }

    [Test]
    public void NetRatePerTick_CanBeNegativeWhenTwoExecutorsTouchTheSameItemInOneTick()
    {
        // +2,400 on a single unblocked extractor is the one number every plausible-but-wrong
        // implementation still gets right. Tick 18 of the default world is the first tick the
        // feed line has carried enough ore into the smelter's buffer for a run, so ore is both
        // produced (+2,400 into the hold) and consumed (-40,000 out of the buffer) in the same
        // tick: net -37,600. The roll-up spans storages, so hauling between them nets to zero
        // and only production and consumption move this number.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(18);

        var ore = engine.Snapshot.Resources.Single(r => r.Id == Ore);
        Assert.That(ore.NetRatePerTick, Is.EqualTo(-37_600));
    }
}
