using Dimenship.Core.Simulation;

namespace Dimenship.Core.Planning;

/// <summary>
/// Which built line carries a leg. One rule, read by the planner when it drafts a leg and by
/// <c>SimulationEngine.Move</c> when it re-routes committed work, so a moved leg goes where a fresh
/// order would have sent it. The rule was once written out in both places, and the next tuning of
/// it (K5b already moved throughput ahead of declaration order once) would have reached only one.
/// </summary>
public static class LineChoice
{
    /// <summary>
    /// The line on <paramref name="from"/> → <paramref name="to"/> with the least load, then the
    /// highest throughput, then the first declared. Load is a line's queued transfers plus what the
    /// caller has already placed on it (<paramref name="placed"/>), so legs one draft or one move
    /// emits spread across parallel lines. Throughput sits ahead of declaration order because two
    /// lines feed Factory Beta's buffer at 7 and 50 a tick, and declaration order alone sent a whole
    /// construction unit down the slow one. Null when no line runs the route.
    /// </summary>
    public static PlannerTransport? Best(
        IEnumerable<PlannerTransport> lines,
        StorageId from,
        StorageId to,
        IReadOnlyDictionary<ExecutorId, long> placed)
    {
        PlannerTransport? best = null;
        var bestLoad = long.MaxValue;
        foreach (var line in lines)
        {
            if (line.From != from || line.To != to)
            {
                continue;
            }

            var load = line.QueuedTransfers + placed.GetValueOrDefault(line.Id);
            if (best is null || load < bestLoad || (load == bestLoad && line.ThroughputPerTick > best.ThroughputPerTick))
            {
                best = line;
                bestLoad = load;
            }
        }

        return best;
    }

    /// <summary>Whether <paramref name="line"/> is a built line that runs <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool Runs(IEnumerable<PlannerTransport> lines, ExecutorId line, StorageId from, StorageId to) =>
        lines.Any(l => l.Id == line && l.From == from && l.To == to);
}
