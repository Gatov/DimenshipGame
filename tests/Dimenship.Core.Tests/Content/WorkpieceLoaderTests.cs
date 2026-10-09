using Dimenship.Core.Content;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// K3's loader rules (D2 Decision 4). The tree here adds one workpiece, a blank the refinery forms
/// from ore and finishes into alloy, and each test breaks exactly one thing about it.
/// </summary>
public class WorkpieceLoaderTests
{
    private const string Blank = """{ "id": "blank", "label": "Blank", "holdCapacity": 100000, "workpiece": true }""";

    private const string Form = """
        {
          "id": "form",
          "output": { "item": "blank", "quantity": 100 },
          "inputs": [ { "item": "ore", "quantity": 100 } ],
          "effortPerRun": 100,
          "energyPerRun": 0,
          "requiredFacilityType": "matter_reactor"
        }
        """;

    private const string Finish = """
        {
          "id": "finish",
          "output": { "item": "alloy", "quantity": 100 },
          "inputs": [ { "item": "blank", "quantity": 100 } ],
          "effortPerRun": 100,
          "energyPerRun": 0,
          "requiredFacilityType": "matter_reactor"
        }
        """;

    private const string Smelt = """
        {
          "id": "smelt",
          "output": { "item": "alloy", "quantity": 100 },
          "inputs": [ { "item": "ore", "quantity": 400 } ],
          "effortPerRun": 1600,
          "energyPerRun": 200,
          "requiredFacilityType": "matter_reactor"
        }
        """;

    private static MemoryContentFileSystem Tree(bool form = true, bool finish = true)
    {
        var schematics = new List<string> { Smelt };
        if (form)
        {
            schematics.Add(Form);
        }

        if (finish)
        {
            schematics.Add(Finish);
        }

        return ContentTree.Valid()
            .Edit(
                ContentTree.Items,
                """{ "id": "alloy", "label": "Alloy", "holdCapacity": 500000, "workpiece": false }""",
                """{ "id": "alloy", "label": "Alloy", "holdCapacity": 500000, "workpiece": false },""" + "\n" + Blank)
            .Write(ContentTree.Schematics, $$"""{ "schematics": [ {{string.Join(",\n", schematics)}} ] }""");
    }

    private static string Report(ContentLoadResult result) =>
        string.Join("\n", result.Errors.Select(e => e.ToString()));

    [Test]
    public void AWorkpieceMadeAndConsumed_AndPutNowhereItIsRefused_Loads()
    {
        var result = Tree().Load();

        Assert.That(result.Errors, Is.Empty, Report(result));
        Assert.That(result.Catalog!.Item(new("blank"))!.Workpiece, Is.True);
        Assert.That(result.Catalog.Item(new("ore"))!.Workpiece, Is.False);
    }

    [Test]
    public void AnItemWithoutTheWorkpieceFlag_IsRejected_RatherThanDefaultedToStorable()
    {
        var result = Tree()
            .Edit(ContentTree.Items, "\"holdCapacity\": 1000000, \"workpiece\": false", "\"holdCapacity\": 1000000")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Items && e.Path == "items[0].workpiece"),
            Is.True, Report(result));
    }

    [Test]
    public void AWorkpieceNothingProduces_IsRejected()
    {
        var result = Tree(form: false).Load();

        Assert.That(result.Errors.Any(e => e.Message.Contains("no schematic produces it")), Is.True, Report(result));
    }

    [Test]
    public void AWorkpieceNothingConsumes_IsRejected()
    {
        var result = Tree(finish: false).Load();

        Assert.That(result.Errors.Any(e => e.Message.Contains("no schematic consumes it")), Is.True, Report(result));
    }

    [Test]
    public void AWorkpieceAsAConstructionUnit_IsRejected()
    {
        var result = Tree()
            .Edit(ContentTree.Facilities, "\"constructionUnit\": null", "\"constructionUnit\": \"blank\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Facilities && e.Message.Contains("construction unit")),
            Is.True, Report(result));
    }

    [Test]
    public void AWorkpieceAsAStratumYield_IsRejected()
    {
        var result = Tree()
            .Write(ContentTree.Strata, """
                {
                  "strata": [
                    {
                      "id": "shelf",
                      "label": "Shelf",
                      "yields": [ { "item": "blank", "quantity": 100 } ],
                      "travelTicks": 10,
                      "energyCost": 0,
                      "hazardPermille": 0
                    }
                  ]
                }
                """)
            .Load();

        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Strata && e.Message.Contains("is a workpiece")),
            Is.True, Report(result));
    }

    [Test]
    public void AScenarioOpeningWithAWorkpieceInTheHold_IsRejected()
    {
        var result = Tree()
            .Edit(
                ContentTree.Scenario,
                "\"initial\": [ { \"item\": \"ore\", \"quantity\": 1000 } ]",
                "\"initial\": [ { \"item\": \"ore\", \"quantity\": 1000 }, { \"item\": \"blank\", \"quantity\": 100 } ]")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "storages[0].initial" && e.Message.Contains("is a workpiece")),
            Is.True, Report(result));
    }

    [Test]
    public void AScenarioTransferOfAWorkpieceToTheHold_IsRejected()
    {
        var result = Tree()
            .Edit(
                ContentTree.Scenario,
                """{ "item": "ore", "from": "hold", "to": "refinery_buffer", "executor": "hold_to_refinery" }""",
                """{ "item": "ore", "from": "hold", "to": "refinery_buffer", "executor": "hold_to_refinery" },""" +
                """{ "item": "blank", "from": "refinery_buffer", "to": "hold", "executor": "refinery_to_hold" }""")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "initialTransfers[1]" && e.Message.Contains("is a workpiece")),
            Is.True, Report(result));
    }
}
