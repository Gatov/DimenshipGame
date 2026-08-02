namespace Dimenship.Shell;

/// <summary>What kind of thing a graph node stands for.</summary>
public enum GraphNodeKind
{
    /// <summary>A production facility.</summary>
    Executor,

    /// <summary>A transport line. Drawn as an edge, so it has no card of its own.</summary>
    Transport,

    /// <summary>A storage.</summary>
    Storage,

    /// <summary>The vessel's energy pool. Pinned and edgeless — energy is global.</summary>
    Power,
}

/// <summary>
/// What the player has selected in the graph. The identifier is a raw string rather than an
/// <c>ExecutorId</c> or <c>StorageId</c> because this assembly deliberately references nothing:
/// it knows panels and selections as identifiers and leaves resolving them to whoever holds a
/// snapshot.
/// </summary>
public readonly record struct GraphSelection(GraphNodeKind Kind, string Id);
