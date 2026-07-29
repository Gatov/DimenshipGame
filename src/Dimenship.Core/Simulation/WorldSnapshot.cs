namespace Dimenship.Core.Simulation;

public sealed record ResourceStock(ResourceId Id, long Amount, long Capacity, long NetRatePerTick);

/// <summary>
/// The vessel's power position for one tick.
/// <para>
/// <paramref name="CapHits"/> and <paramref name="StarvedTicks"/> are deliberately separate and
/// neither implies the other. <paramref name="CapHits"/> counts ticks where the draw the engine
/// actually granted reached capacity — the vessel ran flat out and got away with it.
/// <paramref name="StarvedTicks"/> counts ticks where at least one facility was refused power it
/// asked for. A facility that would exceed capacity is blocked rather than granted, so its draw
/// never lands in <paramref name="Draw"/>: starvation leaves capacity looking unreached and
/// <paramref name="Reserve"/> looking healthy. Reading either one alone will mislead.
/// </para>
/// </summary>
public sealed record EnergyState(
    long Capacity, long Draw, long Reserve, int CapHits, int StarvedTicks);

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
