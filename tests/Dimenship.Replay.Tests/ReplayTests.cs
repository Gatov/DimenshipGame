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
        Assert.That(
            result.Unfinished.Where(u => u.Demand == "late"), Is.Not.Empty,
            "a demand that is not ready must say which of its tasks are still open");
    }

    [Test]
    public void AReadyDemand_LeavesNoUnfinishedWork()
    {
        Assert.That(Run(Smoke()).Unfinished, Is.Empty);
    }

    [Test]
    public void ADemandsPriority_IsLentToItsPlan_AtCommit()
    {
        var result = Run(Script("""
            {
              "scenario": "default_vessel",
              "endTick": 2,
              "demands": [ { "id": "urgent", "tick": 0, "item": "component", "quantity": 1000, "priority": "High" } ]
            }
            """));

        Assert.That(result.Demands.Single().Demand.Priority, Is.EqualTo(Priority.High));
        Assert.That(ReplayReport.Format(result), Does.Contain("| High |"));
        Assert.That(result.Interventions, Is.EqualTo(1), "a priority given with the demand is part of one command");
    }

    [TestCase("smoke.json")]
    [TestCase("situation-a.json")]
    [TestCase("situation-b.json")]
    [TestCase("situation-b-alone.json")]
    [TestCase("situation-b-priority.json")]
    [TestCase("situation-a-priority.json")]
    [TestCase("situation-b-contested.json")]
    [TestCase("situation-b-hold.json")]
    public void EveryShippedScript_StillParsesAgainstTheShippedContent(string file)
    {
        // A content rename would otherwise surface as a baseline nobody can rerun.
        var result = Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", file)));

        Assert.That(result.Errors, Is.Empty);
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
    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [ { "id": "x", "tick": 0, "item": "component", "quantity": 1000, "urgency": "High" } ] }""")]
    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [ { "id": "x", "tick": 0, "item": "component", "quantity": 1000, "priority": "Urgent" } ] }""")]
    [TestCase("""{ "scenario": "default_vessel", "endTick": 10, "demands": [ { "id": "x", "tick": 0, "item": "component", "quantity": 1000, "priority": "2" } ] }""")]
    public void AFractionalNumberOrAnUnknownField_IsRefused(string json)
    {
        var result = Parse(json);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }

    [Test]
    public void AScriptedHold_GoesThroughTheCommandSurface_AndCountsAsAnIntervention()
    {
        var script = Script(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "situation-b-hold.json")));

        var result = Run(script);

        Assert.That(result.Commands.Select(c => (c.Command.Kind, c.Accepted)), Is.EqualTo(new[]
        {
            (ScriptedCommandKind.Hold, true),
            (ScriptedCommandKind.Release, true),
        }));
        Assert.That(result.Interventions, Is.EqualTo(script.Demands.Count + 2));
        Assert.That(ReplayReport.Format(result), Does.Contain("| 600 | hold | upgrade_components | — | accepted |"));
        Assert.That(
            result.FacilityTime.Single(f => f.Facility.Value == "factory_a").TicksByCategory[(int)UtilizationCategory.Held],
            Is.GreaterThan(0), "the hold never reached Factory Alpha");
    }

    /// <summary>situation-b-hold.json with the release dropped and the run cut to <paramref name="endTick"/>.</summary>
    private static ReplayScript HeldForGood(long endTick)
    {
        var tree = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "situation-b-hold.json")))!;
        tree["endTick"] = endTick;
        tree["commands"]!.AsArray().RemoveAt(1);
        return Script(tree.ToJsonString());
    }

    [Test]
    public void AHeldPlansStock_IsNamedBehindTheRunsItBlocks()
    {
        // C0's finding, now reported by the run itself: the held upgrade's cargo fills Factory
        // Alpha's buffer, and the construction runs there say whose stock is in the way (K8).
        var result = Run(HeldForGood(1500));

        var atFactory = result.Unfinished.Where(u => u.Executor.Value == "factory_a").ToList();
        Assert.That(
            atFactory.Single(u => u.Demand == "upgrade_components").Reason, Is.EqualTo(PostponeReason.SafetyLock),
            "the upgrade's own runs stop for the hold, and name nobody");

        var blocked = atFactory.Where(u => u.Demand != "upgrade_components").ToList();
        Assert.That(blocked.Select(u => u.Demand), Is.EquivalentTo(new[] { "build_reactor_b", "build_dock_a" }));
        Assert.That(blocked, Has.All.Matches<UnfinishedTask>(u =>
            u.Reason == PostponeReason.DestinationFull && u.Behind == "upgrade_components"));
    }

    [Test]
    public void ADemandStarvedForAnHour_RaisesAWaitingAlert_NamingTheDemandItWaitsBehind()
    {
        var result = Run(HeldForGood(6000));

        Assert.That(
            result.WaitingAlerts.Select(a => (a.Demand, a.Behind, a.Reason, a.ClearedAtTick)),
            Is.EquivalentTo(new (string, string, PostponeReason?, long?)[]
            {
                ("build_reactor_b", "upgrade_components", PostponeReason.DestinationFull, null),
                ("build_dock_a", "upgrade_components", PostponeReason.DestinationFull, null),
            }));
        Assert.That(ReplayReport.Format(result), Does.Contain("| build_reactor_b | upgrade_components | DestinationFull |"));
    }

    [Test]
    public void AStaleCommand_IsReportedRefused_AndCountsForNothing()
    {
        // The plan completes long before tick 900, so cancelling it is a command against a world
        // that moved on: the kernel answers, and the run goes on.
        var result = Run(Script("""
            {
              "scenario": "default_vessel",
              "endTick": 1000,
              "demands": [ { "id": "small", "tick": 0, "item": "component", "quantity": 1000 } ],
              "commands": [
                { "tick": 0, "command": "priority", "demand": "small", "priority": "High" },
                { "tick": 900, "command": "cancel", "demand": "small" }
              ]
            }
            """));

        Assert.That(result.Demands.Single().ReadyAtTick, Is.Not.Null, "fixture: the plan should finish first");
        Assert.That(result.Commands[0].Accepted, Is.True);
        Assert.That(result.Commands[1].Refusal, Does.Contain("No active plan"));
        Assert.That(result.Interventions, Is.EqualTo(2), "one demand and one accepted command");
        Assert.That(ReplayReport.Format(result), Does.Contain("| 900 | cancel | small | — | refused: "));
    }

    [Test]
    public void CommandErrors_AreCollected_AndEachKindTakesOnlyItsOwnFields()
    {
        var result = Parse("""
            {
              "scenario": "default_vessel",
              "endTick": 100,
              "demands": [ { "id": "a", "tick": 10, "item": "component", "quantity": 1000 } ],
              "commands": [
                { "tick": 5, "command": "hold", "demand": "a" },
                { "tick": 20, "command": "hold", "demand": "nobody" },
                { "tick": 20, "command": "pause", "demand": "a" },
                { "tick": 20, "command": "hold", "demand": "a", "quantity": 10 },
                { "tick": 20, "command": "amend", "demand": "a" },
                { "tick": 20, "command": "reassign", "demand": "a", "to": "a", "storage": "nowhere", "item": "component", "quantity": 5 },
                { "tick": 20, "command": "priority", "demand": "a", "priority": "Urgent" }
              ]
            }
            """);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors, Is.EqualTo(new[]
        {
            "commands[0].demand: demand 'a' is not committed until tick 10.",
            "commands[1].demand: no demand 'nobody' in this script.",
            "commands[2].command: 'pause' is not one of priority, hold, release, cancel, amend, relinquish, reassign.",
            "commands[3].quantity: the 'hold' command takes no quantity.",
            "commands[4].quantity: missing; the 'amend' command needs one.",
            "commands[5].storage: no storage 'nowhere' in scenario 'default_vessel'.",
            "commands[6].priority: 'Urgent' is not one of Low, Normal, High, Critical.",
        }));
    }
}
