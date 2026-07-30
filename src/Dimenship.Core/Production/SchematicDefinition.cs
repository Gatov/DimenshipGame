using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// How one item is produced. The authoritative production instructions: a task references a
/// schematic by id and never copies its contents, so a facility upgrade that changes work rate
/// or energy efficiency changes execution without touching the schematic or any task in flight.
/// </summary>
public sealed record SchematicDefinition
{
    public required SchematicId Id { get; init; }

    /// <summary>One execution of the schematic produces this quantity.</summary>
    public required ItemAmount Output { get; init; }

    /// <summary>Materials and components consumed by one execution. May be empty, for extraction.</summary>
    public required IReadOnlyList<ItemAmount> Inputs { get; init; }

    /// <summary>
    /// Determines production time together with the executor's work rate:
    /// a run lasts <c>ceil(EffortPerRun / WorkRatePerTick)</c> ticks.
    /// </summary>
    public required WorkAmount EffortPerRun { get; init; }

    /// <summary>
    /// Base total energy consumed by one execution, charged in proportion to the work actually
    /// done. Independent of effort: a low-energy item may take a long time.
    /// </summary>
    public required EnergyAmount EnergyPerRun { get; init; }

    /// <summary>Which class of production facility executes this schematic.</summary>
    public required FacilityType RequiredFacilityType { get; init; }
}
