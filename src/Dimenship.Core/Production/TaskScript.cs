using Dimenship.Core.Programs;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// What a task does when it runs. Production and transport collapse into one shape so a plan can
/// be one ordered list of scripts rather than two parallel queues that Commit has to stitch.
/// </summary>
public abstract record TaskAction;

/// <summary>
/// Run a schematic a number of times, or indefinitely when <paramref name="Runs"/> is null.
/// Named for the verb rather than <c>ProduceAction</c>: the type lives in
/// <see cref="Dimenship.Core.Production"/> where nothing else claims the word.
/// </summary>
public sealed record Produce(SchematicId Schematic, int? Runs) : TaskAction;

/// <summary>
/// Move an item between two storages, or haul indefinitely when <paramref name="Quantity"/> is
/// null. Same naming rule as <see cref="Produce"/>.
/// </summary>
public sealed record Transfer(ItemId Item, long? Quantity, StorageId From, StorageId To) : TaskAction;

/// <summary>
/// A task as authored: conditions that gate starting, and the action that runs when they hold.
/// Empty conditions mean attempt every tick — the byte-identical behaviour every planner and
/// scenario task already had before conditions existed.
/// </summary>
public sealed record TaskScript(IReadOnlyList<Condition> Conditions, TaskAction Action);
