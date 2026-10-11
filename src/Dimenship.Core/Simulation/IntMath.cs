namespace Dimenship.Core.Simulation;

/// <summary>
/// Integer arithmetic the kernel needs in more than one place. The kernel has no floating point, so
/// a rounding rule is code, and code written out by hand at each use drifts.
/// </summary>
public static class IntMath
{
    /// <summary>
    /// How many whole units of <paramref name="per"/> cover <paramref name="amount"/>: the ticks a
    /// run of some effort takes at a work rate, the ticks a haul takes at a throughput, the runs
    /// that make a deficit. It was written out ten times between the engine and the planner, and a
    /// run must cost the same number of ticks to the planner that estimates it as to the engine
    /// that runs it (#65 item 3). <paramref name="amount"/> is not negative and
    /// <paramref name="per"/> is positive at every call site; callers that cannot promise a
    /// positive rate guard it themselves.
    /// </summary>
    public static long CeilDiv(long amount, long per) => (amount + per - 1) / per;
}
