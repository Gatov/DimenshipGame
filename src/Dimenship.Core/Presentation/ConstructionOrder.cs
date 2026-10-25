using Dimenship.Core.Content;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// The order that builds one unbuilt slot: one whole construction unit, delivered into the slot's
/// own buffer, with the slot as the draft's assembly target. The inspector's BUILD and the
/// Operations build composer both ask for it here, where each once assembled it by hand.
/// <para>
/// Both halves of the rule are load-bearing. Commissioning withdraws exactly
/// <see cref="SimulationEngine.WholeConstructionUnit"/> from the slot's local storage, so an order
/// for less never builds and an order for more leaves the rest sitting in a buffer. And the unit
/// has to be delivered into that buffer: ordered to the hold, it arrives, the plan reads complete,
/// and the facility never builds.
/// </para>
/// <para>
/// The replay harness keeps its own one-line "a construction demand with no destination delivers
/// into the slot's buffer" default, because a script names its own quantity.
/// </para>
/// </summary>
public sealed record ConstructionOrder(ItemAmount Goal, StorageId Destination, ExecutorId Slot)
{
    /// <summary>
    /// The order for <paramref name="slot"/>, or null when its archetype names no construction
    /// unit, which is a facility that is never commissioned this way.
    /// </summary>
    public static ConstructionOrder? For(FacilityArchetype archetype, ExecutorId slot, StorageId buffer) =>
        archetype.ConstructionUnit is { } unit
            ? new ConstructionOrder(new ItemAmount(unit, SimulationEngine.WholeConstructionUnit), buffer, slot)
            : null;
}
