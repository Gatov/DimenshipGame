using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;

namespace Dimenship.Ui;

/// <summary>One choice in a slot's dropdown: the value stored, and the word the player reads.</summary>
public sealed record VocabularyOption(string Id, string Label);

/// <summary>
/// One editable value in a block: what it offers, the words either side of it, and — for a number —
/// the range it clamps to. Bounds live on the slot rather than on the value, so a slot cannot be
/// left outside the range the vocabulary declares for it.
/// </summary>
public sealed record SlotSpec(
    SlotKind Kind,
    string Lead = "",
    string Trail = "",
    long Min = 0,
    long Max = 1_000_000,
    long Default = 0);

/// <summary>
/// One entry in the block vocabulary: the keyword cap the block wears, the words the palette lists
/// it under, and the slots it declares.
/// </summary>
public sealed record BlockSpec(
    string Keyword,
    string Summary,
    string Domain,
    IReadOnlyList<SlotSpec> Slots);

/// <summary>
/// The closed condition and action vocabulary, and the targets each slot may name.
/// <para>
/// The targets are read from <see cref="WorldDefinition.CreateDefault"/>, so every dropdown offers
/// exactly what the vessel really contains — two items, two storages, four executors, two
/// schematics — and nothing the concept art invented. A program that could name a refinery the
/// vessel does not have would be a vocabulary of words that command nothing, which is the one thing
/// the design spec is most insistent about.
/// </para>
/// <para>
/// Reading it from a second constructed world is the mock's shortcut. The real view takes the
/// definition through <see cref="ShellContext"/>; this call is cheap, read-only, and happens once.
/// </para>
/// </summary>
public static class ProgramVocabulary
{
    private static readonly WorldDefinition World = WorldDefinition.CreateDefault();

    public const string DomainAll = "All";
    public const string DomainStorage = "Storage";
    public const string DomainFacility = "Facility";
    public const string DomainVessel = "Vessel";
    public const string DomainSystem = "System";

    public static readonly IReadOnlyList<string> Domains =
        new[] { DomainAll, DomainStorage, DomainFacility, DomainVessel, DomainSystem };

    public static readonly IReadOnlyList<VocabularyOption> Items =
        World.Items.Select(item => new VocabularyOption(item.Id.Value, item.Label)).ToList();

    public static readonly IReadOnlyList<VocabularyOption> Storages =
        World.Storages.Select(store => new VocabularyOption(store.Id.Value, store.Label)).ToList();

    /// <summary>
    /// Producers and transport lines in one list. A program holds, prioritises and queues work on
    /// both, and the player does not think of a feed line as a different kind of thing to hold.
    /// </summary>
    public static readonly IReadOnlyList<VocabularyOption> Executors =
        World.Producers.Select(one => new VocabularyOption(one.Id.Value, one.Label))
            .Concat(World.Transports.Select(one => new VocabularyOption(one.Id.Value, one.Label)))
            .ToList();

    public static readonly IReadOnlyList<VocabularyOption> Schematics =
        World.Schematics.All.Select(one => new VocabularyOption(one.Id.Value, Title(one.Id.Value)))
            .ToList();

    public static readonly IReadOnlyList<VocabularyOption> Comparisons = new[]
    {
        new VocabularyOption(nameof(Comparison.LessThan), "Less Than"),
        new VocabularyOption(nameof(Comparison.LessOrEqual), "At Most"),
        new VocabularyOption(nameof(Comparison.Equal), "Equals"),
        new VocabularyOption(nameof(Comparison.NotEqual), "Not Equals"),
        new VocabularyOption(nameof(Comparison.GreaterOrEqual), "At Least"),
        new VocabularyOption(nameof(Comparison.GreaterThan), "Greater Than"),
    };

    public static readonly IReadOnlyList<VocabularyOption> Statuses = new[]
    {
        new VocabularyOption(nameof(ExecutorStatus.NoTasksQueued), "Idle"),
        new VocabularyOption(nameof(ExecutorStatus.RunningTask), "Running"),
        new VocabularyOption(nameof(ExecutorStatus.SwitchingOver), "Switching Over"),
        new VocabularyOption(nameof(ExecutorStatus.AllQueuedTasksBlocked), "Blocked"),
    };

    public static readonly IReadOnlyList<VocabularyOption> Trends = new[]
    {
        new VocabularyOption("rising", "Rising"),
        new VocabularyOption("steady", "Steady"),
        new VocabularyOption("falling", "Falling"),
    };

    public static readonly IReadOnlyList<VocabularyOption> Toggles = new[]
    {
        new VocabularyOption("on", "On"),
        new VocabularyOption("off", "Off"),
    };

    public static readonly IReadOnlyList<ConditionKind> ConditionKinds =
        Enum.GetValues<ConditionKind>();

    public static readonly IReadOnlyList<ActionKind> ActionKinds = Enum.GetValues<ActionKind>();

    /// <summary>Every option a slot of this kind offers. Empty for a number, which has no list.</summary>
    public static IReadOnlyList<VocabularyOption> OptionsFor(SlotKind kind) => kind switch
    {
        SlotKind.Item => Items,
        SlotKind.Storage => Storages,
        SlotKind.Executor => Executors,
        SlotKind.Schematic => Schematics,
        SlotKind.Comparison => Comparisons,
        SlotKind.ExecutorStatus => Statuses,
        SlotKind.Trend => Trends,
        SlotKind.Toggle => Toggles,
        _ => Array.Empty<VocabularyOption>(),
    };

    public static BlockSpec Spec(ConditionKind kind) => kind switch
    {
        ConditionKind.StorageItemAmount => new BlockSpec(
            "STORAGE", "Storage : Item Amount", DomainStorage, new[]
            {
                new SlotSpec(SlotKind.Storage),
                new SlotSpec(SlotKind.Item, Lead: "holds"),
                new SlotSpec(SlotKind.Comparison),
                new SlotSpec(SlotKind.Number, Trail: "units", Max: 100_000_000, Default: 50_000),
            }),

        ConditionKind.StorageFillPercent => new BlockSpec(
            "STORAGE", "Storage : Fill Percent", DomainStorage, new[]
            {
                new SlotSpec(SlotKind.Storage),
                new SlotSpec(SlotKind.Comparison, Lead: "is"),
                new SlotSpec(SlotKind.Number, Trail: "% full", Max: 100, Default: 90),
            }),

        ConditionKind.VesselItemAmount => new BlockSpec(
            "VESSEL", "Vessel : Item Amount", DomainVessel, new[]
            {
                new SlotSpec(SlotKind.Item),
                new SlotSpec(SlotKind.Comparison, Lead: "amount is"),
                new SlotSpec(SlotKind.Number, Trail: "units", Max: 100_000_000, Default: 50_000),
            }),

        ConditionKind.VesselItemTrend => new BlockSpec(
            "VESSEL", "Vessel : Item Trend", DomainVessel, new[]
            {
                new SlotSpec(SlotKind.Item),
                new SlotSpec(SlotKind.Trend, Lead: "is"),
            }),

        ConditionKind.ExecutorStatusIs => new BlockSpec(
            "FACILITY", "Facility : Status", DomainFacility, new[]
            {
                new SlotSpec(SlotKind.Executor),
                new SlotSpec(SlotKind.ExecutorStatus, Lead: "status is"),
            }),

        ConditionKind.ExecutorQueueLength => new BlockSpec(
            "FACILITY", "Facility : Queue Length", DomainFacility, new[]
            {
                new SlotSpec(SlotKind.Executor),
                new SlotSpec(SlotKind.Comparison, Lead: "queue is"),
                new SlotSpec(SlotKind.Number, Trail: "tasks", Max: 1_000, Default: 1),
            }),

        ConditionKind.EnergyReservePercent => new BlockSpec(
            "ENERGY", "System : Energy Reserve", DomainSystem, new[]
            {
                new SlotSpec(SlotKind.Comparison, Lead: "reserve is"),
                new SlotSpec(SlotKind.Number, Trail: "%", Max: 100, Default: 20),
            }),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown condition kind."),
    };

    public static BlockSpec Spec(ActionKind kind) => kind switch
    {
        ActionKind.Produce => new BlockSpec(
            "PRODUCE", "Produce : Queue Runs", DomainFacility, new[]
            {
                new SlotSpec(SlotKind.Schematic),
                new SlotSpec(SlotKind.Executor, Lead: "on"),
                new SlotSpec(SlotKind.Number, Trail: "runs", Min: 1, Max: 100_000, Default: 10),
            }),

        ActionKind.Transfer => new BlockSpec(
            "TRANSFER", "Transfer : Move Items", DomainStorage, new[]
            {
                new SlotSpec(SlotKind.Number, Max: 100_000_000, Default: 10_000),
                new SlotSpec(SlotKind.Item, Lead: "units of"),
                new SlotSpec(SlotKind.Storage, Lead: "from"),
                new SlotSpec(SlotKind.Storage, Lead: "to"),
            }),

        ActionKind.SetPriority => new BlockSpec(
            "SET PRIORITY", "Facility : Set Priority", DomainFacility, new[]
            {
                new SlotSpec(SlotKind.Executor),
                new SlotSpec(SlotKind.Number, Lead: "to", Max: 100, Default: 50),
            }),

        ActionKind.Hold => new BlockSpec(
            "HOLD", "Facility : Safety Hold", DomainFacility, new[]
            {
                new SlotSpec(SlotKind.Executor),
                new SlotSpec(SlotKind.Toggle),
            }),

        ActionKind.Reserve => new BlockSpec(
            "RESERVE", "Storage : Reserve Material", DomainStorage, new[]
            {
                new SlotSpec(SlotKind.Number, Max: 100_000_000, Default: 25_000),
                new SlotSpec(SlotKind.Item, Lead: "units of"),
                new SlotSpec(SlotKind.Storage, Lead: "in"),
            }),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown action kind."),
    };

    /// <summary>A condition with every slot at its declared default. Never a half-filled block.</summary>
    public static ConditionDraft NewCondition(ConditionKind kind) =>
        new(kind, Defaults(Spec(kind)).ToArray());

    /// <summary>A command with every slot at its declared default.</summary>
    public static CommandDraft NewCommand(ActionKind kind) =>
        new(kind, Defaults(Spec(kind)).ToArray());

    /// <summary>
    /// The word a stored slot value reads as. An id the world no longer contains is rendered as the
    /// id in brackets rather than silently replaced: a program naming a facility that has gone is
    /// exactly the thing the player has to be able to see.
    /// </summary>
    public static string Describe(SlotKind kind, string id)
    {
        var option = OptionsFor(kind).FirstOrDefault(one => one.Id == id);
        return option?.Label ?? $"[{id}]";
    }

    /// <summary>The palette's second dropdown, matched against <see cref="BlockSpec.Domain"/>.</summary>
    public static bool InDomain(BlockSpec spec, string domain) =>
        domain == DomainAll || spec.Domain == domain;

    private static IEnumerable<SlotValue> Defaults(BlockSpec spec) =>
        spec.Slots.Select(slot => slot.Kind == SlotKind.Number
            ? new SlotValue(slot.Default)
            : new SlotValue(OptionsFor(slot.Kind).FirstOrDefault()?.Id ?? string.Empty));

    /// <summary>
    /// <c>smelt_alloy</c> reads as <c>Smelt Alloy</c>. Schematics carry no label of their own in the
    /// world definition, and a raw identifier in a dropdown beside two proper names looks like a
    /// bug rather than a naming convention.
    /// </summary>
    private static string Title(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
