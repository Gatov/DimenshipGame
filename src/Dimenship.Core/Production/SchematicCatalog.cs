using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// The schematics a world contains, and which of them the player has unlocked.
/// <para>
/// Every ordered result is built from the declaration list, never from dictionary enumeration:
/// the planner picks among candidates in this order, so an unstable order would make planning
/// non-deterministic.
/// </para>
/// </summary>
public sealed class SchematicCatalog
{
    private readonly Dictionary<SchematicId, SchematicDefinition> _byId;
    private readonly Dictionary<ItemId, List<SchematicDefinition>> _byOutput;
    private readonly HashSet<SchematicId> _unlocked;

    public SchematicCatalog(
        IReadOnlyList<SchematicDefinition> schematics,
        IReadOnlyCollection<SchematicId> unlocked)
    {
        All = schematics;
        _byId = new Dictionary<SchematicId, SchematicDefinition>(schematics.Count);
        _byOutput = new Dictionary<ItemId, List<SchematicDefinition>>();

        foreach (var schematic in schematics)
        {
            if (!_byId.TryAdd(schematic.Id, schematic))
            {
                throw new ArgumentException(
                    $"Duplicate schematic id '{schematic.Id}'.", nameof(schematics));
            }

            if (!_byOutput.TryGetValue(schematic.Output.Item, out var producers))
            {
                producers = new List<SchematicDefinition>();
                _byOutput[schematic.Output.Item] = producers;
            }

            producers.Add(schematic);
        }

        foreach (var id in unlocked)
        {
            if (!_byId.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Cannot unlock unknown schematic '{id}'.", nameof(unlocked));
            }
        }

        _unlocked = new HashSet<SchematicId>(unlocked);
    }

    /// <summary>Every schematic in the world, in declaration order.</summary>
    public IReadOnlyList<SchematicDefinition> All { get; }

    public bool IsUnlocked(SchematicId id) => _unlocked.Contains(id);

    public bool TryGet(SchematicId id, out SchematicDefinition schematic) =>
        _byId.TryGetValue(id, out schematic!);

    public SchematicDefinition Get(SchematicId id) =>
        _byId.TryGetValue(id, out var schematic)
            ? schematic
            : throw new KeyNotFoundException($"No schematic '{id}' in this catalog.");

    /// <summary>
    /// Every schematic producing the given item, in declaration order, whether unlocked or not.
    /// Multiple schematics may produce one output; the player selects among them.
    /// </summary>
    public IReadOnlyList<SchematicDefinition> ForOutput(ItemId item) =>
        _byOutput.TryGetValue(item, out var producers)
            ? producers
            : Array.Empty<SchematicDefinition>();

    /// <summary>
    /// The unlocked subset of <see cref="ForOutput"/>. The planner may only build a production
    /// branch from these; a locked schematic is a shortage, not a plan.
    /// </summary>
    public IReadOnlyList<SchematicDefinition> UnlockedForOutput(ItemId item)
    {
        var candidates = ForOutput(item);
        if (candidates.Count == 0)
        {
            return Array.Empty<SchematicDefinition>();
        }

        var unlocked = new List<SchematicDefinition>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (_unlocked.Contains(candidate.Id))
            {
                unlocked.Add(candidate);
            }
        }

        return unlocked;
    }
}
