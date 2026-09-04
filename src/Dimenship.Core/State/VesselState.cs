using Dimenship.Core.Content;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State;

/// <summary>
/// How much of an item a storage holds. No capacity: that is the archetype's, and a copy here
/// would be a second answer to a question content already answers.
/// </summary>
public sealed record StoredItem(ItemId Item, long Amount);

/// <summary>
/// A storage this vessel has. Every instance is (id, archetype, overrides, dynamic fields) and
/// nothing else.
/// </summary>
public sealed class StorageInstance
{
    public required StorageId Id { get; init; }

    public required StorageArchetypeId Archetype { get; init; }

    /// <summary>Null leaves the archetype's label. Resolved through one helper, never inline.</summary>
    public string? NameOverride { get; set; }

    /// <summary>Declaration order, which is the order every projection is built in.</summary>
    public List<StoredItem> Stock { get; } = new();
}

/// <summary>
/// Ticks spent in each disposition over a trailing window, as bucketed counters.
/// <para>
/// The GDD asks the node inspector for "utilization 70%, input wait 31% of recent operational
/// window, power throttling 0%, output blocked 4%", and makes it a rule that a percentage without
/// a cause category is not enough. That is not derivable from a snapshot — it is an accumulation
/// over a trailing window, required to be reproducible after a save — so the accumulator is state.
/// </para>
/// <para>
/// Bucketed rather than a per-tick list: the window has to survive a save without the save growing
/// with it, and "31% input wait over ten minutes" needs no finer grain than a bucket. The
/// categories are chosen to sum to the elapsed window exactly, so no cause is silently
/// unattributed.
/// </para>
/// <para>
/// Declared and seeded here; filling and projecting it is the telemetry work.
/// </para>
/// </summary>
public sealed class UtilizationWindow
{
    /// <summary>Ten operational minutes.</summary>
    public const long DefaultWindowTicks = 600;

    public const long DefaultBucketTicks = 10;

    public required long WindowTicks { get; init; }

    public required long BucketTicks { get; init; }

    public int Head { get; set; }

    /// <summary>
    /// Ticks elapsed into the window, capped at <see cref="WindowTicks"/>. It is the divisor, and
    /// it is not <see cref="WindowTicks"/>: a ring that has not filled has counted fewer ticks than
    /// the window is wide, and dividing by the full window makes every category read low — a
    /// facility that has worked every tick since the world began would read 8% utilized two minutes
    /// into a new game, with the six categories summing to 8 rather than 100.
    /// </summary>
    public long Measured { get; set; }

    public required long[] Working { get; init; }

    public required long[] Idle { get; init; }

    public required long[] WaitingInput { get; init; }

    public required long[] WaitingOutput { get; init; }

    public required long[] Throttled { get; init; }

    public required long[] SwitchingOver { get; init; }

    public static UtilizationWindow Empty(
        long windowTicks = DefaultWindowTicks, long bucketTicks = DefaultBucketTicks)
    {
        var buckets = (int)((windowTicks + bucketTicks - 1) / bucketTicks);
        return new UtilizationWindow
        {
            WindowTicks = windowTicks,
            BucketTicks = bucketTicks,
            Working = new long[buckets],
            Idle = new long[buckets],
            WaitingInput = new long[buckets],
            WaitingOutput = new long[buckets],
            Throttled = new long[buckets],
            SwitchingOver = new long[buckets],
        };
    }
}

/// <summary>
/// A production facility this vessel has, or has a slot for.
/// <para>
/// Work rate, standing draw, switch-over ticks and buffer size are the archetype's and are read
/// through it at the point of use. The permille fields are how an upgrade moves them: effective
/// work rate is <c>archetype.WorkRatePerTick * WorkRatePermille / 1000</c>, so an upgrade moves one
/// number and no schematic is touched.
/// </para>
/// </summary>
public sealed class FacilityInstance
{
    public required ExecutorId Id { get; init; }

    public required FacilityArchetypeId Archetype { get; init; }

    public string? NameOverride { get; set; }

    /// <summary>Topology, and buildable, therefore state rather than content.</summary>
    public required StorageId LocalStorage { get; set; }

    /// <summary>
    /// False for a slot the campaign has authored and not yet filled. The layout is fixed and
    /// reveals facilities as they are built, which is only expressible if the slot exists first.
    /// </summary>
    public required bool Built { get; set; }

    /// <summary>1000 = the archetype's rate. Upgrades move this; nothing does yet.</summary>
    public long WorkRatePermille { get; set; } = 1000;

    /// <summary>1000 = the schematic's energy. Upgrades move this; nothing does yet.</summary>
    public long EnergyEfficiencyPermille { get; set; } = 1000;

    /// <summary>1000 = undamaged. Degraded, damaged and in-maintenance are this field read at
    /// different values, rather than three flags that can disagree.</summary>
    public long IntegrityPermille { get; set; } = 1000;

    /// <summary>The schematic the facility is set up for. Retained while idle.</summary>
    public SchematicId? Configured { get; set; }

    public long SwitchOverRemaining { get; set; }

    public TaskId? SwitchTarget { get; set; }

    /// <summary>Insertion order. Task priority reorders selection, not this list.</summary>
    public List<TaskId> Queue { get; } = new();

    public TaskId? Current { get; set; }

    /// <summary>Installed controllers, in evaluation order. Nothing installs one yet.</summary>
    public List<ProgramInstanceId> Programs { get; } = new();

    public ExecutorStatus Status { get; set; } = ExecutorStatus.NoTasksQueued;

    public PostponeReason? BlockReason { get; set; }

    /// <summary>
    /// Energy granted to this facility during the tick just finished. Saved for the same reason
    /// <see cref="TransportInstance.MovedLastTick"/> is: a snapshot rebuilt immediately after a
    /// load must show the vessel as it was, not as a cold start.
    /// </summary>
    public long PowerDrawLastTick { get; set; }

    public required UtilizationWindow Utilization { get; init; }
}

/// <summary>
/// One tick's intake sitting on a transport line's belt. A slot holds one item for one task
/// because a line services at most one task per tick, so a tick's intake is never more than that,
/// and one slot is exactly one tick of belt.
/// <para>
/// Mutable, and the quantity is what moves: a head that only partly fits keeps the remainder
/// rather than dropping it, which is the whole of the GDD's promise (§5.10) that a part in transit
/// is never lost.
/// </para>
/// </summary>
public sealed class BeltSlot
{
    /// <summary>
    /// The transfer this cargo was loaded for. Carried on the slot rather than inferred from the
    /// line's current task, because a line takes the next transfer on as soon as this one is
    /// entirely aboard — several transfers are in flight at once, and a delivery has to be
    /// credited to the one that picked it up.
    /// </summary>
    public required TaskId Task { get; init; }

    public required ItemId Item { get; init; }

    public required long Quantity { get; set; }
}

/// <summary>
/// A transport line this vessel has. The route is the line's, not the transfer's.
/// <para>
/// A line is a conveyor of authored length, one direction only. A two-way link is two of these,
/// which is what makes "blocked one way" free: the opposing line is a different object with its
/// own belt, queue and <see cref="BlockReason"/>, and nothing here can reach it.
/// </para>
/// </summary>
public sealed class TransportInstance
{
    public required ExecutorId Id { get; init; }

    public required TransportArchetypeId Archetype { get; init; }

    public string? NameOverride { get; set; }

    public required StorageId From { get; set; }

    public required StorageId To { get; set; }

    public required bool Built { get; set; }

    /// <summary>1000 = the archetype's throughput.</summary>
    public long ThroughputPermille { get; set; } = 1000;

    /// <summary>
    /// How many ticks cargo spends on this line, and so how many slots <see cref="Belt"/> has.
    /// <para>
    /// Authored on the route rather than on the archetype because it is physical distance: the
    /// hold-star legs share one archetype and are not the same length. It is also why the line has
    /// no authored capacity — the belt holds one tick's throughput per slot, so capacity is
    /// throughput times length and can never drift from either.
    /// </para>
    /// </summary>
    public required long LengthTicks { get; set; }

    /// <summary>
    /// The belt, index 0 at the destination end and index <c>LengthTicks - 1</c> at the source.
    /// A null slot is empty belt.
    /// <para>
    /// Positional rather than a queue of arrival ticks, because a blocked line freezes rather than
    /// accumulating: with nothing advancing, an arrival tick computed when the cargo was loaded
    /// would keep coming due while the cargo it belongs to has not moved an inch.
    /// </para>
    /// </summary>
    public List<BeltSlot?> Belt { get; } = new();

    public List<TaskId> Queue { get; } = new();

    public TaskId? Current { get; set; }

    /// <summary>
    /// How much this line took on during the tick just finished. The graph's edge colour is
    /// computed from it — intake is what "working at this fraction of throughput" means — and a
    /// snapshot rebuilt after a load must not read zero.
    /// </summary>
    public long LoadedLastTick { get; set; }

    /// <summary>
    /// How much this line put down at its destination during the tick just finished. Separate from
    /// <see cref="LoadedLastTick"/> because with a belt the two differ: a line whose source ran dry
    /// is still delivering, and a line that has just started carries without having arrived.
    /// </summary>
    public long DeliveredLastTick { get; set; }

    public long PowerDrawLastTick { get; set; }

    public ExecutorStatus Status { get; set; } = ExecutorStatus.NoTasksQueued;

    public PostponeReason? BlockReason { get; set; }

    /// <summary>What is on the belt, in milli-units, over every slot.</summary>
    public long CargoQuantity
    {
        get
        {
            var total = 0L;
            foreach (var slot in Belt)
            {
                if (slot is not null)
                {
                    total += slot.Quantity;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Sizes the belt to <see cref="LengthTicks"/>, discarding whatever was on it. Called once per
    /// line, by the seeder or by a load — the belt is never resized while a world is running,
    /// because a slot's index is how far its cargo still has to travel.
    /// </summary>
    public void SizeBelt()
    {
        Belt.Clear();
        for (var i = 0L; i < LengthTicks; i++)
        {
            Belt.Add(null);
        }
    }
}

/// <summary>
/// A power source. Declared and seeded empty: energy is still a constant, and the fuel-burning
/// power core that fills this is separate work.
/// </summary>
public sealed class ReactorInstance
{
    public required ExecutorId Id { get; init; }

    public required ReactorArchetypeId Archetype { get; init; }

    public string? NameOverride { get; set; }

    public required bool Built { get; set; }

    public long IntegrityPermille { get; set; } = 1000;

    public required StorageId FuelStore { get; set; }

    /// <summary>Throttled by a program or by the player.</summary>
    public long OutputPermille { get; set; } = 1000;

    public List<ProgramInstanceId> Programs { get; } = new();

    public ExecutorStatus Status { get; set; } = ExecutorStatus.NoTasksQueued;

    public PostponeReason? BlockReason { get; set; }

    public required UtilizationWindow Utilization { get; init; }
}

/// <summary>
/// The vessel's energy budget. Capacity is state rather than content on purpose: it is the kind of
/// thing upgrades and damage move, and there is no vessel archetype to hold a base for it to
/// diverge from.
/// </summary>
public sealed class EnergyLedger
{
    public required long Capacity { get; set; }

    /// <summary>Last tick's granted total, saved so a rebuilt snapshot is not a cold start.</summary>
    public long DrawLastTick { get; set; }

    public int CapHits { get; set; }

    public int StarvedTicks { get; set; }
}

/// <summary>
/// The vessel's compute budget, which has the same shape as energy: a capacity, a draw, and a
/// refusal that lands on a task as a postponement.
/// <para>
/// Two ledgers rather than one dictionary keyed by resource kind. There are two, the engine charges
/// them at different points in a tick, and a dictionary would buy generality nothing has asked for
/// while making the charging order — which is what determinism rests on — implicit.
/// </para>
/// <para>Declared and seeded at zero; charging it is separate work.</para>
/// </summary>
public sealed class ComputeLedger
{
    public long Capacity { get; set; }

    public long DrawLastTick { get; set; }

    public int CapHits { get; set; }

    public int StarvedTicks { get; set; }
}

/// <summary>
/// Material withheld from consumers by an installed program. State, because it changes what the
/// next tick produces rather than what the next frame shows.
/// <para>
/// Owned by a <see cref="ProgramInstanceId"/> and not a <see cref="ProgramId"/>: two installations
/// of one program each hold their own, and keying by the definition would make them
/// indistinguishable at exactly the moment the engine needs to tell them apart.
/// </para>
/// </summary>
public sealed record Reservation(
    StorageId Storage, ItemId Item, long Quantity, ProgramInstanceId Owner);

/// <summary>Reservations, in declaration order. Nothing holds one yet.</summary>
public sealed class ReservationLedger
{
    public List<Reservation> Held { get; } = new();
}

/// <summary>
/// The vessel: what it is made of, what is in it, and what it is spending. Declaration order is
/// significant throughout — it is the order executors claim power and are stepped in, and the order
/// every projection is built in, and it is what makes the simulation deterministic.
/// </summary>
public sealed class VesselState
{
    /// <summary>
    /// The one global storage every plan routes material through, by id.
    /// <para>
    /// It sits here rather than being read from the scenario for the same reason a facility's
    /// local storage and a line's endpoints do: it is topology, and topology is buildable. It is
    /// seeded from the scenario, which is where a content author names it.
    /// </para>
    /// </summary>
    public required StorageId Hold { get; set; }

    public List<StorageInstance> Storages { get; } = new();

    public List<FacilityInstance> Facilities { get; } = new();

    public List<TransportInstance> Transports { get; } = new();

    public List<ReactorInstance> Reactors { get; } = new();

    /// <summary>
    /// Which sinks this vessel has. A sink has no queue, no configuration and nothing that changes,
    /// so it gets no instance type and its draw is read from the catalog. An archetype/instance
    /// pair for a record with no dynamic half is ceremony.
    /// </summary>
    public List<PowerSinkId> Sinks { get; } = new();

    public required EnergyLedger Energy { get; init; }

    public required ComputeLedger Compute { get; init; }

    public required ReservationLedger Reservations { get; init; }
}
