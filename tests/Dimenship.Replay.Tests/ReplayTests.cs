using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using Dimenship.Core.State;
using NUnit.Framework;

namespace Dimenship.Replay.Tests;

/// <summary>
/// The replay harness (scheduling plan, M2). Every later scheduling ticket reports its effect as a
/// harness number against the M3 baseline, so the first thing pinned here is that one script on one
/// content tree gives one report.
/// </summary>
public class ReplayTests
{
    private static readonly ContentLoadResult Content =
        new JsonContentSource(new DirectoryContentFileSystem(Path.Combine(AppContext.BaseDirectory, "content")))
            .Load();

    private static ContentCatalog Catalog =>
        Content.Catalog
        ?? throw new InvalidOperationException(
            "the shipped content does not load:\n" + string.Join("\n", Content.Errors));

    private static ScriptLoadResult Parse(string json) => ReplayScript.Parse(json, Catalog, Content.Scenarios);

    private static ReplayScript Script(string json)
    {
        var result = Parse(json);
        Assert.That(result.Errors, Is.Empty, "the fixture script does not parse");
        return result.Script!;
    }

    private static ReplayResult Run(ReplayScript script) =>
        Replay.Run(Catalog, Content.Scenarios.Single(s => s.Id == script.Scenario), script);

    private static ReplayScript Smoke() =>
        Script(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "smoke.json")));

    [Test]
    public void OneScript_RunTwice_GivesByteIdenticalReports()
    {
        var first = ReplayReport.Format(Run(Smoke()));
        var second = ReplayReport.Format(Run(Smoke()));

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Does.Contain("Final state SHA-256: `"));
        Assert.That(first, Does.Not.Contain("\r"), "a report must not take the platform's newline");
    }

    [Test]
    public void TheSmokeScript_ExercisesEveryTable()
    {
        // A determinism test over a run where nothing happened would prove very little.
        var result = Run(Smoke());

        Assert.That(result.Demands.All(d => d.ReadyAtTick is not null), Is.True, "every demand should finish");
        Assert.That(result.Interventions, Is.EqualTo(result.Demands.Count));
        Assert.That(result.MaterialTiedUp, Is.Not.Empty);
        Assert.That(result.Changeovers.Any(c => c.Count > 0), Is.True, "nothing switched over");
    }

    [Test]
    public void ADemandNotReadyByTheEnd_IsReportedSo_NotDropped()
    {
        var result = Run(Script("""
            {
              "scenario": "default_vessel",
              "endTick": 10,
              "demands": [ { "id": "late", "tick": 0, "item": "component", "quantity": 4000 } ]
            }
            """));

        var late = result.Demands.Single();
        Assert.That(late.CommittedAtTick, Is.EqualTo(0));
        Assert.That(late.ReadyAtTick, Is.Null);
        Assert.That(late.Delivered, Is.EqualTo(0));
        Assert.That(ReplayReport.Format(result), Does.Contain("| late | component | 4000 | 0 | 0 | not ready |"));
    }

    [Test]
    public void AConstructionDemand_DeliversIntoTheSlot_AndTheSlotBuilds()
    {
        // The composer's build mode sends the unit to the slot's own buffer. Without that the unit
        // lands in the hold, the plan still reads ready, and the facility never builds — which is
        // exactly the silent difference between a harness and the game this test exists to stop.
        var result = Run(Script("""
            {
              "scenario": "default_vessel",
              "endTick": 2000,
              "demands": [
                { "id": "build", "tick": 0, "item": "factory_construction_unit", "quantity": 1000, "assemble": "factory_b" }
              ]
            }
            """));

        Assert.That(result.Demands.Single().ReadyAtTick, Is.Not.Null);
        var factoryB = result.FacilityTime.Single(f => f.Facility == new ExecutorId("factory_b"));
        Assert.That(factoryB.TicksByCategory.Sum(), Is.GreaterThan(0), "factory_b never counted a tick as built");
    }

    [Test]
    public void ChangeoverTicks_AgreeWithTheSwitchingColumn()
    {
        // Two readings of one thing, taken two ways — events for the count, status for the ticks
        // and the category. If they disagree, one of them is not measuring what it says.
        var result = Run(Smoke());
        var switching = (int)UtilizationCategory.SwitchingOver;

        foreach (var changeover in result.Changeovers)
        {
            var time = result.FacilityTime.Single(f => f.Facility == changeover.Facility);
            Assert.That(changeover.Ticks, Is.EqualTo(time.TicksByCategory[switching]), changeover.Facility.Value);
        }
    }

    [Test]
    public void AFacilityBuiltFromTheStart_AccountsForEveryTick()
    {
        var result = Run(Smoke());
        var extractor = result.FacilityTime.Single(f => f.Facility == new ExecutorId("extractor_01"));

        Assert.That(extractor.TicksByCategory.Sum(), Is.EqualTo(result.EndTick));
    }

    [Test]
    public void ScriptErrors_AreCollected_NotStoppedAtTheFirst()
    {
        var result = Parse("""
            {
              "scenario": "default_vessel",
              "endTick": 100,
              "demands": [
                { "id": "a", "tick": 0, "item": "unobtainium", "quantity": 1000 },
                { "id": "b", "tick": 100, "item": "component", "quantity": 1000 },
                { "id": "a", "tick": 0, "item": "component", "quantity": 0 }
              ]
            }
            """);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(4), string.Join("\n", result.Errors));
    }

    [Test]
    public void AnUnknownScenario_IsRefused()
    {
        var result = Parse("""{ "scenario": "nowhere", "endTick": 10, "demands": [] }""");

        Assert.That(result.Errors, Has.Some.Contains("nowhere"));
    }

    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [ { "id": "x", "tick": 0, "item": "component", "quantity": 1.5 } ] }""")]
    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [], "speed": 4 }""")]
    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [ { "id": "x", "tick": 0, "item": "component", "quantity": 1000, "priority": 1 } ] }""")]
    public void AFractionalNumberOrAnUnknownField_IsRefused(string json)
    {
        var result = Parse(json);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }
}
