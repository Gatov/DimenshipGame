namespace Dimenship.Core.Simulation;

public sealed record ResourceStock(ResourceId Id, long Amount, long Capacity, long NetRatePerTick);

public sealed record EnergyState(long Capacity, long Draw, long Reserve, int CapHits);

public sealed record FacilityState(
    FacilityId Id,
    FacilityKind Kind,
    FacilityStatus Status,
    long PowerDraw,
    EventCode? BlockReason);

/// <summary>
/// Immutable view of the world. Replaced wholesale on every change, never mutated, so the
/// shell can use reference equality as an exact change test.
/// </summary>
public sealed record WorldSnapshot(
    long Tick,
    IReadOnlyList<ResourceStock> Resources,
    EnergyState Energy,
    IReadOnlyList<FacilityState> Facilities,
    IReadOnlyList<SimEvent> RecentEvents,
    long TotalEventsEmitted);
