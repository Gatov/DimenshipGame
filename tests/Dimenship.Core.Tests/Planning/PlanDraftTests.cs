using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Planning;

/// <summary>
/// The draft keeps the requirement graph; flattening must reproduce the flat
/// <see cref="ProductionPlanner"/> output byte-for-byte so every existing planner test stays a
/// regression proof. See <c>2026-09-16-editable-production-plans-design.md</c>.
/// </summary>
public class PlanDraftTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId BufferA = new("buffer_a");
    private static readonly SchematicId Smelt = new("smelt");
    private static readonly ExecutorId RefineryA = new("refinery_a");
    private static readonly ExecutorId FeedA = new("feed_a");
    private static readonly ExecutorId ReturnA = new("return_a");

    [Test]
    public void AnUnadjustedDraft_FlattensToThePlanTheFlatPlannerEmitted()
    {
        var engine = new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100))
            .Storage(BufferA, 100)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor,
                inputs: new ItemAmount(Ore, 10))
            .Producer(RefineryA, FacilityType.MatterReactor, Smelt, storage: BufferA)
            .Transport(FeedA, Hold, BufferA, 1_000)
            .Transport(ReturnA, BufferA, Hold, 1_000)
            .Engine();

        var goal = new ItemAmount(Alloy, 5);
        var destination = BufferA;

        var expected = ProductionPlanner.Plan(goal, engine, destination);
        var draft = PlanDraftEditor.Create(goal, engine, destination);
        var flattened = draft.Flatten();

        Assert.That(flattened.Goal, Is.EqualTo(expected.Goal));
        Assert.That(flattened.Destination, Is.EqualTo(expected.Destination));
        Assert.That(flattened.EstimatedTicks, Is.EqualTo(expected.EstimatedTicks));
        Assert.That(flattened.Unplannable, Is.EqualTo(expected.Unplannable));
        Assert.That(flattened.Tasks, Is.EqualTo(expected.Tasks));
    }
}
