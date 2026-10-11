using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

/// <summary>
/// <see cref="IntMath.CeilDiv"/>: the one rounding rule for how many whole ticks, runs or buckets
/// cover an amount. The engine and the planner each wrote it out by hand, ten times between them,
/// and a run must cost the same number of ticks to both.
/// </summary>
public class IntMathTests
{
    [TestCase(0, 100, 0)]
    [TestCase(1, 100, 1)]
    [TestCase(800, 100, 8)]
    [TestCase(801, 100, 9)]
    [TestCase(799, 100, 8)]
    [TestCase(1600, 75, 22)]
    public void CeilDiv_IsTheWholeCountThatCoversTheAmount(long amount, long per, long expected)
    {
        Assert.That(IntMath.CeilDiv(amount, per), Is.EqualTo(expected));
    }
}
