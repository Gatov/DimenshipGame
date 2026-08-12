using System.Text.Json;
using System.Text.RegularExpressions;
using Dimenship.Core.Content.Json;
using Dimenship.Core.Presentation;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Content;

/// <summary>
/// Reads a content tree into a catalog and its scenarios.
/// <para>
/// Two phases, and the second is the point. <b>Parse</b> turns each file into records: malformed
/// JSON, an unknown field, a fractional number and an unknown enum name are all errors here.
/// <b>Link</b> resolves every id against every other file and checks every invariant.
/// </para>
/// <para>
/// Errors are <b>collected, not thrown on the first one</b>. A content author fixing eleven
/// dangling item ids should see eleven messages, not eleven runs. That is the difference between
/// this and the engine constructor, which still throws — a constructor is meeting a programmer,
/// and an exception is the right shape for a programmer error.
/// </para>
/// </summary>
public sealed class JsonContentSource : IContentSource
{
    public const string ManifestPath = "manifest.json";

    /// <summary>
    /// The catalog files that must be listed and must exist. A catalog field with no file is a
    /// field the loader cannot load, so the set is fixed here rather than inferred from whatever
    /// the manifest happens to name.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredCatalogFiles = new[]
    {
        "items.json",
        "schematics.json",
        "facilities.json",
        "transports.json",
        "storages.json",
        "sinks.json",
        "reactors.json",
        "programs.json",
        "strata.json",
    };

    private static readonly Regex IdPattern = new(ContentCatalog.IdPattern, RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, FacilityType> FacilityTypes =
        new Dictionary<string, FacilityType>
        {
            ["extractor"] = FacilityType.Extractor,
            ["matter_reactor"] = FacilityType.MatterReactor,
            ["factory"] = FacilityType.Factory,
            ["mission_dock"] = FacilityType.MissionDock,
        };

    private readonly IContentFileSystem _files;

    public JsonContentSource(IContentFileSystem files) => _files = files;

    public ContentLoadResult Load()
    {
        var errors = new List<ContentError>();

        var manifest = Parse(ManifestPath, ContentJsonContext.Default.ManifestFile, errors);
        if (manifest is null)
        {
            return ContentLoadResult.Failed(errors);
        }

        var version = Required(manifest.ContentVersion, ManifestPath, "contentVersion", errors);
        var catalogFiles = ManifestCatalogFiles(manifest, errors);
        var scenarioFiles = manifest.Scenarios ?? Array.Empty<string>();
        if (manifest.Scenarios is null || manifest.Scenarios.Count == 0)
        {
            errors.Add(new ContentError(ManifestPath, "scenarios", "no scenarios are listed."));
        }

        if (errors.Count > 0)
        {
            return ContentLoadResult.Failed(errors);
        }

        // Parse everything before linking anything. Linking against a file that failed to parse
        // reports the same dangling id once per reference, which buries the one error that matters.
        var items = Parse(catalogFiles["items.json"], ContentJsonContext.Default.ItemsFile, errors);
        var schematics = Parse(catalogFiles["schematics.json"], ContentJsonContext.Default.SchematicsFile, errors);
        var facilities = Parse(catalogFiles["facilities.json"], ContentJsonContext.Default.FacilitiesFile, errors);
        var transports = Parse(catalogFiles["transports.json"], ContentJsonContext.Default.TransportsFile, errors);
        var storages = Parse(catalogFiles["storages.json"], ContentJsonContext.Default.StoragesFile, errors);
        var sinks = Parse(catalogFiles["sinks.json"], ContentJsonContext.Default.SinksFile, errors);
        var reactors = Parse(catalogFiles["reactors.json"], ContentJsonContext.Default.ReactorsFile, errors);
        var programs = Parse(catalogFiles["programs.json"], ContentJsonContext.Default.ProgramsFile, errors);
        var strata = Parse(catalogFiles["strata.json"], ContentJsonContext.Default.StrataFile, errors);

        var scenarioSources = new List<(string Path, ScenarioFile File)>();
        foreach (var relative in scenarioFiles)
        {
            var path = $"scenarios/{relative}";
            var parsed = Parse(path, ContentJsonContext.Default.ScenarioFile, errors);
            if (parsed is not null)
            {
                scenarioSources.Add((path, parsed));
            }
        }

        if (errors.Count > 0)
        {
            return ContentLoadResult.Failed(errors);
        }

        var catalog = Link(
            version!, items!, schematics!, facilities!, transports!, storages!, sinks!, reactors!,
            programs!, strata!, catalogFiles, errors);

        var scenarios = new List<Scenario>();
        foreach (var (path, file) in scenarioSources)
        {
            var scenario = LinkScenario(catalog, file, path, errors);
            if (scenario is not null)
            {
                scenarios.Add(scenario);
            }
        }

        return errors.Count > 0
            ? ContentLoadResult.Failed(errors)
            : new ContentLoadResult(catalog, scenarios, errors);
    }

    private IReadOnlyDictionary<string, string> ManifestCatalogFiles(
        ManifestFile manifest, List<ContentError> errors)
    {
        var listed = manifest.Catalog ?? Array.Empty<string>();
        var byName = new Dictionary<string, string>();

        foreach (var relative in listed)
        {
            var name = relative.Split('/')[^1];
            if (!RequiredCatalogFiles.Contains(name))
            {
                errors.Add(new ContentError(
                    ManifestPath, "catalog", $"'{relative}' is not a catalog file this loader reads."));
                continue;
            }

            byName[name] = $"catalog/{relative}";
        }

        foreach (var required in RequiredCatalogFiles)
        {
            if (!byName.ContainsKey(required))
            {
                errors.Add(new ContentError(
                    ManifestPath, "catalog", $"'{required}' is not listed."));
                continue;
            }

            if (!_files.Exists(byName[required]))
            {
                errors.Add(new ContentError(
                    ManifestPath, "catalog", $"'{required}' is listed but not present."));
            }
        }

        return byName;
    }

    private T? Parse<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,
        List<ContentError> errors)
        where T : class
    {
        if (!_files.Exists(path))
        {
            errors.Add(new ContentError(path, string.Empty, "the file is missing."));
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(_files.ReadAllText(path), type);
            if (parsed is null)
            {
                errors.Add(new ContentError(path, string.Empty, "the file is empty."));
            }

            return parsed;
        }
        catch (JsonException failure)
        {
            // The message carries the position and the offending member; the path carries the file.
            errors.Add(new ContentError(path, failure.Path ?? string.Empty, failure.Message));
            return null;
        }
    }

    private static ContentCatalog Link(
        string version,
        ItemsFile items,
        SchematicsFile schematics,
        FacilitiesFile facilities,
        TransportsFile transports,
        StoragesFile storages,
        SinksFile sinks,
        ReactorsFile reactors,
        ProgramsFile programs,
        StrataFile strata,
        IReadOnlyDictionary<string, string> paths,
        List<ContentError> errors)
    {
        var itemList = LinkItems(items, paths["items.json"], errors);
        var known = new HashSet<ItemId>();
        foreach (var item in itemList)
        {
            known.Add(item.Id);
        }

        var schematicList = LinkSchematics(schematics, paths["schematics.json"], known, errors);
        var facilityList = LinkFacilities(facilities, paths["facilities.json"], errors);
        var transportList = LinkTransports(transports, paths["transports.json"], errors);
        var storageList = LinkStorages(storages, paths["storages.json"], errors);
        var sinkList = LinkSinks(sinks, paths["sinks.json"], errors);
        var reactorList = LinkReactors(reactors, paths["reactors.json"], known, errors);
        var stratumList = LinkStrata(strata, paths["strata.json"], known, errors);

        // The envelope is read; the language is not. Reporting a program rather than half-parsing
        // one keeps the file in place without inventing a schema the programming work has to live
        // with.
        if (programs.Programs is { Count: > 0 })
        {
            errors.Add(new ContentError(
                paths["programs.json"],
                "programs",
                $"{programs.Programs.Count} shipped program(s) are declared, and the program " +
                "language is not read yet. The file exists so the field it feeds can arrive with " +
                "the programming work; until then it must be empty."));
        }

        return new ContentCatalog(
            version,
            new SchematicCatalog(schematicList),
            itemList,
            storageList,
            facilityList,
            transportList,
            sinkList,
            reactorList,
            stratumList);
    }

    private static IReadOnlyList<ItemDefinition> LinkItems(
        ItemsFile file, string path, List<ContentError> errors)
    {
        var result = new List<ItemDefinition>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Items?.Count ?? 0); i++)
        {
            var dto = file.Items![i];
            var at = $"items[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var capacity = Positive(dto.HoldCapacity, path, $"{at}.holdCapacity", errors);

            if (id is null || label is null || capacity is null)
            {
                continue;
            }

            result.Add(new ItemDefinition(new ItemId(id), label, capacity.Value));
        }

        return result;
    }

    private static IReadOnlyList<SchematicDefinition> LinkSchematics(
        SchematicsFile file, string path, HashSet<ItemId> items, List<ContentError> errors)
    {
        var result = new List<SchematicDefinition>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Schematics?.Count ?? 0); i++)
        {
            var dto = file.Schematics![i];
            var at = $"schematics[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var output = Amount(dto.Output, path, $"{at}.output", items, errors);
            var effort = Positive(dto.EffortPerRun, path, $"{at}.effortPerRun", errors);
            var energy = NonNegative(dto.EnergyPerRun, path, $"{at}.energyPerRun", errors);
            var type = FacilityTypeOf(dto.RequiredFacilityType, path, $"{at}.requiredFacilityType", errors);

            var inputs = new List<ItemAmount>();
            for (var j = 0; j < (dto.Inputs?.Count ?? 0); j++)
            {
                var input = Amount(dto.Inputs![j], path, $"{at}.inputs[{j}]", items, errors);
                if (input is { } value)
                {
                    inputs.Add(value);
                }
            }

            if (output is { } produced)
            {
                foreach (var input in inputs)
                {
                    if (input.Item == produced.Item)
                    {
                        // Longer cycles stay the planner's business; it already reports them as a
                        // cyclic-schematic shortage. A schematic that eats its own output is the
                        // one case no plan can route around.
                        errors.Add(new ContentError(
                            path, at, $"consumes its own output '{produced.Item}'."));
                    }
                }
            }

            if (id is null || output is null || effort is null || energy is null || type is null)
            {
                continue;
            }

            result.Add(new SchematicDefinition
            {
                Id = new SchematicId(id),
                Output = output.Value,
                Inputs = inputs,
                EffortPerRun = new WorkAmount(effort.Value),
                EnergyPerRun = new EnergyAmount(energy.Value),
                RequiredFacilityType = type.Value,
            });
        }

        return result;
    }

    private static IReadOnlyList<FacilityArchetype> LinkFacilities(
        FacilitiesFile file, string path, List<ContentError> errors)
    {
        var result = new List<FacilityArchetype>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Facilities?.Count ?? 0); i++)
        {
            var dto = file.Facilities![i];
            var at = $"facilities[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var type = FacilityTypeOf(dto.Type, path, $"{at}.type", errors);
            var rate = Positive(dto.WorkRatePerTick, path, $"{at}.workRatePerTick", errors);
            var draw = NonNegative(dto.StandingPowerDraw, path, $"{at}.standingPowerDraw", errors);
            var switchOver = NonNegative(dto.SwitchOverTicks, path, $"{at}.switchOverTicks", errors);
            var buffer = Positive(dto.BufferPermille, path, $"{at}.bufferPermille", errors);
            var commandable = Flag(dto.Commandable, path, $"{at}.commandable", errors);

            if (id is null || label is null || type is null || rate is null || draw is null
                || switchOver is null || buffer is null || commandable is null)
            {
                continue;
            }

            result.Add(new FacilityArchetype(
                new FacilityArchetypeId(id), label, type.Value, rate.Value, draw.Value,
                switchOver.Value, buffer.Value, commandable.Value));
        }

        return result;
    }

    private static IReadOnlyList<TransportArchetype> LinkTransports(
        TransportsFile file, string path, List<ContentError> errors)
    {
        var result = new List<TransportArchetype>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Transports?.Count ?? 0); i++)
        {
            var dto = file.Transports![i];
            var at = $"transports[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var throughput = Positive(dto.ThroughputPerTick, path, $"{at}.throughputPerTick", errors);
            var draw = NonNegative(dto.StandingPowerDraw, path, $"{at}.standingPowerDraw", errors);

            if (id is null || label is null || throughput is null || draw is null)
            {
                continue;
            }

            result.Add(new TransportArchetype(
                new TransportArchetypeId(id), label, throughput.Value, draw.Value));
        }

        return result;
    }

    private static IReadOnlyList<StorageArchetype> LinkStorages(
        StoragesFile file, string path, List<ContentError> errors)
    {
        var result = new List<StorageArchetype>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Storages?.Count ?? 0); i++)
        {
            var dto = file.Storages![i];
            var at = $"storages[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var capacity = Positive(dto.CapacityPermille, path, $"{at}.capacityPermille", errors);

            if (id is null || label is null || capacity is null)
            {
                continue;
            }

            result.Add(new StorageArchetype(new StorageArchetypeId(id), label, capacity.Value));
        }

        return result;
    }

    private static IReadOnlyList<PowerSinkDefinition> LinkSinks(
        SinksFile file, string path, List<ContentError> errors)
    {
        var result = new List<PowerSinkDefinition>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Sinks?.Count ?? 0); i++)
        {
            var dto = file.Sinks![i];
            var at = $"sinks[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var draw = NonNegative(dto.PowerDraw, path, $"{at}.powerDraw", errors);

            if (id is null || label is null || draw is null)
            {
                continue;
            }

            result.Add(new PowerSinkDefinition(new PowerSinkId(id), label, draw.Value));
        }

        return result;
    }

    private static IReadOnlyList<ReactorArchetype> LinkReactors(
        ReactorsFile file, string path, HashSet<ItemId> items, List<ContentError> errors)
    {
        var result = new List<ReactorArchetype>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Reactors?.Count ?? 0); i++)
        {
            var dto = file.Reactors![i];
            var at = $"reactors[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var fuel = KnownItem(dto.Fuel, path, $"{at}.fuel", items, errors);
            var perTick = Positive(dto.FuelPerTick, path, $"{at}.fuelPerTick", errors);
            var perFuel = Positive(dto.EnergyPerFuel, path, $"{at}.energyPerFuel", errors);
            var ceiling = Positive(dto.CapacityCeiling, path, $"{at}.capacityCeiling", errors);

            if (id is null || label is null || fuel is null || perTick is null || perFuel is null
                || ceiling is null)
            {
                continue;
            }

            result.Add(new ReactorArchetype(
                new ReactorArchetypeId(id), label, fuel.Value, perTick.Value, perFuel.Value,
                ceiling.Value));
        }

        return result;
    }

    private static IReadOnlyList<StratumDefinition> LinkStrata(
        StrataFile file, string path, HashSet<ItemId> items, List<ContentError> errors)
    {
        var result = new List<StratumDefinition>();
        var seen = new HashSet<string>();

        for (var i = 0; i < (file.Strata?.Count ?? 0); i++)
        {
            var dto = file.Strata![i];
            var at = $"strata[{i}]";
            var id = Id(dto.Id, path, at, seen, errors);
            var label = Required(dto.Label, path, $"{at}.label", errors);
            var travel = Positive(dto.TravelTicks, path, $"{at}.travelTicks", errors);
            var energy = NonNegative(dto.EnergyCost, path, $"{at}.energyCost", errors);
            var hazard = NonNegative(dto.HazardPermille, path, $"{at}.hazardPermille", errors);

            var yields = new List<ItemAmount>();
            for (var j = 0; j < (dto.Yields?.Count ?? 0); j++)
            {
                var yielded = Amount(dto.Yields![j], path, $"{at}.yields[{j}]", items, errors);
                if (yielded is { } value)
                {
                    yields.Add(value);
                }
            }

            if (id is null || label is null || travel is null || energy is null || hazard is null)
            {
                continue;
            }

            result.Add(new StratumDefinition(
                new StratumId(id), label, yields, travel.Value, energy.Value, hazard.Value));
        }

        return result;
    }

    private static Scenario? LinkScenario(
        ContentCatalog catalog, ScenarioFile file, string path, List<ContentError> errors)
    {
        var before = errors.Count;

        var id = Id(file.Id, path, "id", new HashSet<string>(), errors);
        var label = Required(file.Label, path, "label", errors);
        var capacity = Positive(file.EnergyCapacity, path, "energyCapacity", errors);

        var storages = new List<ScenarioStorage>();
        var storageIds = new HashSet<StorageId>();
        var storageArchetypes = new Dictionary<StorageId, StorageArchetype>();
        var seenStorages = new HashSet<string>();

        for (var i = 0; i < (file.Storages?.Count ?? 0); i++)
        {
            var dto = file.Storages![i];
            var at = $"storages[{i}]";
            var storageId = Id(dto.Id, path, at, seenStorages, errors);
            var archetypeId = Required(dto.Archetype, path, $"{at}.archetype", errors);
            var archetype = archetypeId is null
                ? null
                : catalog.Storage(new StorageArchetypeId(archetypeId));

            if (archetypeId is not null && archetype is null)
            {
                errors.Add(new ContentError(
                    path, $"{at}.archetype", $"no storage archetype '{archetypeId}'."));
            }

            var initial = new List<ItemAmount>();
            for (var j = 0; j < (dto.Initial?.Count ?? 0); j++)
            {
                var stock = Amount(dto.Initial![j], path, $"{at}.initial[{j}]", null, errors);
                if (stock is not { } value)
                {
                    continue;
                }

                var item = catalog.Item(value.Item);
                if (item is null)
                {
                    errors.Add(new ContentError(
                        path, $"{at}.initial[{j}].item", $"no item '{value.Item}'."));
                }
                else if (archetype is not null)
                {
                    // The same arithmetic the engine uses, met where a content author can act on it.
                    var room = item.HoldCapacity * archetype.CapacityPermille
                        / StorageArchetype.FullHold;
                    if (value.Quantity > room)
                    {
                        errors.Add(new ContentError(
                            path,
                            $"{at}.initial[{j}]",
                            $"starts with {value.Quantity} {value.Item} but holds at most {room}."));
                    }
                }

                initial.Add(value);
            }

            if (storageId is null || archetype is null)
            {
                continue;
            }

            var placed = Placement(dto.Placement, path, $"{at}.placement", errors);
            var storage = new ScenarioStorage(
                new StorageId(storageId), archetype.Id, dto.NameOverride, initial, placed);

            storages.Add(storage);
            storageIds.Add(storage.Id);
            storageArchetypes[storage.Id] = archetype;
        }

        var facilities = new List<ScenarioFacility>();
        var facilityArchetypes = new Dictionary<ExecutorId, FacilityArchetype>();
        var buffers = new HashSet<StorageId>();
        var seenExecutors = new HashSet<string>();

        for (var i = 0; i < (file.Facilities?.Count ?? 0); i++)
        {
            var dto = file.Facilities![i];
            var at = $"facilities[{i}]";
            var facilityId = Id(dto.Id, path, at, seenExecutors, errors);
            var archetypeId = Required(dto.Archetype, path, $"{at}.archetype", errors);
            var archetype = archetypeId is null
                ? null
                : catalog.Facility(new FacilityArchetypeId(archetypeId));

            if (archetypeId is not null && archetype is null)
            {
                errors.Add(new ContentError(
                    path, $"{at}.archetype", $"no facility archetype '{archetypeId}'."));
            }

            var local = Required(dto.LocalStorage, path, $"{at}.localStorage", errors);
            if (local is not null && !storageIds.Contains(new StorageId(local)))
            {
                errors.Add(new ContentError(
                    path, $"{at}.localStorage", $"no storage '{local}' in this scenario."));
                local = null;
            }

            SchematicId? initialSchematic = null;
            if (dto.InitialSchematic is { } schematicId)
            {
                if (!catalog.Schematics.TryGet(new SchematicId(schematicId), out var schematic))
                {
                    errors.Add(new ContentError(
                        path, $"{at}.initialSchematic", $"no schematic '{schematicId}'."));
                }
                else
                {
                    if (archetype is not null && schematic.RequiredFacilityType != archetype.Type)
                    {
                        errors.Add(new ContentError(
                            path,
                            $"{at}.initialSchematic",
                            $"'{schematicId}' needs a {schematic.RequiredFacilityType} and " +
                            $"'{archetype.Id}' is a {archetype.Type}."));
                    }

                    initialSchematic = schematic.Id;
                }
            }

            var built = Flag(dto.BuiltAtStart, path, $"{at}.builtAtStart", errors);
            var placed = Placement(dto.Placement, path, $"{at}.placement", errors);
            if (dto.Placement is null)
            {
                errors.Add(new ContentError(
                    path, $"{at}.placement", "a facility has a card, so it needs a cell."));
            }

            if (facilityId is null || archetype is null || local is null || built is null
                || placed is null)
            {
                continue;
            }

            var facility = new ScenarioFacility(
                new ExecutorId(facilityId), archetype.Id, dto.NameOverride, new StorageId(local),
                initialSchematic, built.Value, placed);

            facilities.Add(facility);
            facilityArchetypes[facility.Id] = archetype;
            buffers.Add(facility.LocalStorage);
        }

        // A storage with no cell of its own must be drawn inside some facility's card. One that is
        // neither placed nor claimed would be dropped from the graph silently, which is the failure
        // BaseGraphNodes returns nothing for rather than guessing at.
        foreach (var storage in storages)
        {
            if (storage.Placement is null && !buffers.Contains(storage.Id))
            {
                errors.Add(new ContentError(
                    path,
                    $"storages[{storages.IndexOf(storage)}].placement",
                    $"'{storage.Id}' has no cell and is no facility's local storage, so nothing " +
                    "would draw it."));
            }
        }

        var power = Placement(file.Power, path, "power", errors);
        if (file.Power is null)
        {
            errors.Add(new ContentError(
                path, "power", "the power core has a card, so it needs a cell."));
        }

        var cells = new Dictionary<(int Column, int Row), string>();
        if (power is not null)
        {
            Occupy(cells, power, "power", path, errors);
        }

        foreach (var facility in facilities)
        {
            Occupy(cells, facility.Placement, facility.Id.Value, path, errors);
        }

        foreach (var storage in storages)
        {
            if (storage.Placement is { } placement)
            {
                Occupy(cells, placement, storage.Id.Value, path, errors);
            }
        }

        var routes = new List<ScenarioRoute>();
        for (var i = 0; i < (file.Routes?.Count ?? 0); i++)
        {
            var dto = file.Routes![i];
            var at = $"routes[{i}]";
            var routeId = Id(dto.Id, path, at, seenExecutors, errors);
            var archetypeId = Required(dto.Archetype, path, $"{at}.archetype", errors);
            var archetype = archetypeId is null
                ? null
                : catalog.Transport(new TransportArchetypeId(archetypeId));

            if (archetypeId is not null && archetype is null)
            {
                errors.Add(new ContentError(
                    path, $"{at}.archetype", $"no transport archetype '{archetypeId}'."));
            }

            var from = Endpoint(dto.From, path, $"{at}.from", storageIds, errors);
            var to = Endpoint(dto.To, path, $"{at}.to", storageIds, errors);
            if (from is not null && from == to)
            {
                errors.Add(new ContentError(
                    path, at, $"runs from '{from}' to itself, which would move nothing."));
            }

            var built = Flag(dto.BuiltAtStart, path, $"{at}.builtAtStart", errors);

            if (routeId is null || archetype is null || from is null || to is null || built is null)
            {
                continue;
            }

            routes.Add(new ScenarioRoute(
                new ExecutorId(routeId), archetype.Id, dto.NameOverride, from.Value, to.Value,
                built.Value));
        }

        var sinks = new List<PowerSinkId>();
        var standing = 0L;
        for (var i = 0; i < (file.Sinks?.Count ?? 0); i++)
        {
            var sinkId = new PowerSinkId(file.Sinks![i]);
            PowerSinkDefinition? sink = null;
            foreach (var candidate in catalog.Sinks)
            {
                if (candidate.Id == sinkId)
                {
                    sink = candidate;
                    break;
                }
            }

            if (sink is null)
            {
                errors.Add(new ContentError(path, $"sinks[{i}]", $"no power sink '{sinkId}'."));
                continue;
            }

            sinks.Add(sinkId);
            standing += sink.PowerDraw;
        }

        foreach (var facility in facilities)
        {
            standing += facilityArchetypes[facility.Id].StandingPowerDraw;
        }

        foreach (var route in routes)
        {
            standing += catalog.Transport(route.Archetype)!.StandingPowerDraw;
        }

        if (capacity is { } limit && standing > limit)
        {
            errors.Add(new ContentError(
                path,
                "energyCapacity",
                $"standing draw is {standing} against a capacity of {limit}. Sinks and idle " +
                "executors must fit within capacity."));
        }

        var unlocked = new List<SchematicId>();
        for (var i = 0; i < (file.UnlockedSchematics?.Count ?? 0); i++)
        {
            var schematicId = new SchematicId(file.UnlockedSchematics![i]);
            if (!catalog.Schematics.TryGet(schematicId, out _))
            {
                errors.Add(new ContentError(
                    path, $"unlockedSchematics[{i}]", $"no schematic '{schematicId}'."));
                continue;
            }

            unlocked.Add(schematicId);
        }

        var tasks = new List<ScenarioTask>();
        for (var i = 0; i < (file.InitialTasks?.Count ?? 0); i++)
        {
            var dto = file.InitialTasks![i];
            var at = $"initialTasks[{i}]";
            var schematicId = Required(dto.Schematic, path, $"{at}.schematic", errors);
            var executorId = Required(dto.Executor, path, $"{at}.executor", errors);

            if (dto.Runs is { } runs && runs <= 0)
            {
                errors.Add(new ContentError(
                    path, $"{at}.runs", "a task must request at least one run. Omit it for a standing order."));
            }

            SchematicDefinition? schematic = null;
            if (schematicId is not null
                && !catalog.Schematics.TryGet(new SchematicId(schematicId), out schematic))
            {
                errors.Add(new ContentError(
                    path, $"{at}.schematic", $"no schematic '{schematicId}'."));
            }

            FacilityArchetype? archetype = null;
            if (executorId is not null)
            {
                var executor = new ExecutorId(executorId);
                if (!facilityArchetypes.TryGetValue(executor, out archetype))
                {
                    errors.Add(new ContentError(
                        path, $"{at}.executor", $"no facility '{executorId}' in this scenario."));
                }
                else if (!archetype.Commandable)
                {
                    // A passive source runs what it is configured with and is scheduled by nobody.
                    // Queueing work on one says the player ordered it, which is the misstatement
                    // the flag exists to catch.
                    errors.Add(new ContentError(
                        path,
                        $"{at}.executor",
                        $"'{executorId}' is a {archetype.Id}, which is not commandable. A passive " +
                        "facility runs what it is configured with and is scheduled by nobody."));
                }
            }

            if (schematic is not null && archetype is not null
                && schematic.RequiredFacilityType != archetype.Type)
            {
                errors.Add(new ContentError(
                    path,
                    at,
                    $"'{schematic.Id}' needs a {schematic.RequiredFacilityType} and " +
                    $"'{executorId}' is a {archetype.Type}."));
            }

            if (schematicId is null || executorId is null || schematic is null || archetype is null)
            {
                continue;
            }

            tasks.Add(new ScenarioTask(schematic.Id, dto.Runs, new ExecutorId(executorId)));
        }

        var transfers = new List<ScenarioTransfer>();
        for (var i = 0; i < (file.InitialTransfers?.Count ?? 0); i++)
        {
            var dto = file.InitialTransfers![i];
            var at = $"initialTransfers[{i}]";
            var itemId = Required(dto.Item, path, $"{at}.item", errors);
            var executorId = Required(dto.Executor, path, $"{at}.executor", errors);
            var from = Endpoint(dto.From, path, $"{at}.from", storageIds, errors);
            var to = Endpoint(dto.To, path, $"{at}.to", storageIds, errors);

            if (dto.Quantity is { } quantity && quantity <= 0)
            {
                errors.Add(new ContentError(
                    path,
                    $"{at}.quantity",
                    "a transfer must move at least one unit. Omit it for a standing order."));
            }

            if (itemId is not null && catalog.Item(new ItemId(itemId)) is null)
            {
                errors.Add(new ContentError(path, $"{at}.item", $"no item '{itemId}'."));
                itemId = null;
            }

            ScenarioRoute? route = null;
            if (executorId is not null)
            {
                foreach (var candidate in routes)
                {
                    if (candidate.Id.Value == executorId)
                    {
                        route = candidate;
                        break;
                    }
                }

                if (route is null)
                {
                    errors.Add(new ContentError(
                        path, $"{at}.executor", $"no transport line '{executorId}' in this scenario."));
                }
                else if (from is not null && to is not null
                    && (route.From != from || route.To != to))
                {
                    // A line runs a fixed route, so a transfer it could never make would sit in a
                    // queue no line aboard can serve — which reads as a stalled vessel rather than
                    // as the authoring mistake it is.
                    errors.Add(new ContentError(
                        path,
                        at,
                        $"'{executorId}' runs '{route.From}' to '{route.To}', not '{from}' to '{to}'."));
                }
            }

            if (itemId is null || executorId is null || from is null || to is null || route is null)
            {
                continue;
            }

            transfers.Add(new ScenarioTransfer(
                new ItemId(itemId), dto.Quantity, from.Value, to.Value, new ExecutorId(executorId)));
        }

        var hold = Required(file.Hold, path, "hold", errors);
        if (hold is not null && !storageIds.Contains(new StorageId(hold)))
        {
            errors.Add(new ContentError(
                path,
                "hold",
                $"'{hold}' is not one of this scenario's storages. The hold is the storage every " +
                "plan routes material through, so it has to be one this vessel has."));
            hold = null;
        }

        if (errors.Count > before || id is null || label is null || capacity is null || hold is null
            || power is null)
        {
            return null;
        }

        return new Scenario(
            id, label, capacity.Value, new StorageId(hold), storages, facilities, routes, power,
            sinks, unlocked, tasks, transfers);
    }

    private static void Occupy(
        Dictionary<(int Column, int Row), string> cells,
        NodePlacement placement,
        string owner,
        string path,
        List<ContentError> errors)
    {
        var cell = (placement.Column, placement.Row);
        if (cells.TryGetValue(cell, out var taken))
        {
            errors.Add(new ContentError(
                path,
                "placement",
                $"'{owner}' and '{taken}' both sit at column {placement.Column}, row {placement.Row}."));
            return;
        }

        cells[cell] = owner;
    }

    private static StorageId? Endpoint(
        string? value, string path, string at, HashSet<StorageId> storages, List<ContentError> errors)
    {
        var name = Required(value, path, at, errors);
        if (name is null)
        {
            return null;
        }

        var storage = new StorageId(name);
        if (!storages.Contains(storage))
        {
            errors.Add(new ContentError(path, at, $"no storage '{name}' in this scenario."));
            return null;
        }

        return storage;
    }

    private static NodePlacement? Placement(
        PlacementDto? dto, string path, string at, List<ContentError> errors)
    {
        if (dto is null)
        {
            return null;
        }

        var badge = Required(dto.Badge, path, $"{at}.badge", errors);
        if (dto.Column is null)
        {
            errors.Add(new ContentError(path, $"{at}.column", "is required."));
        }

        if (dto.Row is null)
        {
            errors.Add(new ContentError(path, $"{at}.row", "is required."));
        }

        if (badge is null || dto.Column is null || dto.Row is null)
        {
            return null;
        }

        return new NodePlacement(dto.Column.Value, dto.Row.Value, badge);
    }

    private static ItemAmount? Amount(
        ItemAmountDto? dto, string path, string at, HashSet<ItemId>? items, List<ContentError> errors)
    {
        if (dto is null)
        {
            errors.Add(new ContentError(path, at, "is required."));
            return null;
        }

        var item = Required(dto.Item, path, $"{at}.item", errors);
        var quantity = Positive(dto.Quantity, path, $"{at}.quantity", errors);

        if (item is null || quantity is null)
        {
            return null;
        }

        var id = new ItemId(item);
        if (items is not null && !items.Contains(id))
        {
            errors.Add(new ContentError(path, $"{at}.item", $"no item '{item}'."));
            return null;
        }

        return new ItemAmount(id, quantity.Value);
    }

    private static ItemId? KnownItem(
        string? value, string path, string at, HashSet<ItemId> items, List<ContentError> errors)
    {
        var name = Required(value, path, at, errors);
        if (name is null)
        {
            return null;
        }

        var id = new ItemId(name);
        if (!items.Contains(id))
        {
            errors.Add(new ContentError(path, at, $"no item '{name}'."));
            return null;
        }

        return id;
    }

    private static FacilityType? FacilityTypeOf(
        string? value, string path, string at, List<ContentError> errors)
    {
        var name = Required(value, path, at, errors);
        if (name is null)
        {
            return null;
        }

        if (!FacilityTypes.TryGetValue(name, out var type))
        {
            errors.Add(new ContentError(
                path, at, $"'{name}' is not a facility type. Known: {string.Join(", ", FacilityTypes.Keys)}."));
            return null;
        }

        return type;
    }

    private static string? Id(
        string? value, string path, string at, HashSet<string> seen, List<ContentError> errors)
    {
        var id = Required(value, path, $"{at}.id", errors);
        if (id is null)
        {
            return null;
        }

        if (!IdPattern.IsMatch(id))
        {
            errors.Add(new ContentError(
                path, $"{at}.id", $"'{id}' does not match {ContentCatalog.IdPattern}."));
            return null;
        }

        if (!seen.Add(id))
        {
            errors.Add(new ContentError(path, $"{at}.id", $"'{id}' is declared twice."));
            return null;
        }

        return id;
    }

    private static string? Required(string? value, string path, string at, List<ContentError> errors)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        errors.Add(new ContentError(path, at, "is required."));
        return null;
    }

    private static bool? Flag(bool? value, string path, string at, List<ContentError> errors)
    {
        if (value is null)
        {
            errors.Add(new ContentError(path, at, "is required."));
        }

        return value;
    }

    private static long? Positive(long? value, string path, string at, List<ContentError> errors)
    {
        if (value is null)
        {
            errors.Add(new ContentError(path, at, "is required."));
            return null;
        }

        if (value <= 0)
        {
            errors.Add(new ContentError(path, at, $"is {value}; it has to be positive."));
            return null;
        }

        return value;
    }

    private static long? NonNegative(long? value, string path, string at, List<ContentError> errors)
    {
        if (value is null)
        {
            errors.Add(new ContentError(path, at, "is required."));
            return null;
        }

        if (value < 0)
        {
            errors.Add(new ContentError(path, at, $"is {value}; it cannot be negative."));
            return null;
        }

        return value;
    }
}
