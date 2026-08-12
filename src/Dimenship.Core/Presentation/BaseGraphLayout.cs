using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// Where a node sits on the base graph's grid, and what the player calls it.
/// <para>
/// The badge is authored rather than derived from the cell. It names a tier and a sibling within
/// it — the concept's <c>2A</c> and <c>3B</c> — and no grid coordinate encodes that: two facilities
/// of one tier can sit anywhere on the grid, and moving a card must not rename it.
/// </para>
/// </summary>
public sealed record NodePlacement(int Column, int Row, string Badge);

/// <summary>
/// Where every node of a world is drawn. Grid cells, not pixels: pixel geometry belongs to the
/// view and changes with zoom, while "the reactors sit left of the storage" is content.
/// <para>
/// This lives beside the world definition rather than in the shell for two reasons. The shell
/// cannot name an <see cref="ExecutorId"/> — it knows panels as identifiers and nothing else, and
/// that reference direction is load-bearing. And putting placements here makes "every executor is
/// placed, no two share a cell, every route endpoint is drawn somewhere" a test assertion rather
/// than a bug found by looking at the screen. It carries no rendering type and no float.
/// </para>
/// <para>
/// Transport executors are absent by design: they are edges, drawn between the nodes their route
/// joins. Facility buffers are absent for a different reason — they are drawn inside the card of
/// the facility that works them, which <see cref="BaseGraphNodes"/> resolves.
/// </para>
/// </summary>
public sealed record BaseGraphLayout(
    IReadOnlyDictionary<ExecutorId, NodePlacement> Producers,
    IReadOnlyDictionary<StorageId, NodePlacement> Storages,
    NodePlacement Power)
{
    /// <summary>
    /// The layout of one world: the scenario's authored slots, filtered to what the state says has
    /// been built.
    /// <para>
    /// Projected rather than authored in code. The scenario is retained precisely so this can be:
    /// it holds every slot the campaign will ever show, including the ones nothing stands in yet,
    /// which is what a layout that reveals facilities as they are built requires. A slot the state
    /// says is unbuilt is absent here, and the graph draws it as reveal-pending or not at all.
    /// </para>
    /// <para>
    /// The power cell is the scenario's too. It is authored like every other cell rather than
    /// pinned by the view, because a layout whose coordinates are fixed everywhere except for one
    /// node is a layout with a node nobody can move. It is still edgeless: energy is a global pool,
    /// and drawing power lines to the facilities that draw from it would be a lie about how it
    /// works — which is also why it can sit in a corner without stranding anything.
    /// </para>
    /// </summary>
    public static BaseGraphLayout For(Content.Scenario scenario, State.WorldState state)
    {
        var built = new HashSet<ExecutorId>();
        foreach (var facility in state.Vessel.Facilities)
        {
            if (facility.Built)
            {
                built.Add(facility.Id);
            }
        }

        var producers = new Dictionary<ExecutorId, NodePlacement>();
        foreach (var slot in scenario.Facilities)
        {
            if (built.Contains(slot.Id))
            {
                producers[slot.Id] = slot.Placement;
            }
        }

        var storages = new Dictionary<StorageId, NodePlacement>();
        foreach (var slot in scenario.Storages)
        {
            if (slot.Placement is { } placement)
            {
                storages[slot.Id] = placement;
            }
        }

        return new BaseGraphLayout(producers, storages, scenario.Power);
    }
}
