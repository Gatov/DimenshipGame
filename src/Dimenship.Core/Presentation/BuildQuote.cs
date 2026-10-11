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
        var outOfHold = new List<ItemAmount>();
        var intoHold = new List<ItemAmount>();
        foreach (var step in draft.Steps)
        {
            switch (step.Work)
            {
                case DraftProduce produce when madeAt is null
                    && schematics.TryGet(produce.Schematic, out var schematic) && schematic.Output.Item == unit:
                    madeAt = step.Executor;
                    break;
                case DraftMove move when move.From == hold:
                    Merge(outOfHold, move.Item, move.Quantity);
                    break;
                case DraftMove move when move.To == hold:
                    Merge(intoHold, move.Item, move.Quantity);
                    break;
            }
        }

        // An ordinary item made in the plan travels producer, hold, consumer, so its leg out of the
        // hold is the plan moving its own output. Netting each item's moves out against the plan's
        // moves in leaves what the hold actually gives up: raw material, and a unit taken from
        // stock rather than made. Counting every leg out charged an in-plan intermediate on top of
        // the raw material it is made from, and the unit for itself.
        var fromHold = new List<ItemAmount>();
        foreach (var taken in outOfHold)
        {
            var net = taken.Quantity - intoHold.Where(i => i.Item == taken.Item).Sum(i => i.Quantity);
            if (net > 0)
            {
                fromHold.Add(taken with { Quantity = net });
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
