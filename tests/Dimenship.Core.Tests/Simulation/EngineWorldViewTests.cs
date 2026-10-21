using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// <see cref="EngineWorldView"/> and the <see cref="AmendView"/> decorator over it, on their own.
/// The planner's tests reach the view through an engine; these pin the two rules the view itself
/// owns: an unbuilt executor is invisible, and the hold's supply is free stock only.
/// </summary>
public class EngineWorldViewTests
{
    private static readonly ItemId Ore = WorldBuilder.Ore;
    private static readonly ItemId Alloy = WorldBuilder.Alloy;
    private static readonly StorageId Hold = WorldBuilder.Hold;
    private static readonly StorageId Buffer = new("buffer");
    private static readonly SchematicId Smelt = new("smelt");

    [Test]
    public void AnUnbuiltFacilityOrLine_IsLeftOut_AndAPassiveSourceIsListedFlagged()
    {
        var view = Collaborators.Over(new WorldBuilder()
            .Item(Ore)
            .Item(Alloy)
            .Storage(Hold)
            .Storage(Buffer)
            .Schematic(Smelt, new ItemAmount(Alloy, 1), FacilityType.MatterReactor, inputs: new ItemAmount(Ore, 1))
            .Producer(new ExecutorId("built"), FacilityType.MatterReactor, null)
            .Producer(new ExecutorId("unbuilt"), FacilityType.MatterReactor, null, builtAtStart: false)
            .Producer(new ExecutorId("passive"), FacilityType.Extractor, null, commandable: false)
            .Transport(new ExecutorId("line"), Hold, Buffer, 10)
            .Transport(new ExecutorId("unbuilt_line"), Buffer, Hold, 10, builtAtStart: false)).View();

        Assert.That(view.Facilities.Select(f => f.Id.Value), Is.EqualTo(new[] { "built", "passive" }));
        Assert.That(view.Facilities.Single(f => f.Id.Value == "passive").Commandable, Is.False);
        Assert.That(view.TransportLines.Select(l => l.Id.Value), Is.EqualTo(new[] { "line" }));
    }

    [Test]
    public void TheHoldsSupply_IsFreeStock_AndAnAmendViewAddsOnlyItsOwnPlansHolding()
    {
        var world = Collaborators.Over(new WorldBuilder()
            .Item(Ore)
            .Storage(Hold, StorageArchetype.FullHold, new ItemAmount(Ore, 100)));
        var amended = new PlanId(1);
        world.State.Claims.Change(amended, Hold, Ore, 30);
        world.State.Claims.Change(new PlanId(2), Hold, Ore, 20);

        var view = world.View();

        Assert.That(view.InHold(Ore), Is.EqualTo(50));
        Assert.That(new AmendView(view, world.State.Claims, amended).InHold(Ore), Is.EqualTo(80));
    }
}
