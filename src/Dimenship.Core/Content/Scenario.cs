using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Content;

/// <summary>
/// One campaign's starting position, expressed against the catalog: which machines this vessel
/// has, where they sit, what is aboard at tick zero, and what is already running.
/// <para>
/// The scenario is <b>retained, not discarded</b>. It authors every node slot the campaign will
/// ever show, including the ones nothing has been built in yet, which is what a layout that
/// reveals facilities as they are built requires. Placement therefore stays in content and never
/// reaches a save: edit a layout, and existing campaigns get the edit.
/// </para>
/// </summary>
public sealed record Scenario(
    string Id,
    string Label,
    long EnergyCapacity,

    /// <summary>
    /// The one global Resource Storage plans route material through. Named rather than positional:
    /// the engine reads the first declared storage today, which is a convention enforced by
    /// nothing and breakable by reordering a JSON array.
    /// </summary>
    StorageId Hold,
    IReadOnlyList<ScenarioStorage> Storages,
    IReadOnlyList<ScenarioFacility> Facilities,
    IReadOnlyList<ScenarioRoute> Routes,

    /// <summary>
    /// Where the power core is drawn. Authored like every other cell rather than pinned by the
    /// view: a layout whose coordinates are fixed everywhere except for one node is a layout with
    /// a node nobody can move.
    /// </summary>
    NodePlacement Power,
    IReadOnlyList<PowerSinkId> Sinks,
    IReadOnlyList<SchematicId> UnlockedSchematics,
    IReadOnlyList<ScenarioTask> InitialTasks,
    IReadOnlyList<ScenarioTransfer> InitialTransfers);

/// <summary>
/// A storage this campaign starts with.
/// <para>
/// <see cref="Placement"/> is null for a facility's local buffer, which has no card of its own:
/// it is drawn inside the card of the facility that works it, so a route ending at the buffer ends
/// at that facility. A storage that is neither placed nor any facility's buffer is a content error.
/// </para>
/// </summary>
public sealed record ScenarioStorage(
    StorageId Id,
    StorageArchetypeId Archetype,
    string? NameOverride,
    IReadOnlyList<ItemAmount> Initial,
    NodePlacement? Placement);

/// <summary>
/// A facility this campaign has, or will have. <see cref="BuiltAtStart"/> false authors a slot the
/// campaign reveals when something is built in it — the placement exists from the first frame, and
/// only the machine standing in it is dynamic.
/// </summary>
public sealed record ScenarioFacility(
    ExecutorId Id,
    FacilityArchetypeId Archetype,

    /// <summary>"Factory Alpha". Null leaves the archetype's own label, which is the same
    /// null-means-ask-content indirection the rest of the tier uses.</summary>
    string? NameOverride,
    StorageId LocalStorage,
    SchematicId? InitialSchematic,
    bool BuiltAtStart,
    NodePlacement Placement);

/// <summary>
/// A transport line the campaign starts with. It is an edge, so it has no placement: the graph
/// draws it between the storages its route joins.
/// </summary>
public sealed record ScenarioRoute(
    ExecutorId Id,
    TransportArchetypeId Archetype,
    string? NameOverride,
    StorageId From,
    StorageId To,
    bool BuiltAtStart);

/// <summary>
/// A task the campaign starts with. <c>Runs</c> is null for a standing order — produce for as long
/// as the inputs keep arriving.
/// </summary>
public sealed record ScenarioTask(SchematicId Schematic, int? Runs, ExecutorId Executor);

/// <summary>
/// A transfer the campaign starts with. <c>Quantity</c> is null for a standing order — haul this
/// item down this line for as long as there is any to haul.
/// </summary>
public sealed record ScenarioTransfer(
    ItemId Item, long? Quantity, StorageId From, StorageId To, ExecutorId Executor);
