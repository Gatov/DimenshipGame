using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dimenship.Core.Content.Json;

// The shapes the files on disk actually have: one record per file, one per row.
//
// Everything is nullable, including the numbers and the flags, because "absent" has to be a
// collectable error rather than a silent default. A facility that forgot its work rate must be
// reported, not quietly built at zero. The link phase is where absence becomes a message.
//
// Every record disallows unmapped members, which is what makes a misspelled field a load error
// instead of a value that never arrives. "notes" is declared wherever it is useful and read
// nowhere: it is where the reasoning the C# comments hold goes, and the loader ignores it.
//
// Enums are strings here and parsed by hand in the link phase, so an unknown name is reported
// with the names that would have worked.

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManifestFile
{
    public string? ContentVersion { get; init; }

    public IReadOnlyList<string>? Catalog { get; init; }

    public IReadOnlyList<string>? Scenarios { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemAmountDto
{
    public string? Item { get; init; }

    public long? Quantity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlacementDto
{
    public int? Column { get; init; }

    public int? Row { get; init; }

    public string? Badge { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public long? HoldCapacity { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemsFile
{
    public IReadOnlyList<ItemDto>? Items { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchematicDto
{
    public string? Id { get; init; }

    public ItemAmountDto? Output { get; init; }

    public IReadOnlyList<ItemAmountDto>? Inputs { get; init; }

    public long? EffortPerRun { get; init; }

    public long? EnergyPerRun { get; init; }

    public string? RequiredFacilityType { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchematicsFile
{
    public IReadOnlyList<SchematicDto>? Schematics { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FacilityDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public string? Type { get; init; }

    public long? WorkRatePerTick { get; init; }

    public long? StandingPowerDraw { get; init; }

    public long? SwitchOverTicks { get; init; }

    public long? BufferPermille { get; init; }

    public bool? Commandable { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FacilitiesFile
{
    public IReadOnlyList<FacilityDto>? Facilities { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransportDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public long? ThroughputPerTick { get; init; }

    public long? StandingPowerDraw { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransportsFile
{
    public IReadOnlyList<TransportDto>? Transports { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StorageDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public long? CapacityPermille { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoragesFile
{
    public IReadOnlyList<StorageDto>? Storages { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SinkDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public long? PowerDraw { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SinksFile
{
    public IReadOnlyList<SinkDto>? Sinks { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReactorDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public string? Fuel { get; init; }

    public long? FuelPerTick { get; init; }

    public long? EnergyPerFuel { get; init; }

    public long? CapacityCeiling { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReactorsFile
{
    public IReadOnlyList<ReactorDto>? Reactors { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// The programs file, as far as this loader reads it: the envelope and nothing else. The program
/// language — rules, conditions, operands, actions — belongs to the programming work, and a schema
/// guessed at here would be one more thing for that work to disagree with. A non-empty array is
/// reported rather than half-parsed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgramsFile
{
    public IReadOnlyList<JsonElement>? Programs { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StratumDto
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public IReadOnlyList<ItemAmountDto>? Yields { get; init; }

    public long? TravelTicks { get; init; }

    public long? EnergyCost { get; init; }

    public long? HazardPermille { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StrataFile
{
    public IReadOnlyList<StratumDto>? Strata { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioStorageDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public IReadOnlyList<ItemAmountDto>? Initial { get; init; }

    /// <summary>Absent for a facility's local buffer, which is drawn inside that facility's card.</summary>
    public PlacementDto? Placement { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioFacilityDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public string? LocalStorage { get; init; }

    public string? InitialSchematic { get; init; }

    public bool? BuiltAtStart { get; init; }

    public PlacementDto? Placement { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioRouteDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public bool? BuiltAtStart { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// A task the campaign starts with. An absent <c>runs</c> is a standing order — produce for as
/// long as the inputs keep arriving. That is the meaning of the count being absent, so there is no
/// default to fall back to and none is invented.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioTaskDto
{
    public string? Schematic { get; init; }

    public int? Runs { get; init; }

    public string? Executor { get; init; }

    public string? Notes { get; init; }
}

/// <summary>A transfer the campaign starts with. An absent <c>quantity</c> is a standing order.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioTransferDto
{
    public string? Item { get; init; }

    public long? Quantity { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public string? Executor { get; init; }

    public string? Notes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScenarioFile
{
    public string? Id { get; init; }

    public string? Label { get; init; }

    public long? EnergyCapacity { get; init; }

    public string? Hold { get; init; }

    public IReadOnlyList<ScenarioStorageDto>? Storages { get; init; }

    public IReadOnlyList<ScenarioFacilityDto>? Facilities { get; init; }

    public IReadOnlyList<ScenarioRouteDto>? Routes { get; init; }

    public IReadOnlyList<string>? Sinks { get; init; }

    public IReadOnlyList<string>? UnlockedSchematics { get; init; }

    public IReadOnlyList<ScenarioTaskDto>? InitialTasks { get; init; }

    public IReadOnlyList<ScenarioTransferDto>? InitialTransfers { get; init; }

    public string? Notes { get; init; }
}
