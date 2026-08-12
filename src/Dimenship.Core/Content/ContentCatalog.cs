using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Content;

/// <summary>
/// Everything static, linked and validated: the rulebook, with no campaign in it.
/// <para>
/// It is loaded <b>once per process</b>, is immutable, and is shared by every save opened during
/// that run. It is never copied into a save and never varies per save — a save references it by
/// <see cref="ContentVersion"/> and by ids, and by nothing else.
/// </para>
/// <para>
/// That is the strong form of "static", and it is worth naming because the weak form — a catalog
/// that is merely a separate object, but is constructed per world and can carry per-save fields —
/// is what <see cref="SchematicCatalog"/> was, and is how the unlock set came to live in it.
/// </para>
/// <para>
/// Shipped preset programs are absent, and deliberately: the program language belongs to the
/// programming work, and a field typed against records that do not exist yet would be a schema
/// nobody can fill. <c>catalog/programs.json</c> exists and is required to be empty until then, so
/// the file is in place before the field that reads it.
/// </para>
/// </summary>
public sealed record ContentCatalog(
    string ContentVersion,
    SchematicCatalog Schematics,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<StorageArchetype> Storages,
    IReadOnlyList<FacilityArchetype> Facilities,
    IReadOnlyList<TransportArchetype> Transports,
    IReadOnlyList<PowerSinkDefinition> Sinks,
    IReadOnlyList<ReactorArchetype> Reactors,
    IReadOnlyList<StratumDefinition> Strata)
{
    /// <summary>
    /// The pattern every catalog id matches. The colon in the authored tier's <c>user:</c> prefix
    /// is unrepresentable here, which is what makes the two id spaces provably disjoint rather
    /// than disjoint by convention: a save can never smuggle a redefinition of a shipped schematic
    /// past the catalog by minting an id that collides with one.
    /// </summary>
    public const string IdPattern = "^[a-z][a-z0-9_]*$";

    public FacilityArchetype? Facility(FacilityArchetypeId id)
    {
        foreach (var archetype in Facilities)
        {
            if (archetype.Id == id)
            {
                return archetype;
            }
        }

        return null;
    }

    public TransportArchetype? Transport(TransportArchetypeId id)
    {
        foreach (var archetype in Transports)
        {
            if (archetype.Id == id)
            {
                return archetype;
            }
        }

        return null;
    }

    public StorageArchetype? Storage(StorageArchetypeId id)
    {
        foreach (var archetype in Storages)
        {
            if (archetype.Id == id)
            {
                return archetype;
            }
        }

        return null;
    }

    public ReactorArchetype? Reactor(ReactorArchetypeId id)
    {
        foreach (var archetype in Reactors)
        {
            if (archetype.Id == id)
            {
                return archetype;
            }
        }

        return null;
    }

    public ItemDefinition? Item(ItemId id)
    {
        foreach (var item in Items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }
}
