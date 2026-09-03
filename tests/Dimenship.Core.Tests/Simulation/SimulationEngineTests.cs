using Dimenship.Core.State;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
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
        var bulk = Shipped.Engine();
        var single = Shipped.Engine();

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
        Assert.That(bulk.Snapshot.Tasks, Is.EqualTo(single.Snapshot.Tasks));
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
        // iteration — which would not preserve DefaultVessel.Producers order — fails here.
        //
        // Every task the default vessel starts with is a standing order, so no run event carries
        // a requested count: event payloads are a long map, and an indefinite task omits the key
        // rather than carrying a sentinel that every reader would have to know about.
        //
        // Tick zero emits nothing at all. Seeding a world is not something that happened to it —
        // the opening tasks are its starting position, exactly as its opening stock is, and no
        // event announces that either. What the seeder produced is asserted in ScenarioSeederTests.
        var engine = Shipped.Engine();

        engine.Advance(1);

        Assert.That(Describe(engine.Snapshot.RecentEvents), Is.EqualTo(new List<string>
        {
            // Quiet opening: only the extractor's out-haul is seeded. Empty buffers postpone;
            // built lines with nothing queued are silent. Unbuilt interconnects are skipped.
            "1|Logistics|PostponeInsufficientSource|extractor_out|",
            "1|Logistics|AllTasksBlocked|extractor_out|queued=1",
            "1|Production|RunStarted|extractor_01|run=1,task=1",
        }));
    }

    [Test]
    public void TwoEnginesFromTheSameDefinition_ProduceIdenticalEventStreams()
    {
        var a = Shipped.Engine();
        var b = Shipped.Engine();

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
        var engine = Shipped.Engine();

        Assert.That(engine.Snapshot.Tick, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(0));
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Produce).Select(t => t.State),
            Is.All.EqualTo(TaskState.NotStarted));
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Select(t => t.State),
            Is.All.EqualTo(TaskState.NotStarted));
        // Seeding a world emits nothing. The journal records what happened, and at tick zero
        // nothing has: the vessel's opening tasks are its starting position in the same way its
        // opening stock is, and no event announces that either.
        Assert.That(engine.Snapshot.TotalEventsEmitted, Is.Zero);
    }

    [Test]
    public void ExecutorOrder_DeterminesWhichExecutorWinsThePowerCap()
    {
        // Neither can run if the other already has: 6,000 + 6,000 > 10,000. Whichever is listed
        // first in DefaultVessel.Producers gets power; the other is refused. Ids are
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
        var engine = Shipped.Engine();

        engine.Advance(500);

        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(0));

        // Quiet opening: standing draw only. CapHits under an approved plan are a later check.
        Assert.That(engine.Snapshot.Energy.CapHits, Is.Zero, "the default world stays under its cap");
        Assert.That(
            engine.Snapshot.Energy.Draw,
            Is.GreaterThan(engine.Snapshot.Energy.Capacity * 7 / 10),
            "standing draw is still most of the budget");
        Assert.That(
            engine.Snapshot.Energy.Draw,
            Is.LessThanOrEqualTo(engine.Snapshot.Energy.Capacity));
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

        engine.Advance(JournalLedger.Capacity);

        Assert.That(engine.Snapshot.RecentEvents, Has.Count.EqualTo(JournalLedger.Capacity));
        Assert.That(
            engine.Snapshot.TotalEventsEmitted,
            Is.EqualTo(JournalLedger.Capacity * 2),
            "a started and a completed run every tick, and none of them forgotten by the counter");
        Assert.That(
            engine.Snapshot.RecentEvents[0].Tick,
            Is.GreaterThan(1),
            "the oldest events were evicted");
    }

    [Test]
    public void DefaultWorld_StayQuietUntilWorkIsQueued()
    {
        var engine = Shipped.Engine();

        engine.Advance(60);

        Assert.That(
            engine.Snapshot.Resources.Single(r => r.Id == DefaultVessel.BasicMetals).Amount,
            Is.EqualTo(40_000),
            "no reactor is seeded, so opening Basic Metals must not grow on their own");

        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(DefaultVessel.SeparateBasic, 1)), DefaultVessel.ReactorA);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(DefaultVessel.MatterMix, 4_000, DefaultVessel.ResourceStorage, DefaultVessel.ReactorABuffer)), DefaultVessel.ReactorAFeed);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(DefaultVessel.BasicMetals, null, DefaultVessel.ReactorABuffer, DefaultVessel.ResourceStorage)), DefaultVessel.ReactorAReturn);

        engine.Advance(60);

        Assert.That(
            engine.Snapshot.Resources.Single(r => r.Id == DefaultVessel.BasicMetals).Amount,
            Is.GreaterThan(40_000),
            "once queued, the hold-star feed and return must complete a reactor run");
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
