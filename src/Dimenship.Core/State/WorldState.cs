using Dimenship.Core.Content;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State;

/// <summary>
/// Everything about one world that changes during play, addressable and serialisable.
/// <para>
/// Mutable classes rather than records rebuilt with <c>with</c>: the engine mutates this hundreds
/// of times per tick, and the immutability that matters is <see cref="WorldSnapshot"/>'s.
/// </para>
/// <para>
/// The rule that keeps state from becoming a second rulebook: <b>state stores ids and deltas, never
/// definitions, and never a value the catalog can already answer.</b> A label copied onto an
/// instance means a rename in content never reaches an existing save.
/// </para>
/// <para>
/// A save version and a content version are deliberately absent. They describe the file, not the
/// world; holding them here as well would be holding two answers to one question, and the day they
/// disagreed the loader would have no way to tell which was right.
/// </para>
/// </summary>
public sealed class WorldState
{
    /// <summary>The retained scenario this world runs on, by id.</summary>
    public required string ScenarioId { get; set; }

    public required OperationalClock Clock { get; init; }

    public required RandomState Random { get; init; }

    public required VesselState Vessel { get; init; }

    public required TaskRegistry Tasks { get; init; }

    public required ProgressLedger Progress { get; init; }

    public required PlanRegistry Plans { get; init; }

    public required MissionLedger Missions { get; init; }

    public required AlertLedger Alerts { get; init; }

    public required JournalLedger Journal { get; init; }

    // Declared, deferred. Each is a domain of its own rather than a field to fill in here.

    public required ProgramLedger Programs { get; init; }

    public required RobotLedger Robots { get; init; }

    public required CaseLedger Case { get; init; }

    /// <summary>
    /// The name to show for a facility: its own if the player gave it one, and the archetype's
    /// otherwise. Resolved here and nowhere else, so no call site can forget the fallback.
    /// </summary>
    public static string NameOf(ContentCatalog catalog, FacilityInstance facility) =>
        facility.NameOverride ?? catalog.Facility(facility.Archetype)?.Label ?? facility.Id.Value;

    /// <inheritdoc cref="NameOf(ContentCatalog, FacilityInstance)"/>
    public static string NameOf(ContentCatalog catalog, TransportInstance transport) =>
        transport.NameOverride ?? catalog.Transport(transport.Archetype)?.Label ?? transport.Id.Value;

    /// <inheritdoc cref="NameOf(ContentCatalog, FacilityInstance)"/>
    public static string NameOf(ContentCatalog catalog, StorageInstance storage) =>
        storage.NameOverride ?? catalog.Storage(storage.Archetype)?.Label ?? storage.Id.Value;

    /// <inheritdoc cref="NameOf(ContentCatalog, FacilityInstance)"/>
    public static string NameOf(ContentCatalog catalog, ReactorInstance reactor) =>
        reactor.NameOverride ?? catalog.Reactor(reactor.Archetype)?.Label ?? reactor.Id.Value;
}
