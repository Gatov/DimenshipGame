namespace Dimenship.Core.Simulation;

/// <summary>
/// A vessel-wide roll-up for one item: how much exists anywhere aboard, how much could exist,
/// and how fast the total moved last tick. Storage locations are visible separately on
/// <see cref="WorldSnapshot.Storages"/>; this is the answer to "how much alloy does this vessel
/// have", which is the question the overview and status bar ask.
/// </summary>
public sealed record ResourceStock(ItemId Id, long Amount, long Capacity, long NetRatePerTick);

/// <summary>One item's position in one storage.</summary>
public sealed record ItemStock(ItemId Id, long Amount, long Capacity);

/// <summary>
/// One storage location's contents. Items are listed in world item order, including items the
/// storage happens to hold none of, so a panel can render a stable set of rows.
/// <para>
/// <paramref name="FillPermille"/> is how full the storage is as a fraction of its one shared
/// volume, <c>1000</c> being full. It is carried rather than left to each surface because it is
/// the same number the engine enforces room against, and a storage node and an inspector panel
/// disagreeing about how full a storage is would be exactly the kind of small lie this UI cannot
/// afford.
/// </para>
/// <para>
/// It replaced a pair of sums over <paramref name="Items"/>. Those could not answer the question:
/// adding the amounts of unlike items totals nothing real, and adding the capacities counts room
/// for items that will never arrive — which is what made a facility buffer holding 29% of its
/// input read as 1% full on the base graph.
/// </para>
/// </summary>
public sealed record StorageState(
    StorageId Id,
    string Label,
    long FillPermille,
    IReadOnlyList<ItemStock> Items);

/// <summary>
/// The vessel's power position for one tick.
/// <para>
/// <paramref name="CapHits"/> and <paramref name="StarvedTicks"/> are deliberately separate and
/// neither implies the other. <paramref name="CapHits"/> counts ticks where the draw the engine
/// actually granted reached capacity — the vessel ran flat out and got away with it.
/// <paramref name="StarvedTicks"/> counts ticks where at least one executor was refused the
/// production energy it asked for. A refused charge is never granted, so its draw never lands in
/// <paramref name="Draw"/>: starvation leaves capacity looking unreached and
/// <paramref name="Reserve"/> looking healthy. Reading either one alone will mislead.
/// </para>
/// </summary>
public sealed record EnergyState(
    long Capacity, long Draw, long Reserve, int CapHits, int StarvedTicks);

/// <summary>
/// What one executor is doing this tick. <paramref name="RunTicksRemaining"/> is derived from the
/// work left on the run in progress, so it counts down honestly through a postponement rather
/// than pretending progress was made. <paramref name="RunTicksTotal"/> is what the whole run
/// costs; remaining ticks alone cannot express progress.
/// </summary>
public sealed record ExecutorState(
    ExecutorId Id,
    string Label,
    FacilityType Type,
    StorageId LocalStorage,
    ExecutorStatus Status,
    SchematicId? Configured,
    TaskId? CurrentTask,
    long PowerDraw,
    long RunTicksRemaining,
    long RunTicksTotal,
    long SwitchOverTicksRemaining,
    PostponeReason? BlockReason);

/// <summary>
/// What one transport line is doing this tick. <paramref name="MovedLastTick"/> against
/// <paramref name="ThroughputPerTick"/> is how hard the line is working; without the first of
/// those a view can only tell running from not.
/// </summary>
public sealed record TransportExecutorState(
    ExecutorId Id,
    string Label,
    StorageId From,
    StorageId To,
    ExecutorStatus Status,
    TaskId? CurrentTask,
    ItemId? CarriedItem,
    long ThroughputPerTick,
    long MovedLastTick,
    long PowerDraw,
    PostponeReason? BlockReason);

/// <summary>Something that draws power and does nothing else.</summary>
public sealed record PowerSinkState(string Id, string Label, long PowerDraw);

/// <summary>
/// An immutable projection of one queued production task. <paramref name="RequestedRuns"/> is
/// null for a standing order, where there is a running total to show and no ratio: a surface that
/// renders a percentage for one is inventing it.
/// </summary>
public sealed record ProductionTaskState(
    TaskId Id,
    SchematicId Schematic,
    ExecutorId Executor,
    int? RequestedRuns,
    int CompletedRuns,
    TaskState State,
    PostponeReason? LastReason,
    long? PostponedAtTick);

/// <summary>
/// An immutable projection of one queued transfer. <paramref name="RequestedQuantity"/> is null
/// for a standing order, for the same reason it is on a production task.
/// </summary>
public sealed record TransportTaskState(
    TaskId Id,
    ItemId Item,
    ExecutorId Executor,
    StorageId Source,
    StorageId Destination,
    long? RequestedQuantity,
    long MovedQuantity,
    TaskState State,
    PostponeReason? LastReason,
    long? PostponedAtTick);

/// <summary>
/// Immutable view of the world. Replaced wholesale on every change, never mutated, so the
/// shell can use reference equality as an exact change test.
/// </summary>
public sealed record WorldSnapshot(
    long Tick,
    IReadOnlyList<ResourceStock> Resources,
    IReadOnlyList<StorageState> Storages,
    EnergyState Energy,
    IReadOnlyList<ExecutorState> Executors,
    IReadOnlyList<TransportExecutorState> Transports,
    IReadOnlyList<PowerSinkState> Sinks,
    IReadOnlyList<ProductionTaskState> ProductionTasks,
    IReadOnlyList<TransportTaskState> TransportTasks,
    IReadOnlyList<SimEvent> RecentEvents,
    long TotalEventsEmitted);
