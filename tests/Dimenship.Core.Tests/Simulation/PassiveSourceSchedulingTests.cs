using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;
using Dimenship.Core.Tests.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// A passive source (<c>commandable: false</c>) must be invisible to the planner and refused by
/// <see cref="SimulationEngine.Enqueue"/> — the same sentence the content loader already uses when
/// a scenario tries to queue one. Without both, Hydrogen on the shipped vessel quietly schedules
/// the Emergency Hydrogen Extractor. See
/// <c>docs/superpowers/specs/2026-09-16-editable-production-plans-design.md</c> Decision 11.
/// </summary>
public class PassiveSourceSchedulingTests
{
    [Test]
    public void APassiveSource_IsNeverChosenByThePlanner()
    {
        var engine = Shipped.Engine();

        var plan = ProductionPlanner.Plan(new ItemAmount(DefaultVessel.Hydrogen, 1_000), engine);

        Assert.That(
            plan.Tasks, Is.Empty,
            "planning Hydrogen queued work on the emergency extractor");
        Assert.That(
            plan.Unplannable.Single().Reason, Is.EqualTo(UnplannableReason.NoExecutorOrLine));
        Assert.That(
            ((IWorldView)engine).Facilities.Any(f => f.Id == DefaultVessel.Extractor01),
            Is.False,
            "the planner's facility list still includes the passive extractor");
    }

    [Test]
    public void APassiveSource_RefusesARunEnqueuedOnItByName()
    {
        var engine = Shipped.Engine();

        var error = Assert.Throws<ArgumentException>(() =>
            engine.Enqueue(
                new TaskScript(
                    Array.Empty<Condition>(),
                    new Produce(DefaultVessel.ExtractHydrogen, 1)),
                DefaultVessel.Extractor01));

        Assert.That(error!.Message, Does.Contain("not commandable"));
        Assert.That(error.Message, Does.Contain("scheduled by nobody"));
        Assert.That(
            engine.Snapshot.Tasks.Where(t => t.Action is Produce p
                && p.Schematic == DefaultVessel.ExtractHydrogen
                && p.Runs == 1),
            Is.Empty);
    }
}
