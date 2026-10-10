using System.Text.Json;
using System.Text.Json.Serialization;
using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Replay;

/// <summary>
/// One scripted demand: at <see cref="Tick"/>, ask for <see cref="Goal"/> exactly as the
/// Operations composer would, and approve it. Scripted demand stands in for the mission system,
/// which does not exist, as the scheduling design's §7 proposes.
/// <para>
/// <see cref="Assemble"/> names an unbuilt facility slot and makes the demand a construction draft,
/// which is the composer's build mode. Null is an ordinary production demand. A construction demand
/// with no <see cref="Destination"/> delivers into the slot's own buffer, as build mode does.
/// </para>
/// <para>
/// <see cref="Priority"/> is lent to the committed plan in the same tick it is committed, as one
/// command with the commit. Null leaves every task at <c>Normal</c>, which is the queue-order
/// baseline.
/// </para>
/// <para>
/// <see cref="Assign"/> is the composer's facility picker (E3): before approval, every production
/// step running a listed schematic is moved to the listed facility, by the draft edit the player's
/// picker makes. Null or empty approves the planner's choice untouched, which is every demand
/// before E3.
/// </para>
/// </summary>
public sealed record ScriptedDemand(
    string Id, long Tick, ItemAmount Goal, StorageId? Destination, ExecutorId? Assemble,
    Priority? Priority = null, IReadOnlyList<ScriptedAssignment>? Assign = null);

/// <summary>
/// One facility choice a scripted demand makes in its draft: run <see cref="Schematic"/> at
/// <see cref="Facility"/>. It is how a script schedules by hand, which is the design's fourth
/// policy, rather than leaving every stage where the planner's estimate put it.
/// </summary>
public sealed record ScriptedAssignment(SchematicId Schematic, ExecutorId Facility);

/// <summary>What a scripted command does to the plan of the demand it names.</summary>
public enum ScriptedCommandKind
{
    Priority,
    Hold,
    Release,
    Cancel,
    Amend,
    Relinquish,
    Reassign,

    /// <summary>
    /// Moves the plan's unstarted runs of a schematic to a facility (K6d): the facility choice
    /// <see cref="ScriptedDemand.Assign"/> makes before approval, made after commit.
    /// </summary>
    Move,
}

/// <summary>
/// One scripted command (C0): at <see cref="Tick"/>, act on the plan <see cref="Demand"/>
/// committed, through <c>SimulationEngine.Execute</c>, the door a controller will use. A script
/// names demands, never plan ids, because a plan id depends on how many plans came before.
/// <para>
/// The optional fields belong to particular kinds, and the parser refuses one given to a kind that
/// has no use for it: <see cref="Priority"/> to <c>priority</c>; <see cref="Quantity"/> to
/// <c>amend</c>, <c>relinquish</c> and <c>reassign</c>; <see cref="Storage"/> and
/// <see cref="Item"/> to the last two; <see cref="To"/>, another demand, to <c>reassign</c>;
/// <see cref="Schematic"/> and <see cref="Facility"/> to <c>move</c>.
/// </para>
/// <para>
/// A script decides in advance, which is what makes it a fixture rather than a controller: E1
/// pins a situation with it and E2 compares policies against it.
/// </para>
/// </summary>
public sealed record ScriptedCommand(
    long Tick,
    ScriptedCommandKind Kind,
    string Demand,
    Priority? Priority = null,
    long? Quantity = null,
    StorageId? Storage = null,
    ItemId? Item = null,
    string? To = null,
    SchematicId? Schematic = null,
    ExecutorId? Facility = null);

/// <summary>
/// A replay: which scenario to open, how long to run it, and what to ask of it along the way.
/// Ticks are the engine's clock: a demand at tick <c>t</c> is committed when the clock reads
/// <c>t</c>, before the tick that takes it to <c>t + 1</c>, so a demand at 0 is in the queues
/// before anything has run. Commands at a tick follow the demands at that tick, so a command can
/// act on a demand committed the same tick.
/// </summary>
public sealed record ReplayScript(
    string Scenario, long EndTick, IReadOnlyList<ScriptedDemand> Demands, IReadOnlyList<ScriptedCommand> Commands)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>
    /// Parses and links a script against the content it will run on. Like the content loader it
    /// runs in two phases, and like the loader it collects errors rather than stopping at the
    /// first: an author with three bad item ids should see three messages.
    /// <para>
    /// The parse phase rejects malformed JSON, an unknown field and a fractional number. A
    /// fractional number has no meaning here for the same reason it has none in content, since every
    /// quantity is already in milli-units. The link phase resolves every id against the catalog and
    /// the named scenario.
    /// </para>
    /// </summary>
    public static ScriptLoadResult Parse(string json, ContentCatalog catalog, IReadOnlyList<Scenario> scenarios)
    {
        ScriptDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ScriptDto>(json, Options);
        }
        catch (JsonException e)
        {
            return new ScriptLoadResult(null, new[] { $"script: {e.Message}" });
        }

        if (dto is null)
        {
            return new ScriptLoadResult(null, new[] { "script: the file is empty." });
        }

        var errors = new List<string>();

        Scenario? scenario = null;
        if (dto.Scenario is null)
        {
            errors.Add("scenario: missing.");
        }
        else
        {
            scenario = scenarios.FirstOrDefault(s => s.Id == dto.Scenario);
            if (scenario is null)
            {
                errors.Add($"scenario: no scenario '{dto.Scenario}' in this content.");
            }
        }

        if (dto.EndTick is not { } endTick || endTick <= 0)
        {
            errors.Add("endTick: missing, or not a positive number of ticks.");
            endTick = 0;
        }

        var demands = new List<ScriptedDemand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = dto.Demands ?? Array.Empty<DemandDto>();
        if (dto.Demands is null)
        {
            errors.Add("demands: missing. A script with nothing to ask for is an empty list, not an absent one.");
        }

        for (var i = 0; i < list.Count; i++)
        {
            var d = list[i];
            var at = $"demands[{i}]";
            var before = errors.Count;

            if (d.Id is null)
            {
                errors.Add($"{at}.id: missing.");
            }
            else if (!seen.Add(d.Id))
            {
                errors.Add($"{at}.id: '{d.Id}' is used twice; the report could not tell them apart.");
            }

            if (d.Tick is not { } tick)
            {
                errors.Add($"{at}.tick: missing.");
                tick = 0;
            }
            else if (tick < 0 || (endTick > 0 && tick >= endTick))
            {
                errors.Add($"{at}.tick: {tick} is outside the run, which covers ticks 0 to {endTick - 1}.");
            }

            if (d.Item is null)
            {
                errors.Add($"{at}.item: missing.");
            }
            else if (catalog.Item(new ItemId(d.Item)) is null)
            {
                errors.Add($"{at}.item: no item '{d.Item}' in the catalog.");
            }

            if (d.Quantity is not { } quantity || quantity <= 0)
            {
                errors.Add($"{at}.quantity: missing, or not a positive number of milli-units.");
                quantity = 0;
            }

            if (d.Destination is { } destination && scenario is not null
                && scenario.Storages.All(s => s.Id.Value != destination))
            {
                errors.Add($"{at}.destination: no storage '{destination}' in scenario '{scenario.Id}'.");
            }

            if (d.Assemble is { } assemble && scenario is not null
                && scenario.Facilities.All(f => f.Id.Value != assemble))
            {
                errors.Add($"{at}.assemble: no facility '{assemble}' in scenario '{scenario.Id}'.");
            }

            var priority = ParsePriority(d.Priority, $"{at}.priority", errors);
            var assign = ParseAssignments(d.Assign, $"{at}.assign", scenario, catalog, errors);

            if (errors.Count == before)
            {
                demands.Add(new ScriptedDemand(
                    d.Id!,
                    tick,
                    new ItemAmount(new ItemId(d.Item!), quantity),
                    d.Destination is null ? null : new StorageId(d.Destination),
                    d.Assemble is null ? null : new ExecutorId(d.Assemble),
                    priority,
                    assign));
            }
        }

        var commands = ParseCommands(dto.Commands, list, endTick, scenario, catalog, errors);

        return errors.Count > 0 || scenario is null
            ? new ScriptLoadResult(null, errors)
            : new ScriptLoadResult(new ReplayScript(scenario.Id, endTick, demands, commands), errors);
    }

    /// <summary>
    /// The commands, linked against the demands they name. Optional, unlike demands: a script from
    /// before C0 has none, and an absent list means exactly that.
    /// </summary>
    private static List<ScriptedCommand> ParseCommands(
        IReadOnlyList<CommandDto>? list, IReadOnlyList<DemandDto> demands, long endTick, Scenario? scenario,
        ContentCatalog catalog, List<string> errors)
    {
        var commands = new List<ScriptedCommand>();
        var demandTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var d in demands)
        {
            if (d.Id is not null && d.Tick is { } t)
            {
                demandTicks.TryAdd(d.Id, t);
            }
        }

        var kinds = Enum.GetNames<ScriptedCommandKind>().Select(n => n.ToLowerInvariant()).ToList();
        var items = list ?? Array.Empty<CommandDto>();
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            var at = $"commands[{i}]";
            var before = errors.Count;

            if (c.Tick is not { } tick)
            {
                errors.Add($"{at}.tick: missing.");
                tick = 0;
            }
            else if (tick < 0 || (endTick > 0 && tick >= endTick))
            {
                errors.Add($"{at}.tick: {tick} is outside the run, which covers ticks 0 to {endTick - 1}.");
            }

            ScriptedCommandKind kind = default;
            var known = false;
            if (c.Command is null)
            {
                errors.Add($"{at}.command: missing.");
            }
            else if (kinds.IndexOf(c.Command) is var index and >= 0)
            {
                kind = (ScriptedCommandKind)index;
                known = true;
            }
            else
            {
                errors.Add($"{at}.command: '{c.Command}' is not one of {string.Join(", ", kinds)}.");
            }

            RequireDemand(c.Demand, $"{at}.demand", tick, demandTicks, errors);

            if (known)
            {
                var takes = kind switch
                {
                    ScriptedCommandKind.Priority => new[] { "priority" },
                    ScriptedCommandKind.Amend => new[] { "quantity" },
                    ScriptedCommandKind.Relinquish => new[] { "quantity", "storage", "item" },
                    ScriptedCommandKind.Reassign => new[] { "quantity", "storage", "item", "to" },
                    ScriptedCommandKind.Move => new[] { "schematic", "facility" },
                    _ => Array.Empty<string>(),
                };

                var given = new (string Name, bool Present)[]
                {
                    ("priority", c.Priority is not null),
                    ("quantity", c.Quantity is not null),
                    ("storage", c.Storage is not null),
                    ("item", c.Item is not null),
                    ("to", c.To is not null),
                    ("schematic", c.Schematic is not null),
                    ("facility", c.Facility is not null),
                };

                foreach (var (name, present) in given)
                {
                    if (present && !takes.Contains(name))
                    {
                        errors.Add($"{at}.{name}: the '{c.Command}' command takes no {name}.");
                    }
                    else if (!present && takes.Contains(name))
                    {
                        errors.Add($"{at}.{name}: missing; the '{c.Command}' command needs one.");
                    }
                }
            }

            var priority = ParsePriority(c.Priority, $"{at}.priority", errors);

            if (c.Quantity is <= 0)
            {
                errors.Add($"{at}.quantity: not a positive number of milli-units.");
            }

            if (c.Storage is { } storage && scenario is not null && scenario.Storages.All(s => s.Id.Value != storage))
            {
                errors.Add($"{at}.storage: no storage '{storage}' in scenario '{scenario.Id}'.");
            }

            if (c.Item is { } item && catalog.Item(new ItemId(item)) is null)
            {
                errors.Add($"{at}.item: no item '{item}' in the catalog.");
            }

            if (c.To is not null)
            {
                RequireDemand(c.To, $"{at}.to", tick, demandTicks, errors);
            }

            if (known && kind == ScriptedCommandKind.Move)
            {
                // A missing field is already reported above, as every kind's is.
                RequireRunnable(c.Schematic, c.Facility, at, scenario, catalog, errors, reportMissing: false);
            }

            if (errors.Count == before)
            {
                commands.Add(new ScriptedCommand(
                    tick,
                    kind,
                    c.Demand!,
                    priority,
                    c.Quantity,
                    c.Storage is null ? null : new StorageId(c.Storage),
                    c.Item is null ? null : new ItemId(c.Item),
                    c.To,
                    c.Schematic is null ? null : new SchematicId(c.Schematic),
                    c.Facility is null ? null : new ExecutorId(c.Facility)));
            }
        }

        return commands;
    }

    /// <summary>A command acts on a demand the script names and has committed by then.</summary>
    private static void RequireDemand(
        string? demand, string at, long tick, Dictionary<string, long> demandTicks, List<string> errors)
    {
        if (demand is null)
        {
            errors.Add($"{at}: missing.");
        }
        else if (!demandTicks.TryGetValue(demand, out var committed))
        {
            errors.Add($"{at}: no demand '{demand}' in this script.");
        }
        else if (committed > tick)
        {
            errors.Add($"{at}: demand '{demand}' is not committed until tick {committed}.");
        }
    }

    private static Priority? ParsePriority(string? name, string at, List<string> errors)
    {
        if (name is null)
        {
            return null;
        }

        if (Enum.TryParse<Priority>(name, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed) && !int.TryParse(name, out _))
        {
            return parsed;
        }

        errors.Add($"{at}: '{name}' is not one of {string.Join(", ", Enum.GetNames<Priority>())}.");
        return null;
    }

    /// <summary>
    /// A demand's facility choices, linked. A facility whose type cannot run the schematic is
    /// refused here rather than left to approval, because the composer's picker never offers one:
    /// a script that could make that choice would be scheduling with a control the player lacks.
    /// An empty list and a schematic listed twice are refused as well, being an author's slip
    /// rather than a choice.
    /// </summary>
    private static IReadOnlyList<ScriptedAssignment>? ParseAssignments(
        IReadOnlyList<AssignmentDto>? list, string at, Scenario? scenario, ContentCatalog catalog,
        List<string> errors)
    {
        if (list is null)
        {
            return null;
        }

        if (list.Count == 0)
        {
            errors.Add($"{at}: empty. A demand that assigns nothing leaves the field out.");
            return null;
        }

        var assignments = new List<ScriptedAssignment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var here = $"{at}[{i}]";
            if (a.Schematic is not null && catalog.Schematics.TryGet(new SchematicId(a.Schematic), out _)
                && !seen.Add(a.Schematic))
            {
                errors.Add($"{here}.schematic: '{a.Schematic}' is assigned twice in one demand.");
            }

            RequireRunnable(a.Schematic, a.Facility, here, scenario, catalog, errors);

            if (a.Schematic is not null && a.Facility is not null)
            {
                assignments.Add(new ScriptedAssignment(new SchematicId(a.Schematic), new ExecutorId(a.Facility)));
            }
        }

        return assignments;
    }

    /// <summary>
    /// A schematic and a facility to run it at, linked: both named in the content, and the
    /// facility of the type the schematic runs at. A demand's <c>assign</c> and a <c>move</c>
    /// command share it, because they are one choice made before and after commit.
    /// </summary>
    private static void RequireRunnable(
        string? schematicId, string? facilityId, string here, Scenario? scenario, ContentCatalog catalog,
        List<string> errors, bool reportMissing = true)
    {
        SchematicDefinition? schematic = null;
        if (schematicId is null)
        {
            if (reportMissing)
            {
                errors.Add($"{here}.schematic: missing.");
            }
        }
        else if (!catalog.Schematics.TryGet(new SchematicId(schematicId), out schematic))
        {
            errors.Add($"{here}.schematic: no schematic '{schematicId}' in the catalog.");
            schematic = null;
        }

        ScenarioFacility? facility = null;
        if (facilityId is null)
        {
            if (reportMissing)
            {
                errors.Add($"{here}.facility: missing.");
            }
        }
        else if (scenario is not null)
        {
            facility = scenario.Facilities.FirstOrDefault(f => f.Id.Value == facilityId);
            if (facility is null)
            {
                errors.Add($"{here}.facility: no facility '{facilityId}' in scenario '{scenario.Id}'.");
            }
        }

        if (schematic is not null && facility is not null
            && catalog.Facility(facility.Archetype)?.Type is { } type && type != schematic.RequiredFacilityType)
        {
            errors.Add(
                $"{here}.facility: '{facilityId}' is a {type}, and '{schematicId}' runs at a "
                + $"{schematic.RequiredFacilityType}.");
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ScriptDto
    {
        /// <summary>For the author. Read by nobody, as in every content file.</summary>
        public string? Notes { get; init; }

        public string? Scenario { get; init; }

        public long? EndTick { get; init; }

        public IReadOnlyList<DemandDto>? Demands { get; init; }

        public IReadOnlyList<CommandDto>? Commands { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CommandDto
    {
        public long? Tick { get; init; }

        /// <summary>Lower case: priority, hold, release, cancel, amend, relinquish, reassign or move.</summary>
        public string? Command { get; init; }

        public string? Demand { get; init; }

        public string? Priority { get; init; }

        public long? Quantity { get; init; }

        public string? Storage { get; init; }

        public string? Item { get; init; }

        public string? To { get; init; }

        public string? Schematic { get; init; }

        public string? Facility { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record DemandDto
    {
        public string? Id { get; init; }

        public long? Tick { get; init; }

        public string? Item { get; init; }

        public long? Quantity { get; init; }

        public string? Destination { get; init; }

        public string? Assemble { get; init; }

        /// <summary>By name, as a save writes it: Low, Normal, High or Critical.</summary>
        public string? Priority { get; init; }

        public IReadOnlyList<AssignmentDto>? Assign { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record AssignmentDto
    {
        public string? Schematic { get; init; }

        public string? Facility { get; init; }
    }
}

/// <summary>A parsed script, or every reason it could not be one.</summary>
public sealed record ScriptLoadResult(ReplayScript? Script, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Script is not null && Errors.Count == 0;
}
