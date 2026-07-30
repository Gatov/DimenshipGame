using Dimenship.Core.Production;

namespace Dimenship.Core.Simulation;

/// <summary>
/// A kind of item. <paramref name="HoldCapacity"/> is how much of it a full-sized storage holds;
/// a smaller storage holds a fraction of that (see <see cref="StorageDefinition"/>).
/// </summary>
public sealed record ItemDefinition(ItemId Id, string Label, long HoldCapacity);

/// <summary>
/// A place items sit. Capacity is expressed as a permille of each item's
/// <see cref="ItemDefinition.HoldCapacity"/> rather than as a per-item table: a facility's local
/// buffer is "one percent of a hold" for every item at once, which is one number instead of a
/// table that has to be revisited every time an item is added.
/// <para>
/// Per-item overrides are a deliberate omission. Nothing in the production model needs them, and
/// adding them later changes no call site.
/// </para>
/// </summary>
public sealed record StorageDefinition(
    StorageId Id,
    string Label,
    long CapacityPermille,
    IReadOnlyList<ItemAmount> Initial)
{
    public const long FullHold = 1000;
}

/// <summary>
/// A facility's fixed configuration. <paramref name="Input"/> and <paramref name="Output"/> are
/// null for facilities that only consume power, such as the stabilization field.
/// </summary>
public sealed record FacilityDefinition(
    FacilityId Id,
    FacilityKind Kind,
    StorageId Storage,
    long PowerDraw,
    ItemId? Input,
    long InputPerTick,
    ItemId? Output,
    long OutputPerTick);

/// <summary>
/// The starting configuration of a world. Facility order is significant: it is the order in
/// which facilities claim power each tick, and it is what makes the simulation deterministic.
/// Item and storage order are significant for the same reason — every ordered projection on the
/// snapshot is built by walking these lists.
/// </summary>
public sealed record WorldDefinition(
    long EnergyCapacity,
    SchematicCatalog Schematics,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<StorageDefinition> Storages,
    IReadOnlyList<FacilityDefinition> Facilities)
{
    public static readonly ItemId Ore = new("ore");
    public static readonly ItemId Alloy = new("alloy");

    public static readonly StorageId MainHold = new("main_hold");

    public static WorldDefinition CreateDefault()
    {
        var schematics = new SchematicCatalog(
            new[]
            {
                new SchematicDefinition
                {
                    Id = new SchematicId("extract_ore"),
                    Output = new ItemAmount(Ore, 2_400),
                    Inputs = Array.Empty<ItemAmount>(),
                    EffortPerRun = new WorkAmount(100),
                    EnergyPerRun = new EnergyAmount(4_000),
                    RequiredFacilityType = FacilityType.Extractor,
                },
                new SchematicDefinition
                {
                    Id = new SchematicId("smelt_alloy"),
                    Output = new ItemAmount(Alloy, 8_000),
                    Inputs = new[] { new ItemAmount(Ore, 40_000) },
                    EffortPerRun = new WorkAmount(100),
                    EnergyPerRun = new EnergyAmount(2_000),
                    RequiredFacilityType = FacilityType.Refinery,
                },
            },
            new[] { new SchematicId("extract_ore"), new SchematicId("smelt_alloy") });

        return new WorldDefinition(
            EnergyCapacity: 10_000,
            Schematics: schematics,
            Items: new[]
            {
                new ItemDefinition(Ore, "Ore", HoldCapacity: 2_000_000),
                new ItemDefinition(Alloy, "Alloy", HoldCapacity: 500_000),
            },
            Storages: new[]
            {
                new StorageDefinition(
                    MainHold, "Main Hold", StorageDefinition.FullHold, Array.Empty<ItemAmount>()),
            },
            Facilities: new[]
            {
                // Draws power unconditionally and produces nothing. GDD: the stabilization
                // field is the vessel's permanent energy sink.
                new FacilityDefinition(
                    new FacilityId("stabilization_field"), FacilityKind.StabilizationField, MainHold,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: null, OutputPerTick: 0),

                new FacilityDefinition(
                    new FacilityId("extractor_01"), FacilityKind.Extractor, MainHold,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 2_400),

                // Needs ~17 ticks of extractor output per run, so it alternates between
                // BlockMissingInput and Run rather than sitting in one state.
                new FacilityDefinition(
                    new FacilityId("smelter_a"), FacilityKind.Smelter, MainHold,
                    PowerDraw: 2_000, Input: Ore, InputPerTick: 40_000, Output: Alloy, OutputPerTick: 8_000),
            });
    }
}
