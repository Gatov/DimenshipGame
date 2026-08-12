using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// The test that makes this stage safe to land. Until it passes, <c>content/</c> is a guess; once
/// it passes, switching the engine over is a substitution rather than a rewrite.
/// <para>
/// It compares value for value rather than by round-tripping through anything, because a
/// round-trip would pass just as happily if both sides were wrong in the same way.
/// </para>
/// </summary>
public class DefaultVesselContentTests
{
    private static (ContentCatalog Catalog, Scenario Scenario) Shipped()
    {
        var result = new JsonContentSource(DirectoryContentFileSystem.Shipped()).Load();

        Assert.That(
            result.Errors.Select(e => e.ToString()).ToList(),
            Is.Empty,
            "the content the game ships does not load");

        return (result.Catalog!, result.Scenarios.Single(s => s.Id == "default_vessel"));
    }

    private static string Describe(ItemAmount amount) => $"{amount.Item}:{amount.Quantity}";

    private static List<string> Describe(IEnumerable<ItemAmount> amounts) =>
        amounts.Select(Describe).ToList();

    [Test]
    public void TheCatalogsItems_AreTheDefaultWorlds()
    {
        var (catalog, _) = Shipped();
        var world = WorldDefinition.CreateDefault();

        Assert.That(catalog.Items, Is.EqualTo(world.Items));
    }

    [Test]
    public void TheCatalogsSchematics_AreTheDefaultWorlds()
    {
        var (catalog, _) = Shipped();
        var world = WorldDefinition.CreateDefault();

        // Field by field: SchematicDefinition holds its inputs in a list, and record equality
        // compares a list by reference, so Is.EqualTo on the records would pass for the wrong
        // reason — or rather, would fail for one.
        List<string> Flatten(IReadOnlyList<SchematicDefinition> schematics) =>
            schematics.Select(s =>
                $"{s.Id}|{Describe(s.Output)}|{string.Join(",", Describe(s.Inputs))}|" +
                $"{s.EffortPerRun.Value}|{s.EnergyPerRun.Value}|{s.RequiredFacilityType}").ToList();

        Assert.That(
            Flatten(catalog.Schematics.All),
            Is.EqualTo(Flatten(world.Schematics.All)));
    }

    [Test]
    public void TheScenariosStorages_AreTheDefaultWorlds()
    {
        var (catalog, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        var authored = scenario.Storages.Select(s =>
        {
            var archetype = catalog.Storage(s.Archetype)!;
            return $"{s.Id}|{s.NameOverride ?? archetype.Label}|{archetype.CapacityPermille}|" +
                string.Join(",", Describe(s.Initial));
        }).ToList();

        var current = world.Storages.Select(s =>
            $"{s.Id}|{s.Label}|{s.CapacityPermille}|{string.Join(",", Describe(s.Initial))}").ToList();

        Assert.That(authored, Is.EqualTo(current));
    }

    [Test]
    public void TheScenariosFacilities_AreTheDefaultWorlds()
    {
        var (catalog, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        var authored = scenario.Facilities.Select(f =>
        {
            var archetype = catalog.Facility(f.Archetype)!;
            return $"{f.Id}|{f.NameOverride ?? archetype.Label}|{archetype.Type}|{f.LocalStorage}|" +
                $"{archetype.WorkRatePerTick}|{archetype.StandingPowerDraw}|" +
                $"{archetype.SwitchOverTicks}|{f.InitialSchematic?.ToString() ?? "-"}";
        }).ToList();

        var current = world.Producers.Select(p =>
            $"{p.Id}|{p.Label}|{p.Type}|{p.LocalStorage}|{p.WorkRatePerTick}|" +
            $"{p.StandingPowerDraw}|{p.SwitchOverTicks}|{p.InitialSchematic?.ToString() ?? "-"}").ToList();

        Assert.That(authored, Is.EqualTo(current));
    }

    [Test]
    public void TheScenariosRoutes_AreTheDefaultWorlds()
    {
        var (catalog, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        var authored = scenario.Routes.Select(r =>
        {
            var archetype = catalog.Transport(r.Archetype)!;
            return $"{r.Id}|{r.NameOverride ?? archetype.Label}|{r.From}|{r.To}|" +
                $"{archetype.ThroughputPerTick}|{archetype.StandingPowerDraw}";
        }).ToList();

        var current = world.Transports.Select(t =>
            $"{t.Id}|{t.Label}|{t.From}|{t.To}|{t.ThroughputPerTick}|{t.StandingPowerDraw}").ToList();

        Assert.That(authored, Is.EqualTo(current));
    }

    [Test]
    public void TheEnergyCapacity_TheSinks_AndTheUnlockSet_AreTheDefaultWorlds()
    {
        var (catalog, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        Assert.That(scenario.EnergyCapacity, Is.EqualTo(world.EnergyCapacity));
        Assert.That(scenario.UnlockedSchematics, Is.EqualTo(world.UnlockedSchematics));

        var authored = scenario.Sinks
            .Select(id => catalog.Sinks.Single(s => s.Id == id))
            .ToList();

        Assert.That(authored, Is.EqualTo(world.Sinks));
    }

    [Test]
    public void TheHold_IsTheStorageTheEngineWouldHavePickedByBeingDeclaredFirst()
    {
        var (_, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        // Named rather than positional from here on. This asserts the name it was given is the
        // storage the current convention resolves to, which is the only thing that could break
        // when the engine stops reading Storages[0].
        Assert.That(scenario.Hold, Is.EqualTo(world.Storages[0].Id));
    }

    [Test]
    public void TheScenariosTransfers_AreTheDefaultWorlds()
    {
        var (_, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        var authored = scenario.InitialTransfers
            .Select(t => $"{t.Item}|{t.Quantity?.ToString() ?? "standing"}|{t.From}|{t.To}|{t.Executor}")
            .ToList();

        var current = world.InitialTransfers
            .Select(t => $"{t.Item}|{t.Quantity?.ToString() ?? "standing"}|{t.From}|{t.To}|{t.Executor}")
            .ToList();

        Assert.That(authored, Is.EqualTo(current));
    }

    [Test]
    public void TheScenariosTasks_AreTheDefaultWorlds_LessTheExtractorsWhichIsNoLongerATask()
    {
        var (catalog, scenario) = Shipped();
        var world = WorldDefinition.CreateDefault();

        var authored = scenario.InitialTasks
            .Select(t => $"{t.Schematic}|{t.Runs?.ToString() ?? "standing"}|{t.Executor}")
            .ToList();

        var current = world.InitialTasks
            .Where(t => t.Executor != WorldDefinition.Extractor01)
            .Select(t => $"{t.Schematic}|{t.Runs?.ToString() ?? "standing"}|{t.Executor}")
            .ToList();

        Assert.That(authored, Is.EqualTo(current));

        // The one place the scenario deliberately does not match CreateDefault, and the reason is
        // a rule this same stage introduces: the extractor's archetype is not commandable, so a
        // task queued on it is a content error. What it produces is what it is configured with,
        // and that configuration is its standing order — which is exactly the read-only source the
        // GDD describes, rather than a player-scheduled job nobody scheduled.
        //
        // The seeder is where this has to be honoured: a passive facility with an initial
        // schematic runs it indefinitely without a queued task. Until then, the engine still runs
        // on WorldDefinition, where the extractor's task is still a task.
        var extractor = scenario.Facilities.Single(f => f.Id == WorldDefinition.Extractor01);

        Assert.That(catalog.Facility(extractor.Archetype)!.Commandable, Is.False);
        Assert.That(extractor.InitialSchematic, Is.EqualTo(WorldDefinition.ExtractHydrogen));
        Assert.That(
            world.InitialTasks.Count - scenario.InitialTasks.Count,
            Is.EqualTo(1),
            "the extractor is the only task the scenario drops");
    }

    [Test]
    public void ThePlacements_AreTheDefaultLayouts()
    {
        var (_, scenario) = Shipped();
        var layout = BaseGraphLayout.ForDefaultWorld();

        var facilities = scenario.Facilities.ToDictionary(f => f.Id, f => f.Placement);
        Assert.That(facilities, Is.EqualTo(layout.Producers));

        var storages = scenario.Storages
            .Where(s => s.Placement is not null)
            .ToDictionary(s => s.Id, s => s.Placement!);

        Assert.That(storages, Is.EqualTo(layout.Storages));
    }

    [Test]
    public void EveryFacilityBuffer_TakesItsCapacityFromTheFacilityThatWorksIt()
    {
        // BufferPermille is on the facility archetype and the buffer's capacity is on the storage
        // archetype, so the two could drift. On this vessel they agree, and a change to one that
        // forgot the other would show up here rather than as a buffer that holds the wrong amount.
        var (catalog, scenario) = Shipped();

        foreach (var facility in scenario.Facilities)
        {
            var archetype = catalog.Facility(facility.Archetype)!;
            var buffer = scenario.Storages.Single(s => s.Id == facility.LocalStorage);
            var storage = catalog.Storage(buffer.Archetype)!;

            Assert.That(
                storage.CapacityPermille,
                Is.EqualTo(archetype.BufferPermille),
                $"'{facility.Id}' works a {storage.CapacityPermille} permille buffer, and its " +
                $"archetype says {archetype.BufferPermille}");
        }
    }

    [Test]
    public void TheShippedContent_HasAVersion_AndNoShippedPrograms()
    {
        var (catalog, _) = Shipped();

        Assert.That(catalog.ContentVersion, Is.Not.Empty);
        Assert.That(catalog.Reactors, Is.Empty, "energy is still a constant");
        Assert.That(catalog.Strata, Is.Empty, "acquisition is still not a system");
    }
}
