using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Replay.Tests;

/// <summary>
/// The experiment's manual comparator (E3): the design's fourth policy, scheduling by hand, on the
/// tuning situations. What is pinned is the two results the go/no-go leans on: a facility choice
/// made in the composer reaches a facility the planner's estimate never picks, and in situation B
/// one priority, set once, does what the improved controller does with many decisions.
/// </summary>
public class ExperimentTests
{
    private static readonly ContentLoadResult Content =
        new JsonContentSource(new DirectoryContentFileSystem(Path.Combine(AppContext.BaseDirectory, "content")))
            .Load();

    private static ContentCatalog Catalog =>
        Content.Catalog
        ?? throw new InvalidOperationException(
            "the shipped content does not load:\n" + string.Join("\n", Content.Errors));

    private static ReplayResult Run(string file, IController? controller = null)
    {
        var parsed = ReplayScript.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", file)), Catalog, Content.Scenarios);
        Assert.That(parsed.Errors, Is.Empty, "the fixture script does not parse");
        var script = parsed.Script!;
        return Replay.Run(Catalog, Content.Scenarios.Single(s => s.Id == script.Scenario), script, controller);
    }

    private static long ReadinessSum(ReplayResult result) =>
        result.Demands.Where(d => d.Demand.Assemble is null).Sum(d => d.Readiness!.Value);

    private static Changeovers ChangeoversAt(ReplayResult result, string facility) =>
        result.Changeovers.Single(c => c.Facility == new ExecutorId(facility));

    [Test]
    public void InSituationA_HardeningAssignedToReactorBeta_PutsAnIdleReactorToWork_AndSavesReactorAlphasChangeovers()
    {
        var queue = Run("e1-a-sustained.json");
        var manual = Run("e1-a-manual.json");
        var working = (int)UtilizationCategory.Working;
        long Working(ReplayResult r, string facility) =>
            r.FacilityTime.Single(f => f.Facility == new ExecutorId(facility)).TicksByCategory[working];

        Assert.That(manual.Assignments, Is.EqualTo(4), "one choice per bulkhead order");
        Assert.That(manual.Interventions, Is.EqualTo(queue.Interventions + 4));
        Assert.That(Working(queue, "reactor_b"), Is.Zero, "the planner's estimate never picks Reactor Beta");
        Assert.That(Working(manual, "reactor_b"), Is.GreaterThan(0));
        Assert.That(ChangeoversAt(manual, "reactor_a").Count, Is.LessThan(ChangeoversAt(queue, "reactor_a").Count));
        Assert.That(ReadinessSum(manual), Is.LessThan(ReadinessSum(queue)));
        Assert.That(manual.Demands.All(d => d.ReadyAtTick is not null), Is.True);
    }

    [Test]
    public void InSituationB_OnePriorityOnTheExpedition_ReadiesItAboutAsFastAsTheImprovedController()
    {
        // The design's §6 B expects raising only the final task to change little. A plan's
        // priority is not the final task's: it reaches the reactor's treatment and the factory's
        // forming as well (K6a), which is why one command is enough here.
        long Expedition(ReplayResult r) =>
            r.Demands.Single(d => d.Demand.Id == "expedition_bulkheads").Readiness!.Value;

        var queue = Run("e1-b-urgent.json");
        var manual = Run("e1-b-manual.json");
        var improved = Run("e1-b-urgent.json", new ImprovedController());

        Assert.That(manual.Interventions, Is.EqualTo(queue.Interventions + 1));
        Assert.That(Expedition(manual), Is.LessThan(Expedition(queue) / 3));
        Assert.That(Expedition(manual) - Expedition(improved), Is.LessThan(Units.TicksPerMinute));
        Assert.That(manual.Demands.All(d => d.ReadyAtTick is not null), Is.True, "the upgrade must still finish");
    }
}
