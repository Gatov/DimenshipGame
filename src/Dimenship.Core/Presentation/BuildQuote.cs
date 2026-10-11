using Dimenship.Core.Planning.Draft;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.Presentation;

/// <summary>
/// What a construction will take, read off the draft the composer would commit, projected and
/// thrown away (U4; <c>2026-10-11-playable-inspector-and-move-control-design.md</c>). The inspector
/// builds from it without a trip to Operations, and reading the composer's own draft is what keeps
/// the two from quoting one build two ways.
/// <para>
/// Cost is what leaves the hold, not the unit schematic's inputs. A unit whose inputs must first be
/// made costs their raw materials, and those are what the player is short of. A shortage is listed
/// apart from what blocks: <see cref="DraftIssueKind.MaterialShortage"/> never stops approval, and
/// the work waits on its input, so the inspector says "will wait for", not "cannot".
/// </para>
/// <para>
/// <see cref="Warnings"/> are every other issue that does not block approval but means the build
/// may not finish, such as no line from the factory to the slot. Hiding them would put a clean BUILD
/// over a plan that can never deliver.
/// </para>
/// </summary>
public sealed record BuildQuote(
    ExecutorId Slot,
    ItemId Unit,
    ExecutorId? MadeAt,
    IReadOnlyList<ItemAmount> FromHold,
    IReadOnlyList<ItemAmount> Short,
    IReadOnlyList<DraftIssue> Warnings,
    IReadOnlyList<DraftIssue> Blocking,
    long EstimatedTicks)
{
    /// <summary>Whether approval would accept the draft: nothing structural is wrong with it.</summary>
    public bool Committable => Blocking.Count == 0;

    public static BuildQuote For(PlanDraft draft, SchematicCatalog schematics, StorageId hold)
    {
        if (draft.AssemblyTarget is not { } slot)
        {
            throw new ArgumentException(
                "A build quote reads a construction draft, and this draft assembles nothing.", nameof(draft));
        }

        var unit = draft.Goal.Item;
        ExecutorId? madeAt = null;
        var fromHold = new List<ItemAmount>();
        foreach (var step in draft.Steps)
        {
            switch (step.Work)
            {
                case DraftProduce produce when madeAt is null
                    && schematics.TryGet(produce.Schematic, out var schematic) && schematic.Output.Item == unit:
                    madeAt = step.Executor;
                    break;
                // The unit itself passes through the hold on its way to the slot. That leg is the
                // product travelling, not a cost, and quoting it would charge the build for itself.
                case DraftMove move when move.From == hold && move.Item != unit:
                    Merge(fromHold, move.Item, move.Quantity);
                    break;
            }
        }

        var shortages = new List<ItemAmount>();
        var warnings = new List<DraftIssue>();
        var blocking = new List<DraftIssue>();
        foreach (var issue in draft.Issues)
        {
            // One issue at a time through IsCommittable, so which kinds block stays decided in one
            // place, the draft, rather than copied here where it would drift.
            if (!(draft with { Issues = new[] { issue } }).IsCommittable)
            {
                blocking.Add(issue);
            }
            else if (issue.Kind == DraftIssueKind.MaterialShortage)
            {
                Merge(shortages, issue.Item, issue.Quantity);
            }
            else
            {
                warnings.Add(issue);
            }
        }

        return new BuildQuote(slot, unit, madeAt, fromHold, shortages, warnings, blocking, draft.EstimatedTicks);
    }

    private static void Merge(List<ItemAmount> amounts, ItemId item, long quantity)
    {
        var index = amounts.FindIndex(a => a.Item == item);
        if (index < 0)
        {
            amounts.Add(new ItemAmount(item, quantity));
        }
        else
        {
            amounts[index] = amounts[index] with { Quantity = amounts[index].Quantity + quantity };
        }
    }
}
