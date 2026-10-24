using Dimenship.Core.Content;
using Dimenship.Core.Presentation;
using Dimenship.Core.Simulation;

namespace Dimenship.ShellModel;

/// <summary>
/// One slot the composer's Build mode can order: the facility, its label, the order that builds
/// it (one whole construction unit into the slot's own buffer, as the inspector's BUILD asks), and
/// the two archetype readings the preview shows beside it. <see cref="Destination"/> is that
/// order's buffer.
/// </summary>
public readonly record struct BuildTarget(
    ExecutorId Facility,
    string Label,
    ConstructionOrder Order,
    string Purpose,
    long StandingPowerDraw)
{
    public StorageId Destination => Order.Destination;
}

/// <summary>One item the composer's Produce mode can order, with the label its picker shows.</summary>
public readonly record struct ProduceTarget(ItemId Item, string Label);

/// <summary>
/// The composer's two target lists, resolved once from content, because content does not change
/// mid-game. Which build targets are still unbuilt is state, and <see cref="ComposerModel"/> reads
/// it per snapshot rather than here.
/// </summary>
public static class ComposerTargets
{
    /// <summary>
    /// Every scenario facility whose archetype names a construction unit, in scenario declaration
    /// order. Reactor Alpha and Factory Alpha are among them, because the unit belongs to the
    /// archetype and not the slot; they never show, because they open built.
    /// </summary>
    public static IReadOnlyList<BuildTarget> Build(ContentCatalog catalog, Scenario scenario)
    {
        var targets = new List<BuildTarget>();
        foreach (var facility in scenario.Facilities)
        {
            var archetype = catalog.Facility(facility.Archetype);
            if (archetype is null || ConstructionOrder.For(archetype, facility.Id, facility.LocalStorage) is not { } order)
            {
                continue;
            }

            targets.Add(new BuildTarget(
                facility.Id, facility.NameOverride ?? archetype.Label, order,
                archetype.Purpose, archetype.StandingPowerDraw));
        }

        return targets;
    }

    /// <summary>
    /// Every item an unlocked schematic produces, in unlock declaration order and de-duplicated by
    /// output, which is the order the planner itself prefers a producer in. Reading the scenario's
    /// unlocked set rather than every schematic in the catalog is what keeps the list to what the
    /// player can plan today. <paramref name="label"/> is the shell's item wording, passed in
    /// because it lives with the rest of the shell's words.
    /// </summary>
    public static IReadOnlyList<ProduceTarget> Produce(
        ContentCatalog catalog, Scenario scenario, Func<ItemId, string> label)
    {
        var seen = new HashSet<ItemId>();
        var targets = new List<ProduceTarget>();
        foreach (var schematicId in scenario.UnlockedSchematics)
        {
            if (!catalog.Schematics.TryGet(schematicId, out var schematic) || !seen.Add(schematic.Output.Item))
            {
                continue;
            }

            targets.Add(new ProduceTarget(schematic.Output.Item, label(schematic.Output.Item)));
        }

        return targets;
    }
}
