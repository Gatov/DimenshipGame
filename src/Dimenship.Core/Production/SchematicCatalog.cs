using Dimenship.Core.Simulation;

namespace Dimenship.Core.Production;

/// <summary>
/// The schematics a world contains.
/// <para>
/// It holds no unlock set. What a player has unlocked is campaign progress — it changes during
/// play and differs between two players running the same build — so it belongs to the world, not
/// to the rulebook. The planner asks <c>IWorldView.IsUnlocked</c>.
/// </para>
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

    public SchematicCatalog(IReadOnlyList<SchematicDefinition> schematics)
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
    }

    /// <summary>Every schematic in the world, in declaration order.</summary>
    public IReadOnlyList<SchematicDefinition> All { get; }

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
}
