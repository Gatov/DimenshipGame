namespace Dimenship.Core.Simulation;

public sealed record ResourceDefinition(ResourceId Id, long Capacity, long InitialAmount);

/// <summary>
/// A facility's fixed configuration. <paramref name="Input"/> and <paramref name="Output"/> are
/// null for facilities that only consume power, such as the stabilization field.
/// </summary>
public sealed record FacilityDefinition(
    FacilityId Id,
    FacilityKind Kind,
    long PowerDraw,
    ResourceId? Input,
    long InputPerTick,
    ResourceId? Output,
    long OutputPerTick);

/// <summary>
/// The starting configuration of a world. Facility order is significant: it is the order in
/// which facilities claim power each tick, and it is what makes the simulation deterministic.
/// </summary>
public sealed record WorldDefinition(
    long EnergyCapacity,
    IReadOnlyList<ResourceDefinition> Resources,
    IReadOnlyList<FacilityDefinition> Facilities)
{
    public static readonly ResourceId Ore = new("ore");
    public static readonly ResourceId Alloy = new("alloy");

    public static WorldDefinition CreateDefault() =>
        new(
            EnergyCapacity: 10_000,
            Resources: new[]
            {
                new ResourceDefinition(Ore, Capacity: 2_000_000, InitialAmount: 0),
                new ResourceDefinition(Alloy, Capacity: 500_000, InitialAmount: 0),
            },
            Facilities: new[]
            {
                // Draws power unconditionally and produces nothing. GDD: the stabilization
                // field is the vessel's permanent energy sink.
                new FacilityDefinition(
                    new FacilityId("stabilization_field"), FacilityKind.StabilizationField,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: null, OutputPerTick: 0),

                new FacilityDefinition(
                    new FacilityId("extractor_01"), FacilityKind.Extractor,
                    PowerDraw: 4_000, Input: null, InputPerTick: 0, Output: Ore, OutputPerTick: 2_400),

                // Needs ~17 ticks of extractor output per run, so it alternates between
                // BlockMissingInput and Run rather than sitting in one state.
                new FacilityDefinition(
                    new FacilityId("smelter_a"), FacilityKind.Smelter,
                    PowerDraw: 2_000, Input: Ore, InputPerTick: 40_000, Output: Alloy, OutputPerTick: 8_000),
            });
}
