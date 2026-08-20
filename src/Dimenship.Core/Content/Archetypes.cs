using Dimenship.Core.Simulation;

namespace Dimenship.Core.Content;

/// <summary>
/// A kind of item. <paramref name="HoldCapacity"/> is how much of it a full-sized storage holds;
/// a smaller storage holds a permille fraction of that.
/// </summary>
public sealed record ItemDefinition(ItemId Id, string Label, long HoldCapacity);

/// <summary>
/// Something that draws power and does nothing else, such as the stabilization field. It owns no
/// queue and executes no schematic, so forcing it through <see cref="FacilityType"/> would be a
/// fiction — and it has no dynamic half, so it gets no instance type either. A vessel holds the
/// ids of the sinks it has, and the draw is read from here.
/// </summary>
public sealed record PowerSinkDefinition(PowerSinkId Id, string Label, long PowerDraw);

/// <summary>
/// What a class of production facility is, before one is built and named.
/// <para>
/// <see cref="Commandable"/> is false for a passive source. The GDD says three times that the
/// Emergency Hydrogen Extractor is not an automation node and cannot be disabled by player
/// programs, and without a flag it becomes commandable by construction the moment programs are
/// installable — not because anyone decided it should be, but because it is a facility in a list
/// of facilities. That failure is one extra row in a target picker, which is the kind that gets
/// noticed late. A rule the loader enforces fails loudly instead.
/// </para>
/// </summary>
public sealed record FacilityArchetype(
    FacilityArchetypeId Id,
    string Label,
    FacilityType Type,
    long WorkRatePerTick,
    long StandingPowerDraw,
    long SwitchOverTicks,
    long BufferPermille,
    bool Commandable);

/// <summary>
/// What a class of transport line is. It has no configuration and so no switch-over: a line is
/// built between two places and carries whatever is handed to it.
/// </summary>
public sealed record TransportArchetype(
    TransportArchetypeId Id,
    string Label,
    long ThroughputPerTick,
    long StandingPowerDraw);

/// <summary>
/// What a class of storage is. Storages get an archetype for the same reason facilities do:
/// capacity is a property of the kind of hold, not of the save. Opening stock is not here — that
/// is a campaign's starting position, and it rides on the scenario placement.
/// </summary>
public sealed record StorageArchetype(
    StorageArchetypeId Id,
    string Label,
    long CapacityPermille)
{
    /// <summary>A storage that holds every item's full hold capacity.</summary>
    public const long FullHold = 1000;

    /// <summary>
    /// A full storage, in the units its occupancy is measured in. A storage is one volume that
    /// every item competes for: an item's capacity says how much of that item alone would fill
    /// it, so a milli-unit of that item occupies one <paramref name="FullVolume"/>-th of the
    /// storage divided by that capacity, and the shares add up.
    /// <para>
    /// A billion rather than <see cref="FullHold"/>'s thousand because every share is floored.
    /// At permille a seven-item hold could hide most of a percent of itself in rounding, and a
    /// percent of the shipped vessel's hold is fifty thousand milli-units of Matter Mix. At a
    /// billion the loss is below one part in a hundred million even when every item is held.
    /// </para>
    /// </summary>
    public const long FullVolume = 1_000_000_000;
}

/// <summary>
/// What a class of reactor is. Declared here with its schema and shipped empty: energy is still a
/// constant of the scenario, and the fuel-burning power core that gives this record content is
/// separate work. A catalog field with no file is a field the loader cannot load, which is why the
/// file exists before it has rows.
/// </summary>
public sealed record ReactorArchetype(
    ReactorArchetypeId Id,
    string Label,
    ItemId Fuel,
    long FuelPerTick,
    long EnergyPerFuel,
    long CapacityCeiling);

/// <summary>
/// A mission target stratum: where an expedition goes and what it brings back. Declared with its
/// schema and shipped empty for the same reason a reactor is — acquisition is not a system yet,
/// and a dock that reports idle is telling the truth.
/// </summary>
public sealed record StratumDefinition(
    StratumId Id,
    string Label,
    IReadOnlyList<ItemAmount> Yields,
    long TravelTicks,
    long EnergyCost,
    long HazardPermille);
