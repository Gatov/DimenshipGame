using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning.Draft;

/// <summary>Stable id of one draft step. Minted once per <see cref="RequirementKey"/>.</summary>
public readonly record struct DraftStepId(long Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>Where a step sits in the requirement graph — identity, not display order.</summary>
public enum DraftRole
{
    Goal,
    Assembly,
    Output,
    Input,
    Delivery,
    Manual,
}

public enum DraftOrigin
{
    Automatic,
    Manual,
}

public enum DraftField
{
    Quantity,
    Executor,
}

/// <summary>
/// The identity of a requirement. Locks and step ids follow this key so a re-plan cannot slide a
/// pin onto a neighbour. See <c>2026-09-16-editable-production-plans-design.md</c> Decision 2.
/// </summary>
public sealed record RequirementKey(DraftStepId? Parent, DraftRole Role, int Ordinal, ItemId Item);

/// <summary>
/// Draft work is its own hierarchy, not <see cref="TaskAction"/>: <see cref="DraftAssemble"/> must
/// be incapable of reaching <c>Enqueue</c>.
/// </summary>
public abstract record DraftWork;

public sealed record DraftProduce(SchematicId Schematic, long Runs) : DraftWork;

public sealed record DraftMove(ItemId Item, long Quantity, StorageId From, StorageId To) : DraftWork;

public sealed record DraftAssemble(ItemId Unit, ExecutorId Target) : DraftWork;

public sealed record DraftStep(
    DraftStepId Id,
    RequirementKey Key,
    DraftWork Work,
    ExecutorId? Executor,
    long AvailableAtSource,
    bool QuantityLocked,
    bool ExecutorLocked,
    DraftOrigin Origin,
    bool Replanned);

/// <summary>
/// Why a draft step or the goal cannot stand as proposed. Structural kinds make
/// <see cref="PlanDraft.IsCommittable"/> false; supply kinds do not. Which is which is
/// <see cref="DraftIssueKinds.IsStructural"/>.
/// </summary>
public enum DraftIssueKind
{
    IncompatibleExecutor,
    NoSuchRoute,
    UnknownEndpoint,
    NonPositiveQuantity,
    UnbuiltExecutor,
    NotCommandable,
    /// <summary>
    /// A raw material that nothing aboard produces is short in the hold. The work that consumes it
    /// is planned anyway, and waits on its input until the player brings the material in. A supply
    /// kind: it informs, it never blocks approval, and it is not an <c>Unplannable</c> entry.
    /// </summary>
    MaterialShortage,
    GoalShortfall,
    LockedSchematic,
    NoExecutorOrLine,
    CyclicSchematic,

    /// <summary>
    /// A move would put a workpiece down where it is never accepted, or the goal would leave one
    /// in the hold (K3, D2 Decision 4). Structural: approval refuses the draft, because a supply
    /// kind would let it commit and then fail at <c>Enqueue</c>. Appended last, as D2 specifies, so
    /// no existing kind changes value.
    /// </summary>
    WorkpieceNotAccepted,
}

/// <summary>
/// Which <see cref="DraftIssueKind"/>s block approval. One place, read by
/// <see cref="PlanDraft.IsCommittable"/>, by the editor when it merges a repeated issue (a
/// structural one replaces, a supply one accumulates) and by the inspector's build quote. The
/// list was once written out twice, in <c>IsCommittable</c> and in the editor, and the quote built
/// a single-issue draft to ask the first copy. A kind added to one copy and not the other would
/// have either summed its quantities across edits or slipped past approval.
/// </summary>
public static class DraftIssueKinds
{
    /// <summary>
    /// True when the draft cannot commit while the issue stands; false for a supply kind, which
    /// informs and commits. Every kind is listed, and an unlisted one throws, so a kind appended to
    /// the enum has to be decided here before the first draft that reports it.
    /// </summary>
    public static bool IsStructural(this DraftIssueKind kind) => kind switch
    {
        DraftIssueKind.IncompatibleExecutor => true,
        DraftIssueKind.NoSuchRoute => true,
        DraftIssueKind.UnknownEndpoint => true,
        DraftIssueKind.NonPositiveQuantity => true,
        DraftIssueKind.UnbuiltExecutor => true,
        DraftIssueKind.NotCommandable => true,
        DraftIssueKind.WorkpieceNotAccepted => true,
        DraftIssueKind.MaterialShortage => false,
        DraftIssueKind.GoalShortfall => false,
        DraftIssueKind.LockedSchematic => false,
        DraftIssueKind.NoExecutorOrLine => false,
        DraftIssueKind.CyclicSchematic => false,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unclassified draft issue kind."),
    };
}

public sealed record DraftIssue(
    DraftIssueKind Kind,
    ItemId Item,
    long Quantity,
    DraftStepId? Step);

/// <summary>
/// An immutable proposal that retains the requirement graph until approval. Not one of the four
/// data tiers: held by the shell across frames, never saved, never on the snapshot.
/// </summary>
public sealed record PlanDraft(
    ItemAmount Goal,
    StorageId? Destination,
    ExecutorId? AssemblyTarget,
    IReadOnlyList<DraftStep> Steps,
    IReadOnlyList<DraftIssue> Issues,
    long Covered,
    long EstimatedTicks)
{
    public bool IsComplete => Covered >= Goal.Quantity;

    public bool IsCommittable => Issues.All(i => !i.Kind.IsStructural());

    /// <summary>
    /// Merges per-requirement moves into the flat task list <see cref="ProductionPlanner"/> used
    /// to emit directly. Legs sharing (item, from, to, executor) collapse in first-appearance
    /// order; delivery steps stay unmerged and append last.
    /// </summary>
    public ProductionPlan Flatten()
    {
        var transfers = new List<PlannedTask>();
        var transferIndex = new Dictionary<(ItemId Item, StorageId From, StorageId To, ExecutorId Line), int>();
        var runs = new List<PlannedTask>();

        foreach (var step in Steps)
        {
            switch (step.Work)
            {
                case DraftMove move when step.Key.Role != DraftRole.Delivery:
                    if (step.Executor is not { } line || move.Quantity <= 0 || move.From == move.To)
                    {
                        break;
                    }

                    var key = (move.Item, move.From, move.To, line);
                    if (transferIndex.TryGetValue(key, out var index))
                    {
                        var existing = transfers[index];
                        var transfer = (Transfer)existing.Script.Action;
                        transfers[index] = existing with
                        {
                            Script = existing.Script with
                            {
                                Action = transfer with
                                {
                                    Quantity = transfer.Quantity!.Value + move.Quantity,
                                },
                            },
                            AvailableAtSource = existing.AvailableAtSource + step.AvailableAtSource,
                        };
                    }
                    else
                    {
                        transferIndex[key] = transfers.Count;
                        transfers.Add(new PlannedTask(
                            new TaskScript(
                                Array.Empty<Programs.Condition>(),
                                new Transfer(move.Item, move.Quantity, move.From, move.To)),
                            line,
                            step.AvailableAtSource));
                    }

                    break;

                case DraftProduce produce when step.Executor is { } facility:
                    runs.Add(new PlannedTask(
                        new TaskScript(
                            Array.Empty<Programs.Condition>(),
                            new Produce(produce.Schematic, (int)produce.Runs)),
                        facility,
                        step.AvailableAtSource));
                    break;
            }
        }

        PlannedTask? finalLeg = null;
        foreach (var step in Steps)
        {
            if (step.Work is DraftMove move
                && step.Key.Role == DraftRole.Delivery
                && step.Executor is { } line
                && move.Quantity > 0
                && move.From != move.To)
            {
                finalLeg = new PlannedTask(
                    new TaskScript(
                        Array.Empty<Programs.Condition>(),
                        new Transfer(move.Item, move.Quantity, move.From, move.To)),
                    line,
                    step.AvailableAtSource);
                break;
            }
        }

        var tasks = new List<PlannedTask>(transfers.Count + runs.Count + (finalLeg is null ? 0 : 1));
        tasks.AddRange(transfers);
        tasks.AddRange(runs);
        if (finalLeg is not null)
        {
            tasks.Add(finalLeg);
        }

        return new ProductionPlan(Goal, Destination, tasks, ToUnplannable(Issues), EstimatedTicks);
    }

    private static IReadOnlyList<Unplannable> ToUnplannable(IReadOnlyList<DraftIssue> issues)
    {
        var list = new List<Unplannable>();
        foreach (var issue in issues)
        {
            var reason = issue.Kind switch
            {
                DraftIssueKind.LockedSchematic => UnplannableReason.LockedSchematic,
                DraftIssueKind.NoExecutorOrLine => UnplannableReason.NoExecutorOrLine,
                DraftIssueKind.CyclicSchematic => UnplannableReason.CyclicSchematic,
                _ => (UnplannableReason?)null,
            };
            if (reason is { } r)
            {
                list.Add(new Unplannable(issue.Item, issue.Quantity, r));
            }
        }

        return list;
    }
}
