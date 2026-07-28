using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

public class SimulationEngineTests
{
    private static readonly ResourceId Ore = new("ore");
    private static readonly ResourceId Alloy = new("alloy");

    /// <summary>An extractor with plenty of headroom and no competition for power.</summary>
    private static WorldDefinition ExtractorOnly(long oreCapacity = 1_000_000) =>
        new(
            EnergyCapacity: 10_000,
            Resources: new[] { new ResourceDefinition(Ore, oreCapacity, 0) },
            Facilities: new[]
            {
                new FacilityDefinition(new FacilityId("extractor"), FacilityKind.Extractor,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            });

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
        var bulk = new SimulationEngine(ExtractorOnly());
        var single = new SimulationEngine(ExtractorOnly());

        bulk.Advance(10);
        for (var i = 0; i < 10; i++)
        {
            single.Advance(1);
        }

        Assert.That(bulk.Snapshot.Tick, Is.EqualTo(single.Snapshot.Tick));
        Assert.That(bulk.Snapshot.Resources, Is.EqualTo(single.Snapshot.Resources));
        Assert.That(bulk.Snapshot.Energy, Is.EqualTo(single.Snapshot.Energy));
        Assert.That(bulk.Snapshot.Facilities, Is.EqualTo(single.Snapshot.Facilities));
        Assert.That(bulk.Snapshot.TotalEventsEmitted, Is.EqualTo(single.Snapshot.TotalEventsEmitted));
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
    public void Output_IsClampedToCapacityAndReportsStockFull()
    {
        var engine = new SimulationEngine(ExtractorOnly(oreCapacity: 250));

        engine.Advance(3);

        Assert.That(engine.Snapshot.Resources[0].Amount, Is.EqualTo(250));
        Assert.That(
            engine.Snapshot.RecentEvents.Where(e => e.Code == EventCode.StockFull).ToList(),
            Has.Count.EqualTo(1),
            "the third tick overflows, the first two do not");
    }

    [Test]
    public void FacilityWithoutItsInput_IsBlockedAndDrawsNoPower()
    {
        var definition = new WorldDefinition(
            EnergyCapacity: 10_000,
            Resources: new[]
            {
                new ResourceDefinition(Ore, 1_000, 0),
                new ResourceDefinition(Alloy, 1_000, 0),
            },
            Facilities: new[]
            {
                new FacilityDefinition(new FacilityId("smelter"), FacilityKind.Smelter,
                    PowerDraw: 2_000, Input: Ore, InputPerTick: 40, Output: Alloy, OutputPerTick: 8),
            });
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
        var definition = new WorldDefinition(
            EnergyCapacity: 5_000,
            Resources: new[] { new ResourceDefinition(Ore, 1_000_000, 0) },
            Facilities: new[]
            {
                new FacilityDefinition(new FacilityId("first"), FacilityKind.Extractor,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
                new FacilityDefinition(new FacilityId("second"), FacilityKind.Extractor,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            });
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
        var definition = new WorldDefinition(
            EnergyCapacity: 4_000,
            Resources: new[] { new ResourceDefinition(Ore, 1_000_000, 0) },
            Facilities: new[]
            {
                new FacilityDefinition(new FacilityId("extractor"), FacilityKind.Extractor,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 100),
            });
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
            engine.Snapshot.Resources.Single(r => r.Id == new ResourceId("alloy")).Amount,
            Is.GreaterThan(0),
            "the extractor should have accumulated enough ore for at least one smelter run");
    }
}
