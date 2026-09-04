using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

public class TransportTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly ExecutorId Line = new("line");
    private static readonly SchematicId Mine = new("mine");
    private static readonly SchematicId Smelt = new("smelt");

    /// <summary>A hold, a buffer, and one line between them. No production at all.</summary>
    private static WorldBuilder Route(
        long atSource,
        long quantity,
        long throughput = 10,
        long bufferPermille = StorageArchetype.FullHold) =>
        new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, atSource))
            .Storage(Buffer, bufferPermille)
            .Transport(Line, Hold, Buffer, throughput)
            .Transfer(Ore, quantity, Hold, Buffer, Line);

    [Test]
    public void ATransfer_PicksUpItsThroughputEachTick_AndDeliversATickBehind()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(3);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(20), "three picked up, two arrived");
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(70));
        Assert.That(engine.Snapshot.Transports[0].Cargo.Single().Amount, Is.EqualTo(10),
            "the third tick's load is still on the belt");
    }

    [Test]
    public void ATransfer_CompletesAtItsRequestedQuantity_AndStopsThere()
    {
        var engine = Route(atSource: 100, quantity: 25, throughput: 10).Engine();

        engine.Advance(10);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(25), "not a unit more than asked for");
        var transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.State, Is.EqualTo(TaskState.Complete));
        Assert.That(transfer.MovedQuantity, Is.EqualTo(25));
        Assert.That(engine.Snapshot.Transports[0].Status, Is.EqualTo(ExecutorStatus.NoTasksQueued));
        Assert.That(engine.Snapshot.Transports[0].Cargo, Is.Empty);
    }

    [Test]
    public void ATransfer_CompletesOnDelivery_NotOnPickup()
    {
        // The distinction the belt introduces: a haul whose last unit is aboard has been picked up
        // in full and moved nothing yet. Completing here would tell a plan its material had
        // arrived a whole belt early.
        var engine = Route(atSource: 100, quantity: 10, throughput: 10).Engine();

        engine.Advance(1);

        var transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.LoadedQuantity, Is.EqualTo(10), "all of it is aboard");
        Assert.That(transfer.MovedQuantity, Is.Zero, "and none of it has arrived");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Running));

        engine.Advance(1);
        transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(10));
        Assert.That(transfer.State, Is.EqualTo(TaskState.Complete));
    }

    [Test]
    public void CargoTakesTheWholeBelt_HoweverLongTheRouteIs()
    {
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, 10, Hold, Buffer, Line)
            .Engine();

        engine.Advance(4);
        Assert.That(engine.Available(Buffer, Ore), Is.Zero, "four ticks of belt, and it is not across yet");
        Assert.That(engine.Snapshot.Transports[0].Cargo.Single().Amount, Is.EqualTo(10));

        engine.Advance(1);
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(10), "the fifth tick lands it");
    }

    [Test]
    public void ALinesCapacity_IsOneTicksThroughputPerTickOfLength()
    {
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 10_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 1_000))
            .Storage(Buffer, 0)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, null, Hold, Buffer, Line)
            .Engine();

        Assert.That(engine.Snapshot.Transports[0].Capacity, Is.EqualTo(40));

        // The destination takes nothing, so the belt fills to the head and then stops there: a
        // rigid belt freezes rather than packing, and the four ticks it took to fill are all it
        // ever holds.
        engine.Advance(100);

        var line = engine.Snapshot.Transports[0];
        Assert.That(line.Cargo.Single().Amount, Is.EqualTo(40));
        Assert.That(line.CargoFillPermille, Is.EqualTo(1_000));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(960), "and it took no more than that");
    }

    [Test]
    public void PartialTransfer_PicksUpWhatIsThere_PostponesTheRest_AndFinishesWhenMoreArrives()
    {
        // The Planning specification's §7 case: a request to move 60 with 19 at the source takes
        // the 19 immediately rather than waiting for the other 41, and picks the rest up later.
        var extractor = new ExecutorId("extractor");
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 19))
            .Storage(Buffer)
            // Listed after the transport in tick order regardless, since transport steps first.
            .Schematic(Mine, new ItemAmount(Ore, 41), FacilityType.Extractor, effort: 500)
            .Producer(extractor, FacilityType.Extractor, Mine, storage: Hold)
            .Transport(Line, Hold, Buffer, 100)
            .Transfer(Ore, 60, Hold, Buffer, Line)
            .Task(Mine, 1, extractor)
            .Engine();

        engine.Advance(1);

        var transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.LoadedQuantity, Is.EqualTo(19), "it took what was there");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Running));

        engine.Advance(1);
        transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(19), "and delivered it a tick later");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
        Assert.That(
            engine.Snapshot.Transports[0].Status,
            Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked),
            "the line reports itself blocked; the task never reports 'waiting for transport'");

        // The extractor's five-tick run lands 41 more ore in the hold, and the line finishes.
        engine.Advance(10);
        transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(60));
        Assert.That(transfer.State, Is.EqualTo(TaskState.Complete));
    }

    [Test]
    public void AFullDestination_FreezesTheBelt_AndHoldsWhatWillNotFit()
    {
        // The buffer holds 100 of the 1,000-unit hold capacity, and the transfer asks for 200.
        var engine = Route(atSource: 500, quantity: 200, throughput: 40, bufferPermille: 100).Engine();

        engine.Advance(10);

        var transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(100), "it filled the destination and stopped");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.DestinationFull));

        var line = engine.Snapshot.Transports[0];
        Assert.That(line.BlockReason, Is.EqualTo(PostponeReason.DestinationFull));
        Assert.That(line.Cargo.Single().Amount, Is.EqualTo(20), "the head keeps what would not fit");
        Assert.That(
            engine.Available(Hold, Ore) + line.Cargo.Single().Amount + engine.Available(Buffer, Ore),
            Is.EqualTo(500),
            "nothing vanished in transit");
    }

    [Test]
    public void AFrozenBelt_KeepsItsFill_HoweverPartial_AndTakesNothingOnWhileItIsStuck()
    {
        // The issue's case: the belt is nowhere near full when the far side stops accepting, and
        // it stays exactly that full. Thirty units on a hundred-unit belt, and a destination with
        // no room at all — a belt is rigid, so nothing behind the stuck head moves and the source
        // is not drawn down another unit.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 500))
            .Storage(Buffer, 0)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 10)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(12);

        var line = engine.Snapshot.Transports[0];
        Assert.That(line.BlockReason, Is.EqualTo(PostponeReason.DestinationFull));
        Assert.That(line.Capacity, Is.EqualTo(100));
        Assert.That(line.CargoFillPermille, Is.EqualTo(300), "blocked at three tenths, not at full");
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(470));

        engine.Advance(50);

        line = engine.Snapshot.Transports[0];
        Assert.That(line.CargoFillPermille, Is.EqualTo(300), "frozen, not filling");
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(470), "and picking nothing up");
        Assert.That(line.LoadedLastTick, Is.Zero);
    }

    [Test]
    public void AFrozenBelt_ResumesFromWhereItStopped_WhenTheDestinationFrees()
    {
        var refinery = new ExecutorId("refinery");
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 500))
            .Storage(Buffer, 30)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(refinery, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 4)
            .Transfer(Ore, null, Hold, Buffer, Line)
            .Engine();

        engine.Advance(20);
        Assert.That(
            engine.Snapshot.Transports[0].BlockReason, Is.EqualTo(PostponeReason.DestinationFull),
            "the buffer filled and the belt stopped");

        // One run frees ten units of buffer, and the line picks up exactly where it stopped.
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(Smelt, 1)), refinery);
        engine.Advance(3);

        Assert.That(engine.Available(Buffer, Alloy), Is.EqualTo(1), "the run consumed the ore");
        Assert.That(engine.Snapshot.Transports[0].DeliveredLastTick, Is.GreaterThan(0),
            "and the belt moved again");
    }

    [Test]
    public void AnEmptySource_IsReportedAsSourceMaterial_NotAsAFullDestination()
    {
        // With nothing to move and nowhere to put it, naming the destination would send the
        // player to the wrong end of the route.
        var engine = Route(atSource: 0, quantity: 50, bufferPermille: 0).Engine();

        engine.Advance(1);

        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single().LastReason,
            Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
    }

    [Test]
    public void Postponement_IsRecordedOnce_NotOncePerTick()
    {
        var engine = Route(atSource: 0, quantity: 50).Engine();

        engine.Advance(200);

        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.PostponeInsufficientSource),
            Is.EqualTo(1));
        Assert.That(
            engine.Snapshot.RecentEvents.Count(e => e.Code == EventCode.AllTasksBlocked),
            Is.EqualTo(1));
    }

    [Test]
    public void TransportRunsBeforeProduction_SoMaterialArrivingThisTickIsUsableThisTick()
    {
        // The ore is in the hold; the refinery works the buffer. The haul takes a tick of belt, so
        // the ore lands on the second tick — and the question this asks is whether the run happens
        // on that same second tick or waits for a third. Transport first means the ore lands and
        // the run begins together, which is what the alloy count below detects.
        var refinery = new ExecutorId("refinery");
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 10))
            .Storage(Buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(refinery, FacilityType.MatterReactor, Smelt, storage: Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Ore, 10, Hold, Buffer, Line)
            .Task(Smelt, 1, refinery)
            .Engine();

        engine.Advance(2);

        Assert.That(
            engine.Available(Buffer, Alloy), Is.EqualTo(1),
            "delivered and smelted in one tick; production-first ordering would need another");
    }

    [Test]
    public void ATransportLine_FinishesLoadingOneTransferBeforeStartingTheNext()
    {
        // Both transfers ride the one route the line runs, so the queue position is the only
        // thing that separates them.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(3);

        var transfers = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ToList();
        Assert.That(transfers[0].LoadedQuantity, Is.EqualTo(30), "the first transfer is entirely aboard");
        Assert.That(transfers[1].LoadedQuantity, Is.Zero, "the second has not begun");

        // And the next tick it moves on rather than idling while the first one travels.
        engine.Advance(1);
        transfers = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ToList();
        Assert.That(transfers[0].State, Is.EqualTo(TaskState.Complete));
        Assert.That(transfers[1].LoadedQuantity, Is.EqualTo(10), "no tick was wasted between them");

        engine.Advance(3);
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ElementAt(1).State,
            Is.EqualTo(TaskState.Complete));
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(60));
    }

    [Test]
    public void ATransferWithNothingToPickUp_DoesNotStallTheOnesBehindIt()
    {
        // The first transfer asks for an item the hold has none of. A line that simply held its
        // queue position would do nothing at all; it must move on to work it can actually do.
        // This is a source-side block, which is the one that leaves the belt free to run.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Alloy, 30, Hold, Buffer, Line)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(4);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(30), "the second transfer ran");
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ElementAt(0).State,
            Is.EqualTo(TaskState.Postponed),
            "and the first is still waiting for material that never came");
    }

    [Test]
    public void ADestinationBlock_StopsTheWholeLine_IncludingTheTransfersBehindIt()
    {
        // The other half of the rule above, and the price of a rigid belt: a head that cannot be
        // put down stops everything, because there is nowhere for the queue behind it to go. The
        // opposing direction is a different line and is not affected by any of this.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold,
                new ItemAmount(Ore, 500), new ItemAmount(Alloy, 500))
            .Storage(Buffer, 30)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 2)
            .Transfer(Ore, null, Hold, Buffer, Line)
            .Transfer(Alloy, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(20);

        var alloy = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ElementAt(1);
        Assert.That(alloy.LoadedQuantity, Is.Zero, "nothing behind the block got aboard");
        Assert.That(alloy.LastReason, Is.EqualTo(PostponeReason.DestinationFull),
            "and it is told what is actually stopping it");
    }

    [Test]
    public void AConditionStopsThePickup_ButCargoAlreadyAboardStillArrives()
    {
        // The producer's carve-out, in transfer form: a condition gates the next pickup and never
        // strands what is already travelling. The gate closes once the hold is down to 70, with
        // three ticks of cargo already on the belt behind it.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 10, lengthTicks: 3)
            .Engine();

        engine.Enqueue(
            new TaskScript(
                new[]
                {
                    new Condition(
                        ConditionKind.StorageItemAmount,
                        new Operand[]
                        {
                            new TargetRef(TargetKind.Storage, Hold.Value),
                            new TargetRef(TargetKind.Item, Ore.Value),
                        },
                        Comparison.GreaterThan,
                        new Literal(70)),
                },
                new Transfer(Ore, null, Hold, Buffer)),
            Line);

        engine.Advance(3);

        Assert.That(engine.Snapshot.Transports[0].Cargo.Single().Amount, Is.EqualTo(30),
            "three ticks of belt, three ticks of cargo, none of it arrived");
        Assert.That(engine.Available(Buffer, Ore), Is.Zero);

        engine.Advance(3);

        var transfer = engine.Snapshot.Tasks.Where(t => t.Action is Transfer).Single();
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.ConditionNotMet),
            "the gate closed on the pickup");
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(30), "and the belt still cleared");
        Assert.That(engine.Snapshot.Transports[0].Cargo, Is.Empty);
    }

    [Test]
    public void EnqueueTransfer_RejectsNonsenseRoutes()
    {
        var engine = Route(atSource: 10, quantity: 10).Engine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 0, Hold, Buffer)), Line));
        Assert.Throws<ArgumentException>(() => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Hold, Hold)), Line));
        Assert.Throws<ArgumentException>(
            () => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Hold, new StorageId("nowhere"))), Line));
        Assert.Throws<ArgumentException>(
            () => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Hold, Buffer)), new ExecutorId("nobody")));
        Assert.Throws<ArgumentException>(
            () => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(new ItemId("unobtanium"), 10, Hold, Buffer)), Line));
    }

    [Test]
    public void ALine_RefusesATransferThatIsNotOnItsRoute()
    {
        // Without this the transfer would sit in a queue no line aboard can serve, which reads as
        // a stalled vessel rather than as the planning mistake it is. A two-way link is two lines,
        // and this is the one that runs the other way refusing work that is not its own.
        var engine = Route(atSource: 10, quantity: 10).Engine();

        Assert.Throws<ArgumentException>(
            () => engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Buffer, Hold)), Line),
            "the line runs hold to buffer, and this asks it to run the other way");
    }

    [Test]
    public void LoadedLastTick_IsWhatTheLineTookOnThatTick()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(1);

        Assert.That(engine.Snapshot.Transports[0].LoadedLastTick, Is.EqualTo(10));
        Assert.That(engine.Snapshot.Transports[0].DeliveredLastTick, Is.Zero, "nothing has arrived yet");
    }

    [Test]
    public void TheTickReadings_ReturnToZeroOnAnIdleTick()
    {
        // Without the reset an edge would keep its colour after the line stopped, which is the
        // one thing a live load reading must never do.
        var engine = Route(atSource: 10, quantity: 10, throughput: 10).Engine();

        engine.Advance(2);
        Assert.That(engine.Snapshot.Transports[0].DeliveredLastTick, Is.EqualTo(10), "it finished");

        engine.Advance(1);
        Assert.That(engine.Snapshot.Transports[0].LoadedLastTick, Is.Zero);
        Assert.That(engine.Snapshot.Transports[0].DeliveredLastTick, Is.Zero);
    }

    [Test]
    public void TheTickReadings_CountOnlyTheTickJustFinished_NotTheRunningTotal()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(3);

        Assert.That(engine.Snapshot.Tasks.Where(t => t.Action is Transfer).ElementAt(0).MovedQuantity, Is.EqualTo(20));
        Assert.That(
            engine.Snapshot.Transports[0].LoadedLastTick, Is.EqualTo(10),
            "the task accumulates, the line reports one tick");
        Assert.That(engine.Snapshot.Transports[0].DeliveredLastTick, Is.EqualTo(10));
    }

    [Test]
    public void ALineReportsItsRouteAndWhatItCarries()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(1);
        var line = engine.Snapshot.Transports[0];

        Assert.That(line.From, Is.EqualTo(Hold));
        Assert.That(line.To, Is.EqualTo(Buffer));
        Assert.That(line.ThroughputPerTick, Is.EqualTo(10));
        Assert.That(line.LengthTicks, Is.EqualTo(1));
        Assert.That(line.Capacity, Is.EqualTo(10));
        Assert.That(line.Cargo.Single().Id, Is.EqualTo(Ore));
        Assert.That(line.CargoFillPermille, Is.EqualTo(1_000));
    }

    [Test]
    public void AnIdleLine_CarriesNothing()
    {
        var engine = Route(atSource: 10, quantity: 10, throughput: 10).Engine();

        engine.Advance(3);

        Assert.That(engine.Snapshot.Transports[0].Cargo, Is.Empty);
        Assert.That(engine.Snapshot.Transports[0].CargoFillPermille, Is.Zero);
    }
    [Test]
    public void DefaultWorld_HoldStarCanCarryFactoryOutput_WhenAPlanQueuesIt()
    {
        // Interconnects are unbuilt. A plan that produces components at Factory Alpha and hauls
        // them through the hold must use the star return, not the A-B link.
        var engine = Shipped.Engine();

        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Produce(DefaultVessel.PressComponents, 2)), DefaultVessel.FactoryA);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(DefaultVessel.BasicMetals, 800, DefaultVessel.ResourceStorage, DefaultVessel.FactoryABuffer)), DefaultVessel.FactoryAFeed);
        engine.Enqueue(new TaskScript(Array.Empty<Condition>(), new Transfer(DefaultVessel.Component, null, DefaultVessel.FactoryABuffer, DefaultVessel.ResourceStorage)), DefaultVessel.FactoryAReturn);

        engine.Advance(600);

        Assert.That(
            engine.Available(DefaultVessel.ResourceStorage, DefaultVessel.Component),
            Is.GreaterThan(0),
            "components reached the hold on the star return");
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Transfer)
                .Single(t => t.Executor == DefaultVessel.FactoryAReturn)
                .MovedQuantity,
            Is.GreaterThan(0));
        Assert.That(
            engine.Snapshot.Transports.Single(t => t.Id == DefaultVessel.FactoryLinkAb).Status,
            Is.EqualTo(ExecutorStatus.NoTasksQueued),
            "the unbuilt interconnect must not have been stepped into service");
    }
}
