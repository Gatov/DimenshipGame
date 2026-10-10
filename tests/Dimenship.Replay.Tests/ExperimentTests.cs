using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Replay.Tests;

/// <summary>
/// The experiment's manual comparator (E3): the design's fourth policy, scheduling by hand, on the
/// tuning situations. What is pinned is the two results the go/no-go leans on: a facility choice
/// made in the composer reaches the facility it names, and in situation B one priority, set once,
/// does what the improved controller does with many decisions. The first follow-up the go/no-go
/// recommended, the changeover-aware estimate (K5c), is pinned here too, on the situation that
/// motivated it.
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

    private static Changeovers ChangeoversAt(ReplayResult result, string facility) =>
        result.Changeovers.Single(c => c.Facility == new ExecutorId(facility));

    private static long Working(ReplayResult result, string facility) =>
        result.FacilityTime.Single(f => f.Facility == new ExecutorId(facility))
            .TicksByCategory[(int)UtilizationCategory.Working];

    [Test]
    public void InSituationA_AHardeningAssignment_PutsTheStageOnReactorBeta_AndCountsAsAnIntervention()
    {
        // At E3 every hardening went to Reactor Alpha unless assigned. Since K5c the estimate
        // follows a setup once one exists, so the first assignment is the one that matters: after
        // it, Reactor Beta is set up for hardening and the planner sends the rest there itself.
        var queue = Run("e1-a-sustained.json");
        var manual = Run("e1-a-manual.json");

        Assert.That(manual.Assignments, Is.GreaterThanOrEqualTo(1));
        Assert.That(manual.Interventions, Is.EqualTo(queue.Interventions + manual.Assignments));
        Assert.That(Working(manual, "reactor_b"), Is.GreaterThan(Working(queue, "reactor_b")));
        Assert.That(manual.Demands.All(d => d.ReadyAtTick is not null), Is.True);
    }

    [Test]
    public void InSituationA_TheChangeoverAwareEstimate_PutsEveryReactorAndFactoryToWork_UnderQueueOrder()
    {
        // K5c. Before it, Reactor Beta never worked in A and Reactor Alpha switched eight times.
        // Each built facility's opening setup now draws the work that matches it.
        var queue = Run("e1-a-sustained.json");

        Assert.That(
            new[] { "reactor_a", "reactor_b", "factory_a", "factory_b", "factory_c" }.Select(f => Working(queue, f)),
            Has.All.GreaterThan(0));
        Assert.That(ChangeoversAt(queue, "reactor_a").Count, Is.LessThanOrEqualTo(1));
        Assert.That(queue.Changeovers.Sum(c => c.Count), Is.LessThan(39 / 2), "under half of E1's 39");
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
