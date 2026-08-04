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
/// view and changes with zoom, while "the smelter sits right of the hold" is content.
/// <para>
/// This lives beside the world definition rather than in the shell for two reasons. The shell
/// cannot name an <see cref="ExecutorId"/> — it knows panels as identifiers and nothing else, and
/// that reference direction is load-bearing. And putting placements here makes "every executor and
/// storage is placed, no two share a cell, every route endpoint is placed" a test assertion rather
/// than a bug found by looking at the screen. It carries no rendering type and no float.
/// </para>
/// <para>
/// Transport executors are absent by design: they are edges, drawn between the storages their
/// route joins. So is power, which the view pins — energy is a global pool with nowhere to sit.
/// </para>
/// </summary>
public sealed record BaseGraphLayout(
    IReadOnlyDictionary<ExecutorId, NodePlacement> Producers,
    IReadOnlyDictionary<StorageId, NodePlacement> Storages)
{
    /// <summary>
    /// The vessel the shell starts on, read left to right along the row material travels: the
    /// hold feeds the smelter's buffer, and each facility sits above the storage it works.
    /// <para>
    /// Badges number the same path — extract, hold, smelt, hold — so a player reading the graph
    /// left to right reads them in order. Sibling letters (<c>3A</c>, <c>3B</c>) appear when a
    /// tier gains siblings; this vessel has none.
    /// </para>
    /// </summary>
    public static BaseGraphLayout ForDefaultWorld() =>
        new(
            new Dictionary<ExecutorId, NodePlacement>
            {
                [WorldDefinition.Extractor01] = new(Column: 0, Row: 0, Badge: "1"),
                [WorldDefinition.SmelterA] = new(Column: 2, Row: 0, Badge: "3"),
            },
            new Dictionary<StorageId, NodePlacement>
            {
                [WorldDefinition.MainHold] = new(Column: 0, Row: 1, Badge: "2"),
                [WorldDefinition.SmelterBuffer] = new(Column: 2, Row: 1, Badge: "4"),
            });
}
