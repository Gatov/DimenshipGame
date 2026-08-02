using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>Where a node sits on the base graph's grid.</summary>
public sealed record NodePlacement(int Column, int Row);

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
    /// </summary>
    public static BaseGraphLayout ForDefaultWorld() =>
        new(
            new Dictionary<ExecutorId, NodePlacement>
            {
                [WorldDefinition.Extractor01] = new(Column: 0, Row: 0),
                [WorldDefinition.SmelterA] = new(Column: 2, Row: 0),
            },
            new Dictionary<StorageId, NodePlacement>
            {
                [WorldDefinition.MainHold] = new(Column: 0, Row: 1),
                [WorldDefinition.SmelterBuffer] = new(Column: 2, Row: 1),
            });
}
