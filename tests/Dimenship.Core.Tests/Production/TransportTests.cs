using Dimenship.Core.Simulation;
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
        long bufferPermille = StorageDefinition.FullHold) =>
        new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, atSource))
            .Storage(Buffer, bufferPermille)
            .Transport(Line, Hold, Buffer, throughput)
            .Transfer(Ore, quantity, Hold, Buffer, Line);

    [Test]
    public void ATransfer_MovesUpToItsThroughputEachTick()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(3);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(30));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(70));
    }

    [Test]
    public void ATransfer_CompletesAtItsRequestedQuantity_AndStopsThere()
    {
        var engine = Route(atSource: 100, quantity: 25, throughput: 10).Engine();

        engine.Advance(10);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(25), "not a unit more than asked for");
        var transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.State, Is.EqualTo(TaskState.Complete));
        Assert.That(transfer.MovedQuantity, Is.EqualTo(25));
        Assert.That(engine.Snapshot.Transports[0].Status, Is.EqualTo(ExecutorStatus.NoTasksQueued));
    }

    [Test]
    public void PartialTransfer_MovesWhatIsThere_PostponesTheRest_AndFinishesWhenMoreArrives()
    {
        // The Planning specification's §7 case: a request to move 60 with 19 at the source moves
        // the 19 immediately rather than waiting for the other 41, and picks the rest up later.
        var extractor = new ExecutorId("extractor");
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 19))
            .Storage(Buffer)
            // Listed after the transport in tick order regardless, since transport steps first.
            .Schematic(Mine, new ItemAmount(Ore, 41), FacilityType.Extractor, effort: 500)
            .Producer(extractor, FacilityType.Extractor, Mine, storage: Hold)
            .Transport(Line, Hold, Buffer, 100)
            .Transfer(Ore, 60, Hold, Buffer, Line)
            .Task(Mine, 1, extractor)
            .Engine();

        engine.Advance(1);

        var transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(19), "it moved what was there");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Running));

        engine.Advance(1);
        transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.InsufficientSourceMaterial));
        Assert.That(
            engine.Snapshot.Transports[0].Status,
            Is.EqualTo(ExecutorStatus.AllQueuedTasksBlocked),
            "the line reports itself blocked; the task never reports 'waiting for transport'");

        // The extractor's five-tick run lands 41 more ore in the hold, and the line finishes.
        engine.Advance(10);
        transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(60));
        Assert.That(transfer.State, Is.EqualTo(TaskState.Complete));
    }

    [Test]
    public void AFullDestination_PostponesWithoutLosingWhatWasAlreadyMoved()
    {
        // The buffer holds 100 of the 1,000-unit hold capacity, and the transfer asks for 200.
        var engine = Route(atSource: 500, quantity: 200, throughput: 40, bufferPermille: 100).Engine();

        engine.Advance(10);

        var transfer = engine.Snapshot.TransportTasks.Single();
        Assert.That(transfer.MovedQuantity, Is.EqualTo(100), "it filled the destination and stopped");
        Assert.That(transfer.State, Is.EqualTo(TaskState.Postponed));
        Assert.That(transfer.LastReason, Is.EqualTo(PostponeReason.DestinationFull));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(400), "nothing vanished in transit");
    }

    [Test]
    public void AnEmptySource_IsReportedAsSourceMaterial_NotAsAFullDestination()
    {
        // With nothing to move and nowhere to put it, naming the destination would send the
        // player to the wrong end of the route.
        var engine = Route(atSource: 0, quantity: 50, bufferPermille: 0).Engine();

        engine.Advance(1);

        Assert.That(
            engine.Snapshot.TransportTasks.Single().LastReason,
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
        // The ore is in the hold; the refinery works the buffer. If production ran first, the
        // refinery would spend this tick postponed and start only on the next one. Transport
        // first means the ore lands and the run begins in the same tick — which is what the
        // one-tick difference in alloy below detects.
        var refinery = new ExecutorId("refinery");
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 10))
            .Storage(Buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.Refinery,
                inputs: new ItemAmount(Ore, 10))
            .Producer(refinery, FacilityType.Refinery, Smelt, storage: Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Ore, 10, Hold, Buffer, Line)
            .Task(Smelt, 1, refinery)
            .Engine();

        engine.Advance(1);

        Assert.That(
            engine.Available(Buffer, Alloy), Is.EqualTo(1),
            "hauled and smelted in one tick; production-first ordering would need two");
    }

    [Test]
    public void ATransportLine_FinishesOneTransferBeforeStartingTheNext()
    {
        // Both transfers ride the one route the line runs, so the queue position is the only
        // thing that separates them.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(3);

        var transfers = engine.Snapshot.TransportTasks;
        Assert.That(transfers[0].State, Is.EqualTo(TaskState.Complete), "the first transfer is done");
        Assert.That(transfers[1].MovedQuantity, Is.EqualTo(0), "the second has not begun");

        engine.Advance(3);
        Assert.That(engine.Snapshot.TransportTasks[1].State, Is.EqualTo(TaskState.Complete));
        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(60));
    }

    [Test]
    public void ABlockedTransfer_DoesNotStallTheOnesBehindIt()
    {
        // The first transfer asks for an item the hold has none of. A line that simply held its
        // queue position would do nothing at all; it must move on to work it can actually do.
        var engine = new WorldBuilder()
            .Item(Ore, holdCapacity: 1_000)
            .Item(Alloy, holdCapacity: 1_000)
            .Storage(Hold, StorageDefinition.FullHold, new ItemAmount(Ore, 100))
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, 10)
            .Transfer(Alloy, 30, Hold, Buffer, Line)
            .Transfer(Ore, 30, Hold, Buffer, Line)
            .Engine();

        engine.Advance(3);

        Assert.That(engine.Available(Buffer, Ore), Is.EqualTo(30), "the second transfer ran");
        Assert.That(
            engine.Snapshot.TransportTasks[0].State, Is.EqualTo(TaskState.NotStarted),
            "and the first is still waiting for material that never came");
    }

    [Test]
    public void EnqueueTransfer_RejectsNonsenseRoutes()
    {
        var engine = Route(atSource: 10, quantity: 10).Engine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.EnqueueTransfer(Ore, 0, Hold, Buffer, Line));
        Assert.Throws<ArgumentException>(() => engine.EnqueueTransfer(Ore, 10, Hold, Hold, Line));
        Assert.Throws<ArgumentException>(
            () => engine.EnqueueTransfer(Ore, 10, Hold, new StorageId("nowhere"), Line));
        Assert.Throws<ArgumentException>(
            () => engine.EnqueueTransfer(Ore, 10, Hold, Buffer, new ExecutorId("nobody")));
        Assert.Throws<ArgumentException>(
            () => engine.EnqueueTransfer(new ItemId("unobtanium"), 10, Hold, Buffer, Line));
    }

    [Test]
    public void ATransportLineWithNoThroughput_IsADefinitionError()
    {
        var world = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold)
            .Storage(Buffer)
            .Transport(Line, Hold, Buffer, throughputPerTick: 0);

        Assert.Throws<ArgumentException>(() => world.Engine());
    }

    [Test]
    public void ARouteToAStorageThatDoesNotExist_IsADefinitionError()
    {
        var world = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold)
            .Transport(Line, Hold, new StorageId("nowhere"), throughputPerTick: 10);

        Assert.Throws<ArgumentException>(() => world.Engine());
    }

    [Test]
    public void ARouteThatEndsWhereItStarts_IsADefinitionError()
    {
        var world = new WorldBuilder()
            .Item(Ore)
            .Storage(Hold)
            .Transport(Line, Hold, Hold, throughputPerTick: 10);

        Assert.Throws<ArgumentException>(() => world.Engine());
    }

    [Test]
    public void ALine_RefusesATransferThatIsNotOnItsRoute()
    {
        // Without this the transfer would sit in a queue no line aboard can serve, which reads as
        // a stalled vessel rather than as the planning mistake it is.
        var engine = Route(atSource: 10, quantity: 10).Engine();

        Assert.Throws<ArgumentException>(
            () => engine.EnqueueTransfer(Ore, 10, Buffer, Hold, Line),
            "the line runs hold to buffer, and this asks it to run the other way");
    }

    [Test]
    public void MovedLastTick_IsWhatTheLineActuallyDeliveredThatTick()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(1);

        Assert.That(engine.Snapshot.Transports[0].MovedLastTick, Is.EqualTo(10));
    }

    [Test]
    public void MovedLastTick_ReturnsToZeroOnAnIdleTick()
    {
        // Without the reset an edge would keep its colour after the line stopped, which is the
        // one thing a live load reading must never do.
        var engine = Route(atSource: 10, quantity: 10, throughput: 10).Engine();

        engine.Advance(1);
        Assert.That(engine.Snapshot.Transports[0].MovedLastTick, Is.EqualTo(10), "it finished");

        engine.Advance(1);
        Assert.That(engine.Snapshot.Transports[0].MovedLastTick, Is.EqualTo(0));
    }

    [Test]
    public void MovedLastTick_CountsOnlyTheTickJustFinished_NotTheRunningTotal()
    {
        var engine = Route(atSource: 100, quantity: 100, throughput: 10).Engine();

        engine.Advance(3);

        Assert.That(engine.Snapshot.TransportTasks[0].MovedQuantity, Is.EqualTo(30));
        Assert.That(
            engine.Snapshot.Transports[0].MovedLastTick, Is.EqualTo(10),
            "the task accumulates, the line reports one tick");
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
        Assert.That(line.CarriedItem, Is.EqualTo(Ore));
    }

    [Test]
    public void AnIdleLine_CarriesNothing()
    {
        var engine = Route(atSource: 10, quantity: 10, throughput: 10).Engine();

        engine.Advance(2);

        Assert.That(engine.Snapshot.Transports[0].CarriedItem, Is.Null);
    }

    [Test]
    public void DefaultWorld_RunsTheWholeChainAcrossItsRoutes()
    {
        // Every stage of the vessel, and every stage reached by a route rather than by a facility
        // reaching into a storage it does not work: raw matter to a reactor, alloy back to central
        // storage, and a hull plate arriving at Factory Beta across the factory interconnect —
        // which no line but that interconnect can have delivered.
        var engine = new SimulationEngine(WorldDefinition.CreateDefault());

        engine.Advance(600);

        Assert.That(
            engine.Available(WorldDefinition.ReactorABuffer, WorldDefinition.Ore),
            Is.GreaterThan(0),
            "the feed line delivered raw matter the reactor never put there itself");
        Assert.That(
            engine.Available(WorldDefinition.CentralStorage, WorldDefinition.Alloy),
            Is.GreaterThan(40_000),
            "and the return line brought back more alloy than the vessel opened with");
        // On the transfer rather than on the buffer: Factory Beta consumes a plate on the tick it
        // arrives, so its buffer reads zero however well the interconnect is working.
        Assert.That(
            engine.Snapshot.TransportTasks
                .Single(t => t.Executor == WorldDefinition.FactoryLinkAb)
                .MovedQuantity,
            Is.GreaterThan(0),
            "and the interconnect carried plate from one factory straight to the next");
    }
}
