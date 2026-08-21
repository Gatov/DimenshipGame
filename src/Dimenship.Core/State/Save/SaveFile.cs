using System.Text.Json.Serialization;

namespace Dimenship.Core.State.Save;

// The shape a save has on disk. One DTO per state type, mirroring the tree rather than reusing it,
// for two reasons.
//
// The state types are mutable classes with construction rules the engine relies on — required
// members, collections that own their own ordering — and a serializer that reached into them would
// quietly become a second way to build a world. And the save is a format with a version, which
// means it has to be able to differ from the tree: the day a field moves, the old shape still has
// to be readable, and it can only be readable if it was written down somewhere.
//
// Everything is nullable so a missing field is a reported error rather than a silent default, on
// the same terms as the content loader. Sets are written sorted, so two saves of one world are
// byte-identical and a diff between two saves means something.

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveEnvelope
{
    /// <summary>
    /// The format's own version. It lives here and not inside the world, because it describes the
    /// file rather than the vessel — holding it in both places is holding two answers to one
    /// question, and the day they disagree nothing can say which is right.
    /// </summary>
    public int? SaveVersion { get; init; }

    /// <summary>Which catalog this world was played against. Compared, never absorbed.</summary>
    public string? ContentVersion { get; init; }

    /// <summary>The tick this was written at. A save browser reads it without parsing the world.</summary>
    public long? SavedAtTick { get; init; }

    public WorldStateDto? State { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorldStateDto
{
    /// <summary>
    /// Pinned by id and re-read from content on every load, never copied in. That is what lets an
    /// edited layout reach a campaign already in progress.
    /// </summary>
    public string? ScenarioId { get; init; }

    public ClockDto? Clock { get; init; }

    public ulong[]? RandomStreams { get; init; }

    public VesselDto? Vessel { get; init; }

    public TasksDto? Tasks { get; init; }

    public ProgressDto? Progress { get; init; }

    public PlansDto? Plans { get; init; }

    public MissionsDto? Missions { get; init; }

    public AlertsDto? Alerts { get; init; }

    public JournalDto? Journal { get; init; }

    public long? NextProgramInstanceId { get; init; }

    public RobotsDto? Robots { get; init; }
}

/// <summary>
/// The clock, less its flow. Every load resumes paused: 0× is the one state where every action is
/// available, and a save that resumed at 4× would resume a vessel moving before its owner had
/// looked at it. The auto-pause preference is saved, because it is a preference the player set
/// rather than a speed they happened to leave running.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClockDto
{
    public long? Tick { get; init; }

    public bool? AutoPauseOnCriticalAlert { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VesselDto
{
    public string? Hold { get; init; }

    public IReadOnlyList<StorageDto>? Storages { get; init; }

    public IReadOnlyList<FacilityDto>? Facilities { get; init; }

    public IReadOnlyList<TransportDto>? Transports { get; init; }

    public IReadOnlyList<ReactorDto>? Reactors { get; init; }

    public IReadOnlyList<string>? Sinks { get; init; }

    public BudgetDto? Energy { get; init; }

    public BudgetDto? Compute { get; init; }

    public IReadOnlyList<ReservationDto>? Reservations { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoredItemDto
{
    public string? Item { get; init; }

    public long? Amount { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StorageDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public IReadOnlyList<StoredItemDto>? Stock { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UtilizationDto
{
    public long? WindowTicks { get; init; }

    public long? BucketTicks { get; init; }

    public int? Head { get; init; }

    public long? Measured { get; init; }

    public long[]? Working { get; init; }

    public long[]? Idle { get; init; }

    public long[]? WaitingInput { get; init; }

    public long[]? WaitingOutput { get; init; }

    public long[]? Throttled { get; init; }

    public long[]? SwitchingOver { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FacilityDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public string? LocalStorage { get; init; }

    public bool? Built { get; init; }

    public long? WorkRatePermille { get; init; }

    public long? EnergyEfficiencyPermille { get; init; }

    public long? IntegrityPermille { get; init; }

    public string? Configured { get; init; }

    public long? SwitchOverRemaining { get; init; }

    public long? SwitchTarget { get; init; }

    public IReadOnlyList<long>? Queue { get; init; }

    public long? Current { get; init; }

    public IReadOnlyList<long>? Programs { get; init; }

    public string? Status { get; init; }

    public string? BlockReason { get; init; }

    public long? PowerDrawLastTick { get; init; }

    public UtilizationDto? Utilization { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransportDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public bool? Built { get; init; }

    public long? ThroughputPermille { get; init; }

    public IReadOnlyList<long>? Queue { get; init; }

    public long? Current { get; init; }

    public long? MovedLastTick { get; init; }

    public long? PowerDrawLastTick { get; init; }

    public string? Status { get; init; }

    public string? BlockReason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReactorDto
{
    public string? Id { get; init; }

    public string? Archetype { get; init; }

    public string? NameOverride { get; init; }

    public bool? Built { get; init; }

    public long? IntegrityPermille { get; init; }

    public string? FuelStore { get; init; }

    public long? OutputPermille { get; init; }

    public IReadOnlyList<long>? Programs { get; init; }

    public string? Status { get; init; }

    public string? BlockReason { get; init; }

    public UtilizationDto? Utilization { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetDto
{
    public long? Capacity { get; init; }

    public long? DrawLastTick { get; init; }

    public int? CapHits { get; init; }

    public int? StarvedTicks { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReservationDto
{
    public string? Storage { get; init; }

    public string? Item { get; init; }

    public long? Quantity { get; init; }

    public long? Owner { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttemptDto
{
    public long? Tick { get; init; }

    public string? Outcome { get; init; }

    public string? Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductionTaskDto
{
    public long? Id { get; init; }

    public string? Schematic { get; init; }

    public int? RequestedRuns { get; init; }

    public string? Executor { get; init; }

    public string? State { get; init; }

    public int? CompletedRuns { get; init; }

    public bool? RunActive { get; init; }

    public bool? RunAwaitingDeposit { get; init; }

    public long? WorkDoneThisRun { get; init; }

    public long? EnergyChargedThisRun { get; init; }

    public string? LastReason { get; init; }

    public long? PostponedAtTick { get; init; }

    public IReadOnlyList<AttemptDto>? History { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransportTaskDto
{
    public long? Id { get; init; }

    public string? Item { get; init; }

    public long? RequestedQuantity { get; init; }

    public string? Executor { get; init; }

    public string? Source { get; init; }

    public string? Destination { get; init; }

    public string? State { get; init; }

    public long? MovedQuantity { get; init; }

    public string? LastReason { get; init; }

    public long? PostponedAtTick { get; init; }

    public IReadOnlyList<AttemptDto>? History { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TasksDto
{
    public long? NextTaskId { get; init; }

    public IReadOnlyList<ProductionTaskDto>? Production { get; init; }

    public IReadOnlyList<TransportTaskDto>? Transport { get; init; }

    public IReadOnlyList<long>? Retired { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressDto
{
    public IReadOnlyList<string>? UnlockedSchematics { get; init; }

    public IReadOnlyList<string>? DiscoveredItems { get; init; }

    public IReadOnlyList<string>? Flags { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShortageDto
{
    public string? Item { get; init; }

    public long? Missing { get; init; }

    public string? Kind { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlanDto
{
    public long? Id { get; init; }

    public string? GoalItem { get; init; }

    public long? GoalQuantity { get; init; }

    public long? CommittedAtTick { get; init; }

    public IReadOnlyList<long>? SpawnedTasks { get; init; }

    public IReadOnlyList<ShortageDto>? Shortages { get; init; }

    public int? CompletedTasks { get; init; }

    public string? State { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlansDto
{
    public long? NextPlanId { get; init; }

    public IReadOnlyList<PlanDto>? Plans { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemAmountDto
{
    public string? Item { get; init; }

    public long? Quantity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MissionDto
{
    public long? Id { get; init; }

    public string? Target { get; init; }

    public string? Kind { get; init; }

    public string? Phase { get; init; }

    public string? Dock { get; init; }

    public IReadOnlyList<long>? Group { get; init; }

    public long? DepartedAtTick { get; init; }

    public long? ArrivesAtTick { get; init; }

    public IReadOnlyList<ItemAmountDto>? Manifest { get; init; }

    public string? Destination { get; init; }

    public long? ForPlan { get; init; }

    public ulong? RngStream { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MissionsDto
{
    public long? NextMissionId { get; init; }

    public IReadOnlyList<MissionDto>? Missions { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AlertDto
{
    public long? Id { get; init; }

    public string? Severity { get; init; }

    public string? Code { get; init; }

    public string? SubjectId { get; init; }

    public long? RaisedAtTick { get; init; }

    public string? RootCause { get; init; }

    public bool? Acknowledged { get; init; }

    public bool? Pinned { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AlertsDto
{
    public long? NextAlertId { get; init; }

    public IReadOnlyList<AlertDto>? Alerts { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EventDto
{
    public long? Tick { get; init; }

    public string? Category { get; init; }

    public string? Code { get; init; }

    public string? Subject { get; init; }

    /// <summary>Written key-sorted, so one world always serialises to one byte sequence.</summary>
    public IReadOnlyDictionary<string, long>? Data { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record JournalDto
{
    public long? TotalEmitted { get; init; }

    /// <summary>Saved in full. A console that goes blank on load is a bug report.</summary>
    public IReadOnlyList<EventDto>? Events { get; init; }
}

/// <summary>One socket and the storage behind it, written in the frame's declaration order.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RobotSocketDto
{
    public string? Socket { get; init; }

    public string? Storage { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RobotDto
{
    public long? Id { get; init; }

    public string? Frame { get; init; }

    public string? NameOverride { get; init; }

    /// <summary>
    /// Every socket the frame declares, in its order. An empty socket is an entry whose storage
    /// holds nothing, not a missing entry — which is the distinction the flat list of fitted ids
    /// this replaced could not make, and the reason the shape changed while it was still free to.
    /// </summary>
    public IReadOnlyList<RobotSocketDto>? Sockets { get; init; }

    public long? IntegrityPermille { get; init; }

    public string? Group { get; init; }

    public long? OnMission { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RobotsDto
{
    public long? NextRobotId { get; init; }

    public IReadOnlyList<RobotDto>? Robots { get; init; }
}
