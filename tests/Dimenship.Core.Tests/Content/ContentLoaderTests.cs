using Dimenship.Core.Content;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// The loader's two phases, and every link rule as a test. Each starts from a tree that loads
/// clean and breaks exactly one thing, so a failure names the rule rather than the fixture.
/// </summary>
public class ContentLoaderTests
{
    private static IReadOnlyList<string> Messages(ContentLoadResult result) =>
        result.Errors.Select(e => e.ToString()).ToList();

    [Test]
    public void AValidTree_LoadsWithNoErrors()
    {
        var result = ContentTree.Valid().Load();

        Assert.That(Messages(result), Is.Empty);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Catalog!.ContentVersion, Is.EqualTo("test-1"));
        Assert.That(result.Scenarios, Has.Count.EqualTo(1));
    }

    [Test]
    public void ElevenDanglingItemIds_AreElevenErrorsInOnePass()
    {
        // The whole reason errors are collected rather than thrown. A content author fixing eleven
        // references should see eleven messages, not eleven runs.
        var rows = string.Join(",\n", Enumerable.Range(0, 11).Select(i => $$"""
            {
              "id": "s{{i}}",
              "output": { "item": "alloy", "quantity": 1 },
              "inputs": [ { "item": "unobtanium_{{i}}", "quantity": 1 } ],
              "effortPerRun": 100,
              "energyPerRun": 0,
              "requiredFacilityType": "matter_reactor"
            }
            """));

        var result = ContentTree.Valid()
            .Write(ContentTree.Schematics, $$"""{ "schematics": [ {{rows}} ] }""")
            .Load();

        var dangling = result.Errors.Where(e => e.Message.Contains("unobtanium")).ToList();

        Assert.That(dangling, Has.Count.EqualTo(11), string.Join("\n", Messages(result)));
        Assert.That(dangling[0].File, Is.EqualTo(ContentTree.Schematics));
        Assert.That(dangling[0].Path, Is.EqualTo("schematics[0].inputs[0].item"));
    }

    [Test]
    public void AnUnknownField_IsALoadError()
    {
        // A typo must not silently become a default. This is the rule that makes the whole format
        // safe to hand to an author: a field that does nothing is reported as one.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Items, "\"holdCapacity\": 1000000", "\"holdCapasity\": 1000000")
            .Load();

        Assert.That(Messages(result), Is.Not.Empty);
        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Items && e.Message.Contains("holdCapasity")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AFractionalNumber_IsALoadError()
    {
        // Every number is an integer in milli-units, which is what lets the parser reject this
        // outright rather than rounding it somewhere nobody looks.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Schematics, "\"effortPerRun\": 1600", "\"effortPerRun\": 1600.5")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Schematics),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AnUnknownEnumName_IsALoadError_ThatNamesTheOnesThatWouldHaveWorked()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Schematics, "\"requiredFacilityType\": \"matter_reactor\"",
                "\"requiredFacilityType\": \"refinery\"")
            .Load();

        var error = result.Errors.Single(e => e.Path == "schematics[0].requiredFacilityType");

        Assert.That(error.Message, Does.Contain("matter_reactor"));
        Assert.That(error.Message, Does.Contain("mission_dock"));
    }

    [Test]
    public void AnIdWithAColon_IsRejected_WhichIsWhatKeepsTheTwoIdSpacesDisjoint()
    {
        // The authored tier mints ids with a "user:" prefix. A colon is unrepresentable under the
        // catalog's pattern, so a save can never smuggle a redefinition of a shipped schematic
        // past the catalog — disjoint by construction rather than by convention.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Schematics, "\"id\": \"smelt\"", "\"id\": \"user:smelt\"")
            .Load();

        var error = result.Errors.First(e => e.Path == "schematics[0].id");

        Assert.That(error.Message, Does.Contain(ContentCatalog.IdPattern));
    }

    [Test]
    public void ADuplicateIdWithinAFile_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Items, "\"id\": \"alloy\"", "\"id\": \"ore\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("declared twice")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void ASchematicThatEatsItsOwnOutput_IsRejected()
    {
        // Longer cycles stay the planner's business — it already reports a cyclic-schematic
        // shortage. This is the one case no plan can route around.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Schematics, "{ \"item\": \"ore\", \"quantity\": 400 }",
                "{ \"item\": \"alloy\", \"quantity\": 400 }")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("consumes its own output")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AFacilityThatDoesNoWorkPerTick_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Facilities, "\"workRatePerTick\": 100,\n      \"standingPowerDraw\": 150,\n      \"switchOverTicks\": 30,\n      \"bufferPermille\": 25,\n      \"commandable\": true",
                "\"workRatePerTick\": 0,\n      \"standingPowerDraw\": 150,\n      \"switchOverTicks\": 30,\n      \"bufferPermille\": 25,\n      \"commandable\": true")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "facilities[0].workRatePerTick"),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void ALineThatMovesNothingPerTick_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Transports, "\"throughputPerTick\": 100", "\"throughputPerTick\": 0")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "transports[0].throughputPerTick"),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AHoldThatIsNotOneOfTheScenariosStorages_IsRejected()
    {
        // The storage every plan routes material through. Today the engine takes the first one
        // declared, which is a convention a content author breaks by reordering an array.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"hold\": \"hold\"", "\"hold\": \"resource_storage\"")
            .Load();

        var error = result.Errors.Single(e => e.Path == "hold");

        Assert.That(error.Message, Does.Contain("resource_storage"));
    }

    [Test]
    public void ATaskQueuedOnAPassiveFacility_IsRejected_NamingTheRule()
    {
        // The GDD says three times that the extractor is not an automation node. Without the flag
        // it becomes commandable by construction the moment programs are installable, and the rule
        // then breaks by one extra row appearing in a target picker.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"archetype\": \"refinery\"", "\"archetype\": \"collector\"")
            .Edit(ContentTree.Scenario, "\"initialSchematic\": \"smelt\"", "\"initialSchematic\": null")
            .Load();

        var error = result.Errors.Single(e => e.Path == "initialTasks[0].executor");

        Assert.That(error.Message, Does.Contain("not commandable"));
        Assert.That(error.Message, Does.Contain("scheduled by nobody"));
    }

    [Test]
    public void AFacilityConfiguredForASchematicItCannotRun_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"archetype\": \"refinery\"", "\"archetype\": \"collector\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "facilities[0].initialSchematic"),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void OpeningStockThatDoesNotFit_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"quantity\": 1000 }", "\"quantity\": 9000000 }")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("holds at most")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void StandingDrawAboveCapacity_IsRejected()
    {
        // 4,000 of sink, 150 of idle facility and 200 of idle line, against a capacity of 300.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"energyCapacity\": 10000", "\"energyCapacity\": 300")
            .Load();

        var error = result.Errors.Single(e => e.Path == "energyCapacity");

        Assert.That(error.Message, Does.Contain("4350"));
    }

    [Test]
    public void AnUnlockedSchematicThatDoesNotExist_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"unlockedSchematics\": [ \"smelt\" ]",
                "\"unlockedSchematics\": [ \"smetl\" ]")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Path == "unlockedSchematics[0]"),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void TwoNodesInOneCell_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"column\": 1, \"row\": 0, \"badge\": \"1\"",
                "\"column\": 0, \"row\": 0, \"badge\": \"1\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("both sit at column 0, row 0")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AStorageNothingWouldDraw_IsRejected()
    {
        // Neither placed nor any facility's buffer. BaseGraphNodes returns nothing for one of
        // these rather than guessing, so it would vanish from the graph in silence.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"nameOverride\": \"Refinery Buffer\"",
                "\"nameOverride\": \"Orphan\"")
            .Edit(ContentTree.Scenario, "\"localStorage\": \"refinery_buffer\"",
                "\"localStorage\": \"hold\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("nothing would draw it")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void ARouteThatBeginsAndEndsInOnePlace_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"to\": \"refinery_buffer\",\n      \"builtAtStart\"",
                "\"to\": \"hold\",\n      \"builtAtStart\"")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("to itself")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void ATransferOnALineThatCannotMakeTheJourney_IsRejected()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario,
                "{ \"item\": \"ore\", \"from\": \"hold\", \"to\": \"refinery_buffer\", \"executor\": \"hold_to_refinery\" }",
                "{ \"item\": \"ore\", \"from\": \"refinery_buffer\", \"to\": \"hold\", \"executor\": \"hold_to_refinery\" }")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("not 'refinery_buffer' to 'hold'")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AShippedProgram_IsReported_BecauseTheLanguageIsNotReadYet()
    {
        // The file exists so the catalog field it feeds can arrive with the programming work. A
        // program appearing before then is reported rather than half-parsed.
        var result = ContentTree.Valid()
            .Write(ContentTree.Programs, """{ "programs": [ { "id": "balance_refining" } ] }""")
            .Load();

        Assert.That(
            result.Errors.Any(e => e.File == ContentTree.Programs),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void ACatalogFileTheManifestDoesNotList_IsALoadError()
    {
        var result = ContentTree.Valid()
            .Write(ContentTree.Manifest, """
                {
                  "contentVersion": "test-1",
                  "catalog": [
                    "items.json", "schematics.json", "facilities.json", "transports.json",
                    "storages.json", "sinks.json", "reactors.json", "programs.json"
                  ],
                  "scenarios": [ "vessel.json" ]
                }
                """)
            .Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("'strata.json' is not listed")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void AListedFileThatIsNotThere_IsALoadError()
    {
        var result = ContentTree.Valid().Remove(ContentTree.Strata).Load();

        Assert.That(
            result.Errors.Any(e => e.Message.Contains("listed but not present")),
            Is.True,
            string.Join("\n", Messages(result)));
    }

    [Test]
    public void MalformedJson_IsOneErrorAgainstTheFileThatHasIt()
    {
        var result = ContentTree.Valid().Write(ContentTree.Items, "{ \"items\": [ ").Load();

        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].File, Is.EqualTo(ContentTree.Items));
    }

    [Test]
    public void AnAbsentRunCount_IsAStandingOrder_RatherThanAMissingField()
    {
        var result = ContentTree.Valid().Load();

        var task = result.Scenarios.Single().InitialTasks.Single();

        Assert.That(task.Runs, Is.Null);
        Assert.That(task.Schematic, Is.EqualTo(new SchematicId("smelt")));
    }

    [Test]
    public void ARunCountOfZero_IsRejected_AndSaysWhatToWriteInstead()
    {
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "{ \"schematic\": \"smelt\", \"executor\": \"refinery_a\" }",
                "{ \"schematic\": \"smelt\", \"runs\": 0, \"executor\": \"refinery_a\" }")
            .Load();

        var error = result.Errors.Single(e => e.Path == "initialTasks[0].runs");

        Assert.That(error.Message, Does.Contain("standing order"));
    }

    [Test]
    public void AFailedLoad_ReturnsNoCatalog()
    {
        // A partially linked catalog is worse than none: everything downstream would treat it as
        // complete.
        var result = ContentTree.Valid()
            .Edit(ContentTree.Scenario, "\"hold\": \"hold\"", "\"hold\": \"nowhere\"")
            .Load();

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Catalog, Is.Null);
        Assert.That(result.Scenarios, Is.Empty);
    }
}
