using System.Text.Json;
using System.Text.Json.Serialization;
using Dimenship.Core.Content;
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
/// </summary>
public sealed record ScriptedDemand(
    string Id, long Tick, ItemAmount Goal, StorageId? Destination, ExecutorId? Assemble);

/// <summary>
/// A replay: which scenario to open, how long to run it, and what to ask of it along the way.
/// Ticks are the engine's clock: a demand at tick <c>t</c> is committed when the clock reads
/// <c>t</c>, before the tick that takes it to <c>t + 1</c>, so a demand at 0 is in the queues
/// before anything has run.
/// </summary>
public sealed record ReplayScript(string Scenario, long EndTick, IReadOnlyList<ScriptedDemand> Demands)
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

            if (errors.Count == before)
            {
                demands.Add(new ScriptedDemand(
                    d.Id!,
                    tick,
                    new ItemAmount(new ItemId(d.Item!), quantity),
                    d.Destination is null ? null : new StorageId(d.Destination),
                    d.Assemble is null ? null : new ExecutorId(d.Assemble)));
            }
        }

        return errors.Count > 0 || scenario is null
            ? new ScriptLoadResult(null, errors)
            : new ScriptLoadResult(new ReplayScript(scenario.Id, endTick, demands), errors);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ScriptDto
    {
        /// <summary>For the author. Read by nobody, as in every content file.</summary>
        public string? Notes { get; init; }

        public string? Scenario { get; init; }

        public long? EndTick { get; init; }

        public IReadOnlyList<DemandDto>? Demands { get; init; }
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
    }
}

/// <summary>A parsed script, or every reason it could not be one.</summary>
public sealed record ScriptLoadResult(ReplayScript? Script, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Script is not null && Errors.Count == 0;
}
