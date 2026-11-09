using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Simulation;

public class UnitsTests
{
    [TestCase(0L, "0.000")]
    [TestCase(1L, "0.001")]
    [TestCase(2_400L, "2.400")]
    [TestCase(1_284_000L, "1284.000")]
    [TestCase(-500L, "-0.500")]
    [TestCase(-2_400L, "-2.400")]
    [TestCase(-1L, "-0.001")]
    [TestCase(long.MaxValue, "9223372036854775.807")]
    [TestCase(long.MinValue, "-9223372036854775.808")]
    public void Format_RendersMilliUnitsWithThreeDecimals(long milli, string expected)
    {
        Assert.That(Units.Format(milli), Is.EqualTo(expected));
    }

    [TestCase(0L, "0")]
    [TestCase(1L, "0.001")]
    [TestCase(2_400L, "2.4")]
    [TestCase(2_450L, "2.45")]
    [TestCase(1_284_000L, "1284")]
    [TestCase(10_000L, "10")]
    [TestCase(-500L, "-0.5")]
    [TestCase(-3_000L, "-3")]
    public void FormatCompact_DropsOnlyTrailingZeros(long milli, string expected)
    {
        Assert.That(Units.FormatCompact(milli), Is.EqualTo(expected));
    }

    [TestCase(0L, "T+00:00:00")]
    [TestCase(59L, "T+00:00:59")]
    [TestCase(60L, "T+00:01:00")]
    [TestCase(15_160L, "T+04:12:40")]
    [TestCase(360_000L, "T+100:00:00")]
    public void FormatSimTime_RendersTicksAsElapsedClock(long ticks, string expected)
    {
        Assert.That(Units.FormatSimTime(ticks), Is.EqualTo(expected));
    }
}
