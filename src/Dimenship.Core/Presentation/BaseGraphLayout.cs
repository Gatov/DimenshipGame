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
    /// The vessel the shell starts on, laid out as the GDD's production layer describes it: the
    /// one global Resource Storage in the middle, the emergency extractor directly above it, the
    /// Matter Reactors down the left, the factory array down the right in the order work passes
    /// along it, and the Mission Docks along the bottom, joined to storage and to nothing else.
    /// <para>
    /// Badges number the chain — extract, store, separate, fabricate, launch — so a player reading
    /// the graph from the extractor onwards reads them in order. Sibling letters (<c>3A</c>,
    /// <c>4B</c>) name the members of a tier that has more than one.
    /// </para>
    /// <para>
    /// Reactor Alpha and Factory Beta share the storage's row and so are joined to it by a straight
    /// line; everything else elbows in the gutter. The factory interconnects join adjacent cells
    /// only, which is what keeps an edge from being drawn straight through the card between them.
    /// </para>
    /// </summary>
    public static BaseGraphLayout ForDefaultWorld() =>
        new(
            new Dictionary<ExecutorId, NodePlacement>
            {
                // Directly above Resource Storage, so the one route that brings anything aboard
                // without a mission is a straight line down the middle rather than an elbow crossing
                // the reactors' lines.
                [WorldDefinition.Extractor01] = new(Column: 2, Row: 0, Badge: "1"),

                [WorldDefinition.ReactorA] = new(Column: 0, Row: 2, Badge: "3A"),
                [WorldDefinition.ReactorB] = new(Column: 0, Row: 3, Badge: "3B"),

                [WorldDefinition.FactoryA] = new(Column: 4, Row: 1, Badge: "4A"),
                [WorldDefinition.FactoryB] = new(Column: 4, Row: 2, Badge: "4B"),
                [WorldDefinition.FactoryC] = new(Column: 4, Row: 3, Badge: "4C"),

                [WorldDefinition.DockA] = new(Column: 1, Row: 4, Badge: "5A"),
                [WorldDefinition.DockB] = new(Column: 3, Row: 4, Badge: "5B"),
            },
            new Dictionary<StorageId, NodePlacement>
            {
                [WorldDefinition.ResourceStorage] = new(Column: 2, Row: 2, Badge: "2"),
            },

            // Authored like every other cell rather than pinned by the view, because a layout whose
            // coordinates are fixed everywhere except for one node is a layout with a node nobody
            // can move. It is still edgeless: energy is a global pool, and drawing power lines to
            // the facilities that draw from it would be a lie about how it works — which is also
            // why it can sit in the corner without leaving anything stranded.
            Power: new(Column: 4, Row: 0, Badge: "P"));
}
