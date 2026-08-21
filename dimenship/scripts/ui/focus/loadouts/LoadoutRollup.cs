using System.Collections.Generic;
using System.Linq;

namespace Dimenship.Ui;

/// <summary>One contributor to the totals: the frame's baseline, or one fitted part.</summary>
/// <param name="Source">What contributed — a frame label, or a part label with its socket.</param>
/// <param name="SocketIndex">Null for the frame's own baseline row.</param>
public sealed record Contribution(string Source, int? SocketIndex, StatBlock Stats);

/// <summary>
/// What the composer reads out: the totals, who contributed what to them, the summed cost, and
/// where the template stands.
/// </summary>
public sealed record Rollup(
    FrameDef Frame,
    StatBlock Totals,
    IReadOnlyList<Contribution> Contributions,
    IReadOnlyList<ItemCost> Cost,
    Verdict Verdict,
    int EmptySockets);

/// <summary>
/// The composer's arithmetic: sum the frame's baseline and every fitted part, keep who contributed
/// what, add up the bill, and say where the template stands.
/// <para>
/// Attribution is kept rather than discarded because the question the composer exists to make
/// answerable is <i>which part is costing me this</i>. A single total column makes the player
/// derive that by pulling parts out one at a time, which is the interface this mock is trying to
/// avoid shipping.
/// </para>
/// <para>
/// This could have lived in <c>Dimenship.Shell</c>, where it would be unit-tested. It does not,
/// because <see cref="LoadoutCatalog"/> would have to travel with it and would then be shipped,
/// tested content describing a subsystem that does not exist — a stronger claim than a mock is
/// entitled to make.
/// </para>
/// </summary>
public static class LoadoutRollup
{
    public static Rollup Of(LoadoutDraft draft)
    {
        var frame = LoadoutCatalog.Frame(draft.FrameId);
        var contributions = new List<Contribution> { new(frame.Label, null, frame.Baseline) };
        var totals = frame.Baseline;
        var cost = new List<ItemCost>(frame.Cost);
        var empty = 0;

        for (var i = 0; i < frame.Sockets.Count; i++)
        {
            var part = i < draft.Fitted.Count ? LoadoutCatalog.Part(draft.Fitted[i]) : null;

            if (part is null)
            {
                empty++;
                continue;
            }

            contributions.Add(new Contribution(part.Label, i, part.Delta));
            totals += part.Delta;
            cost.AddRange(part.Cost);
        }

        return new Rollup(frame, totals, contributions, Sum(cost), Judge(totals, empty), empty);
    }

    /// <summary>
    /// Over-budget outranks incomplete. Both keep a template from being built, but an empty socket
    /// is a step not yet taken and a negative budget is a choice already made wrongly — reporting
    /// the missing part first would let a player fill it and then discover the real problem.
    /// </summary>
    private static Verdict Judge(StatBlock totals, int empty) =>
        totals.Power < 0 ? Verdict.OverBudget
        : empty > 0 ? Verdict.Incomplete
        : Verdict.Complete;

    /// <summary>
    /// Merges the lines naming the same item, in first-appearance order. Sorting them would put
    /// the frame's own cost somewhere other than the top of its own bill, and ordering a readout
    /// by id teaches the player nothing about the thing they are building.
    /// </summary>
    private static IReadOnlyList<ItemCost> Sum(IEnumerable<ItemCost> lines)
    {
        var totals = new Dictionary<string, long>();
        var order = new List<string>();

        foreach (var line in lines)
        {
            if (!totals.ContainsKey(line.ItemId))
            {
                order.Add(line.ItemId);
                totals[line.ItemId] = 0;
            }

            totals[line.ItemId] += line.Amount;
        }

        return order.Select(id => new ItemCost(id, totals[id])).ToList();
    }
}
