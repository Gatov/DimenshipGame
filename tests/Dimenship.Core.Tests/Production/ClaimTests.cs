using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using Dimenship.Core.State.Save;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Production;

/// <summary>
/// The claims ledger (K6b; D3, Decision 5). Held stock belongs to its plan, free stock goes by
/// priority and then age, and executor declaration order decides nothing a plan asked for.
/// </summary>
public class ClaimTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly StorageId BufferB = new("buffer_b");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId RefineryB = new("refinery_b");

    /// <summary>
    /// Two refineries, each on its own buffer, each fed from the hold by its own line. Sixty ore is
    /// enough for one five-run plan and a sliver of a second. The lines go in either order, which
    /// is the whole point: under visit order, the line declared first took the ore.
    /// </summary>
    private static SimulationEngine TwoRefineries(long ore, bool feedBFirst, long effort = 100)
    {
        var builder = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, ore))
            .Storage(BufferA)
            .Storage(BufferB)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, effort: effort,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, null, storage: BufferA)
            .Producer(RefineryB, FacilityType.MatterReactor, null, storage: BufferB);

        var feeds = new[]
        {
            (new ExecutorId("feed_a"), BufferA),
            (new ExecutorId("feed_b"), BufferB),
        };
        if (feedBFirst)
        {
            Array.Reverse(feeds);
        }

        foreach (var (line, buffer) in feeds)
        {
            builder.Transport(line, Hold, buffer, throughputPerTick: 10);
        }

        return builder
            .Transport(new ExecutorId("return_a"), BufferA, Hold, throughputPerTick: 10)
            .Transport(new ExecutorId("return_b"), BufferB, Hold, throughputPerTick: 10)
            .Engine();
    }

    private static CommittedPlan Commit(SimulationEngine engine, ItemAmount goal)
    {
        engine.Commit(ProductionPlanner.Plan(goal, engine));
        return engine.State.Plans.Plans[^1];
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ScarceStock_GoesToTheOlderPlan_WhicheverLineIsDeclaredFirst(bool feedBFirst)
    {
        var engine = TwoRefineries(ore: 60, feedBFirst);
        var older = Commit(engine, new ItemAmount(Alloy, 5));
        var younger = Commit(engine, new ItemAmount(Alloy, 5));

        engine.Advance(60);

        Assert.That(older.State, Is.EqualTo(PlanState.Complete), "the older plan was starved");
        Assert.That(younger.State, Is.EqualTo(PlanState.Active));
        Assert.That(
            engine.Available(Hold, Alloy), Is.EqualTo(6),
            "five runs for the older plan, and one for the younger from the ten that were free");
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
    }

    [Test]
    public void ACommit_ClaimsTheFreeStockItsWithdrawalsNeed()
    {
        var engine = TwoRefineries(ore: 60, feedBFirst: false);
        var plan = Commit(engine, new ItemAmount(Alloy, 3));

        Assert.That(engine.State.Claims.Held(plan.Id, Hold, Ore), Is.EqualTo(30));
        Assert.That(engine.Free(Hold, Ore), Is.EqualTo(30));
        Assert.That(engine.Available(Hold, Ore), Is.EqualTo(60), "a claim does not make material disappear");
        Assert.That(engine.Snapshot.Claims.Single().Held, Is.EqualTo(30));
    }

    [Test]
    public void ADeliveryByAPlansOwnHaul_IsHeldForThatPlan()
    {
        // Cargo keeps its owner: ore a plan hauled to its own buffer is held there for its runs,
        // and a higher-priority plan cannot take it at the destination.
        // Three-tick runs, ten ore a tick: the first delivery starts run one at once, and the next
        // lands while it is still running, so it has to wait in the buffer, held.
        var engine = TwoRefineries(ore: 60, feedBFirst: false, effort: 300);
        var plan = Commit(engine, new ItemAmount(Alloy, 3));

        engine.Advance(3);

        Assert.That(engine.State.Claims.Held(plan.Id, BufferA, Ore), Is.EqualTo(10));
        Assert.That(engine.ClaimInvariantViolations(), Is.Empty);
    }

    [Test]
    public void ATaskWithNoPlan_CannotTakeHeldStock_AndSaysWhy()
    {
        var engine = TwoRefineries(ore: 50, feedBFirst: false);
        var plan = Commit(engine, new ItemAmount(Alloy, 5));
        Assert.That(engine.Free(Hold, Ore), Is.Zero, "fixture: the plan holds all fifty");

        var loose = engine.Enqueue(
            new TaskScript(Array.Empty<Condition>(), new Transfer(Ore, 10, Hold, BufferB)), new ExecutorId("feed_b"));
        engine.Advance(1);

        Assert.That(
            engine.Snapshot.Tasks.Single(t => t.Id == loose).LastReason,
            Is.EqualTo(PostponeReason.MaterialClaimed));
        Assert.That(engine.Available(BufferB, Ore), Is.Zero);
    }

    [Test]
    public void Relinquish_ReturnsStockToTheAllocationOrder()
    {
        // Relinquish means "let the current order decide again". With the plan still first in that
        // order, the stock comes straight back, which is deliberate; holding the plan is how a
        // player stops it receiving stock.
        var engine = TwoRefineries(ore: 60, feedBFirst: false);
        var older = Commit(engine, new ItemAmount(Alloy, 5));
        var younger = Commit(engine, new ItemAmount(Alloy, 5));

        engine.Relinquish(older.Id, Hold, Ore, 20);
        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.EqualTo(50), "it still ranked first");

        older.Held = true;
        engine.Relinquish(older.Id, Hold, Ore, 20);
        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.EqualTo(30));
        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(30), "the younger plan took what was given up");
    }

    [Test]
    public void Reassign_MovesHeldStock_UpToWhatTheReceiverIsShort()
    {
        var engine = TwoRefineries(ore: 60, feedBFirst: false);
        var older = Commit(engine, new ItemAmount(Alloy, 5));
        var younger = Commit(engine, new ItemAmount(Alloy, 5));

        engine.Reassign(older.Id, younger.Id, Hold, Ore, 100);

        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(50), "bounded by the receiver's need");
        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.EqualTo(10), "the rest stays with the giver");
    }

    [Test]
    public void PriorityRanksFreeStock_AheadOfAge()
    {
        var engine = TwoRefineries(ore: 60, feedBFirst: false);
        var older = Commit(engine, new ItemAmount(Alloy, 5));
        var younger = Commit(engine, new ItemAmount(Alloy, 5));
        engine.SetPriority(younger.Id, Priority.High);

        // Priority never moves stock already held: the older plan keeps its fifty.
        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.EqualTo(50));

        // Freed stock is ranked by it, and a held plan is skipped: the younger, urgent plan fills
        // first, and what it does not need stays free rather than going back to the held plan.
        older.Held = true;
        engine.Relinquish(older.Id, Hold, Ore, 50);

        Assert.That(engine.State.Claims.Held(younger.Id, Hold, Ore), Is.EqualTo(50));
        Assert.That(engine.State.Claims.Held(older.Id, Hold, Ore), Is.Zero);
        Assert.That(engine.Free(Hold, Ore), Is.EqualTo(10));
    }

    [Test]
    public void TheInvariantHolds_AfterEveryTick_OnABusyShippedVessel()
    {
        // Several plans for overlapping items, committed while earlier ones are still running, on
        // the vessel the game ships. Free stock beside an outstanding claim, or a holding beyond
        // its need, on any tick, is a bug in allocation.
        var engine = Shipped.Engine();
        var demands = new (long Tick, ItemId Item, long Quantity, StorageId? To)[]
        {
            (0, DefaultVessel.Component, 2_000, null),
            (50, DefaultVessel.RobotFrame, 250, DefaultVessel.DockAHold),
            (300, DefaultVessel.Module, 500, DefaultVessel.DockBHold),
            (600, DefaultVessel.Component, 1_000, null),
        };

        for (var tick = 0L; tick < 2_500; tick++)
        {
            foreach (var d in demands.Where(d => d.Tick == tick))
            {
                var approval = PlanDraftEditor.Approve(PlanDraftEditor.Create(new ItemAmount(d.Item, d.Quantity), engine, d.To), engine);
                engine.Commit(((PlanApprovalCommitted)approval).Plan);
            }

            engine.Advance(1);
            var violations = engine.ClaimInvariantViolations();
            Assert.That(violations, Is.Empty, $"tick {engine.State.Clock.Tick}");
        }

        Assert.That(engine.State.Plans.Plans.Count(p => p.State == PlanState.Complete), Is.GreaterThan(0),
            "nothing finished, so the fixture proves less than it claims");
    }

    [Test]
    public void ClaimsSurviveASave_AndAnImpossibleHoldingIsReported()
    {
        var catalog = Shipped.Catalog;
        var engine = Shipped.Engine();
        engine.Commit(ProductionPlanner.Plan(new ItemAmount(DefaultVessel.Component, 2_000), engine));
        engine.Advance(5);
        Assert.That(engine.State.Claims.Entries, Is.Not.Empty, "fixture: the plan holds something");

        var written = WorldSave.Write(catalog, engine.State);
        var loaded = WorldSave.Read(written, catalog, new[] { Shipped.DefaultVessel });
        Assert.That(loaded.Errors, Is.Empty);
        Assert.That(WorldSave.Write(catalog, loaded.State!), Is.EqualTo(written));

        var tree = System.Text.Json.Nodes.JsonNode.Parse(written)!;
        tree["state"]!["claims"]![0]!["held"] = 999_999_999;
        var broken = WorldSave.Read(tree.ToJsonString(), catalog, new[] { Shipped.DefaultVessel });

        Assert.That(broken.Succeeded, Is.False, "an impossible holding was clamped or absorbed");
    }
}
