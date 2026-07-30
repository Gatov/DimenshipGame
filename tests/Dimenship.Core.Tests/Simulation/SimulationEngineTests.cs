using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

public class SimulationEngineTests
{
    private static readonly ItemId Ore = new("ore");
    private static readonly ItemId Alloy = new("alloy");
    private static readonly StorageId Hold = WorldDefinition.MainHold;

    /// <summary>These tests exercise the tick, not the catalog, so they carry no schematics.</summary>
    private static SchematicCatalog NoSchematics() =>
        new(Array.Empty<SchematicDefinition>(), Array.Empty<SchematicId>());

    /// <summary>A world with one full-sized hold, which is where every facility below works.</summary>
    private static WorldDefinition World(
        long energyCapacity,
        IReadOnlyList<ItemDefinition> items,
        params FacilityDefinition[] facilities) =>
        new(
            energyCapacity,
            NoSchematics(),
            items,
            new[]
            {
                new StorageDefinition(
                    Hold, "Hold", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
            },
            facilities);

    /// <summary>An extractor with plenty of headroom and no competition for power.</summary>
    private static WorldDefinition ExtractorOnly(long oreCapacity = 1_000_000) =>
        World(
            10_000,
            new[] { new ItemDefinition(Ore, "Ore", oreCapacity) },
            new FacilityDefinition(new FacilityId("extractor"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100));

    [Test]
    public void Advance_ProducesOutputEveryTick()
    {
        var engine = new SimulationEngine(ExtractorOnly());

        engine.Advance(3);

        Assert.That(engine.Snapshot.Tick, Is.EqualTo(3));
        Assert.That(engine.Snapshot.Resources[0].Amount, Is.EqualTo(300));
        Assert.That(engine.Snapshot.Resources[0].NetRatePerTick, Is.EqualTo(100));
    }

    [Test]
    public void Advance_InOneCall_MatchesManySingleTickCalls()
    {
        // The default world (not ExtractorOnly) so this exercises blocking, a power cap hit,
        // and a resource touched by two facilities in the same tick — a world where every
        // field is constant tick-to-tick would leave state-leak bugs invisible.
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
        Assert.That(bulk.Snapshot.Energy, Is.EqualTo(single.Snapshot.Energy));
        Assert.That(bulk.Snapshot.Facilities, Is.EqualTo(single.Snapshot.Facilities));
        Assert.That(bulk.Snapshot.TotalEventsEmitted, Is.EqualTo(single.Snapshot.TotalEventsEmitted));
        // Compared as projections, not as records: SimEvent carries an IReadOnlyDictionary, and
        // record equality compares that by reference, so two structurally identical event
        // streams would never be Is.EqualTo each other.
        Assert.That(Describe(bulk.Snapshot.RecentEvents), Is.EqualTo(Describe(single.Snapshot.RecentEvents)));
    }

    [Test]
    public void DefaultWorld_FirstTick_EmitsExactEventSequence()
    {
        // Pins the concrete event sequence (codes, subjects, and order) for a single tick of
        // the default world, so that swapping the facility foreach for a Dictionary iteration
        // — which would not preserve WorldDefinition.Facilities order — fails this suite.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(1);

        Assert.That(Describe(engine.Snapshot.RecentEvents), Is.EqualTo(new List<string>
        {
            "1|Production|Run|stabilization_field|",
            "1|Production|Run|extractor_01|",
            "1|Production|BlockMissingInput|smelter_a|have=2400,need=40000",
        }));
    }

    [Test]
    public void TwoEnginesFromTheSameDefinition_ProduceIdenticalEventStreams()
    {
        var a = new SimulationEngine(WorldDefinition.CreateDefault());
        var b = new SimulationEngine(WorldDefinition.CreateDefault());

        a.Advance(200);
        b.Advance(200);

        // Compared as projections, not as records: SimEvent carries an IReadOnlyDictionary, and
        // record equality compares that by reference, so two structurally identical event
        // streams would never be Is.EqualTo each other.
        Assert.That(Describe(a.Snapshot.RecentEvents), Is.EqualTo(Describe(b.Snapshot.RecentEvents)));
        Assert.That(a.Snapshot.TotalEventsEmitted, Is.EqualTo(b.Snapshot.TotalEventsEmitted));
    }

    private static List<string> Describe(IReadOnlyList<SimEvent> events) =>
        events
            .Select(e =>
                $"{e.Tick}|{e.Category}|{e.Code}|{e.Subject}|" +
                string.Join(",", e.Data.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")))
            .ToList();

    [Test]
    public void Advance_Zero_DoesNothing()
    {
        var engine = new SimulationEngine(ExtractorOnly());
        var before = engine.Snapshot;

        engine.Advance(0);

        Assert.That(engine.Snapshot, Is.SameAs(before), "no tick means no new snapshot");
    }

    [Test]
    public void Advance_NegativeTicks_Throws()
    {
        var engine = new SimulationEngine(ExtractorOnly());

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Advance(-1));
    }

    [Test]
    public void InitialSnapshot_ReportsFacilitiesAsIdle()
    {
        var engine = new SimulationEngine(ExtractorOnly());

        Assert.That(engine.Snapshot.Tick, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Facilities[0].Status, Is.EqualTo(FacilityStatus.Idle));
        Assert.That(engine.Snapshot.TotalEventsEmitted, Is.EqualTo(0));
    }

    [Test]
    public void Output_BlocksWhenDestinationStockIsFull()
    {
        // Room checks happen before anything is written: a facility that cannot fit its full
        // OutputPerTick blocks outright rather than writing a partial, discarded amount. That
        // is what stops a facility from destroying its input once its output stock is full.
        var engine = new SimulationEngine(ExtractorOnly(oreCapacity: 250));

        engine.Advance(3);

        Assert.That(
            engine.Snapshot.Resources[0].Amount,
            Is.EqualTo(200),
            "the third tick has only 50 units of room against a 100-unit output, so it blocks and writes nothing");
        Assert.That(engine.Snapshot.Facilities[0].Status, Is.EqualTo(FacilityStatus.Blocked));
        Assert.That(engine.Snapshot.Facilities[0].BlockReason, Is.EqualTo(EventCode.StockFull));
        Assert.That(engine.Snapshot.Facilities[0].PowerDraw, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(0));

        var stockFullEvents = engine.Snapshot.RecentEvents.Where(e => e.Code == EventCode.StockFull).ToList();
        Assert.That(stockFullEvents, Has.Count.EqualTo(1), "the third tick has no room, the first two do");

        var stockFull = stockFullEvents[0];
        Assert.That(stockFull.Tick, Is.EqualTo(3));
        Assert.That(stockFull.Category, Is.EqualTo(EventCategory.Production));
        Assert.That(stockFull.Subject, Is.EqualTo("extractor"));
        Assert.That(stockFull.Data["room"], Is.EqualTo(50));
        Assert.That(stockFull.Data["need"], Is.EqualTo(100));
    }

    [Test]
    public void FacilityWithoutItsInput_IsBlockedAndDrawsNoPower()
    {
        var definition = World(
            10_000,
            new[]
            {
                new ItemDefinition(Ore, "Ore", 1_000),
                new ItemDefinition(Alloy, "Alloy", 1_000),
            },
            new FacilityDefinition(new FacilityId("smelter"), FacilityKind.Smelter, Hold,
                PowerDraw: 2_000, Input: Ore, InputPerTick: 40, Output: Alloy, OutputPerTick: 8));
        var engine = new SimulationEngine(definition);

        engine.Advance(1);

        Assert.That(engine.Snapshot.Facilities[0].Status, Is.EqualTo(FacilityStatus.Blocked));
        Assert.That(engine.Snapshot.Facilities[0].BlockReason, Is.EqualTo(EventCode.BlockMissingInput));
        Assert.That(engine.Snapshot.Facilities[0].PowerDraw, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(0));
        Assert.That(engine.Snapshot.RecentEvents.Any(e => e.Code == EventCode.BlockMissingInput), Is.True);
    }

    [Test]
    public void FacilityThatWouldExceedCapacity_IsBlockedOnPower()
    {
        var definition = World(
            5_000,
            new[] { new ItemDefinition(Ore, "Ore", 1_000_000) },
            new FacilityDefinition(new FacilityId("first"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            new FacilityDefinition(new FacilityId("second"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100));
        var engine = new SimulationEngine(definition);

        engine.Advance(1);

        Assert.That(engine.Snapshot.Facilities[0].Status, Is.EqualTo(FacilityStatus.Running));
        Assert.That(engine.Snapshot.Facilities[1].Status, Is.EqualTo(FacilityStatus.Blocked));
        Assert.That(engine.Snapshot.Facilities[1].BlockReason, Is.EqualTo(EventCode.BlockPowerCap));
        Assert.That(engine.Snapshot.Energy.Draw, Is.EqualTo(4_000));
        Assert.That(engine.Snapshot.Energy.Reserve, Is.EqualTo(1_000));
        Assert.That(engine.Snapshot.Resources[0].Amount, Is.EqualTo(100), "only one extractor ran");
    }

    [Test]
    public void StarvationIsCounted_EvenThoughGrantedDrawNeverReachesCapacity()
    {
        // The blind spot this pins: the second facility is refused power on every tick, but
        // because a refused facility's draw is never granted, Draw settles at 4_000 of 5_000 and
        // Reserve reads a healthy 1_000. CapHits therefore stays at 0 for the whole run. Anything
        // watching CapHits or Reserve alone concludes the vessel has headroom while a facility
        // starves continuously; StarvedTicks is the only signal that contradicts that.
        var definition = World(
            5_000,
            new[] { new ItemDefinition(Ore, "Ore", 1_000_000) },
            new FacilityDefinition(new FacilityId("fed"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            new FacilityDefinition(new FacilityId("starved"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100));
        var engine = new SimulationEngine(definition);

        engine.Advance(10);

        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(10));
        Assert.That(engine.Snapshot.Energy.CapHits, Is.EqualTo(0), "granted draw never reached capacity");
        Assert.That(engine.Snapshot.Energy.Reserve, Is.EqualTo(1_000), "and reserve looks healthy throughout");
    }

    [Test]
    public void StarvedTicks_CountsTicksNotFacilities()
    {
        // Three facilities want 4_000 each against a 5_000 cap, so two are refused every tick.
        // StarvedTicks must still read 1 per tick, or it stops being comparable with CapHits.
        var definition = World(
            5_000,
            new[] { new ItemDefinition(Ore, "Ore", 1_000_000) },
            new FacilityDefinition(new FacilityId("a"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            new FacilityDefinition(new FacilityId("b"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            new FacilityDefinition(new FacilityId("c"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100));
        var engine = new SimulationEngine(definition);

        engine.Advance(4);

        Assert.That(
            engine.Snapshot.Facilities.Count(f => f.BlockReason == EventCode.BlockPowerCap),
            Is.EqualTo(2),
            "two facilities refused on the final tick");
        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(4), "but four starved ticks, not eight");
    }

    [Test]
    public void StarvedTicks_StaysZeroWhenEveryFacilityGetsPower()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(500);

        Assert.That(engine.Snapshot.Energy.StarvedTicks, Is.EqualTo(0));
        Assert.That(engine.Snapshot.Energy.CapHits, Is.GreaterThan(0), "the default world does reach cap");
    }

    [Test]
    public void FacilityOrder_DeterminesWhichFacilityWinsThePowerCap()
    {
        // Neither facility can run if the other already has: 6_000 + 6_000 > 10_000. Whichever
        // is listed first in WorldDefinition.Facilities gets power; the other blocks. Ids are
        // deliberately NOT alphabetical relative to list position ("zulu" listed before "alpha"
        // below), so an iteration that sorted by id ascending (or any other id-driven order)
        // would pick the same winner regardless of list order and fail this test — as would a
        // reversed iteration, which would pick the loser as the winner in both directions.
        var zulu = new FacilityDefinition(new FacilityId("zulu"), FacilityKind.Extractor, Hold,
            PowerDraw: 6_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100);
        var alpha = new FacilityDefinition(new FacilityId("alpha"), FacilityKind.Extractor, Hold,
            PowerDraw: 6_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100);

        WorldDefinition DefinitionWith(FacilityDefinition first, FacilityDefinition second) =>
            World(10_000, new[] { new ItemDefinition(Ore, "Ore", 1_000_000) }, first, second);

        var zuluFirst = new SimulationEngine(DefinitionWith(zulu, alpha));
        zuluFirst.Advance(1);
        var zuluWhenFirst = zuluFirst.Snapshot.Facilities.Single(f => f.Id == zulu.Id);
        var alphaWhenSecond = zuluFirst.Snapshot.Facilities.Single(f => f.Id == alpha.Id);
        Assert.That(zuluWhenFirst.Status, Is.EqualTo(FacilityStatus.Running), "zulu, listed first, should win");
        Assert.That(alphaWhenSecond.Status, Is.EqualTo(FacilityStatus.Blocked));
        Assert.That(alphaWhenSecond.BlockReason, Is.EqualTo(EventCode.BlockPowerCap));

        var alphaFirst = new SimulationEngine(DefinitionWith(alpha, zulu));
        alphaFirst.Advance(1);
        var alphaWhenFirst = alphaFirst.Snapshot.Facilities.Single(f => f.Id == alpha.Id);
        var zuluWhenSecond = alphaFirst.Snapshot.Facilities.Single(f => f.Id == zulu.Id);
        Assert.That(alphaWhenFirst.Status, Is.EqualTo(FacilityStatus.Running), "alpha, now listed first, should win");
        Assert.That(zuluWhenSecond.Status, Is.EqualTo(FacilityStatus.Blocked));
        Assert.That(zuluWhenSecond.BlockReason, Is.EqualTo(EventCode.BlockPowerCap));
    }

    [Test]
    public void DrawNeverExceedsCapacity_OverALongRun()
    {
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        for (var i = 0; i < 500; i++)
        {
            engine.Advance(1);
            Assert.That(
                engine.Snapshot.Energy.Draw,
                Is.LessThanOrEqualTo(engine.Snapshot.Energy.Capacity));
        }
    }

    [Test]
    public void ReachingCapacityExactly_EmitsPowerCapReachedAndCountsIt()
    {
        var definition = World(
            4_000,
            new[] { new ItemDefinition(Ore, "Ore", 1_000_000) },
            new FacilityDefinition(new FacilityId("extractor"), FacilityKind.Extractor, Hold,
                PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100));
        var engine = new SimulationEngine(definition);

        engine.Advance(2);

        Assert.That(engine.Snapshot.Energy.CapHits, Is.EqualTo(2));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PowerCapReached),
            Is.EqualTo(2));
    }

    [Test]
    public void EventBuffer_IsBoundedButTheTotalKeepsCounting()
    {
        var engine = new SimulationEngine(ExtractorOnly());

        engine.Advance(SimulationEngine.EventBufferCapacity * 2);

        Assert.That(engine.Snapshot.RecentEvents, Has.Count.EqualTo(SimulationEngine.EventBufferCapacity));
        Assert.That(
            engine.Snapshot.TotalEventsEmitted,
            Is.EqualTo(SimulationEngine.EventBufferCapacity * 2),
            "one Run event per tick, and none of them are forgotten by the counter");
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
            engine.Snapshot.Resources.Single(r => r.Id == new ItemId("alloy")).Amount,
            Is.GreaterThan(0),
            "the extractor should have accumulated enough ore for at least one smelter run");
    }

    [Test]
    public void NetRatePerTick_CanBeNegativeWhenTwoFacilitiesTouchTheSameResourceInOneTick()
    {
        // +100 on a single unblocked extractor is the one number every plausible-but-wrong
        // implementation still gets right. Tick 17 of the default world is the first tick the
        // smelter has enough ore to run, so ore is both produced (+2400, extractor_01) and
        // consumed (-40000, smelter_a) in the same tick: net -37600.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(17);

        var ore = engine.Snapshot.Resources.Single(r => r.Id == Ore);
        Assert.That(ore.NetRatePerTick, Is.EqualTo(-37_600));
    }
}
