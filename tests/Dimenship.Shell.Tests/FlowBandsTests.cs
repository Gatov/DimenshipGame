using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class FlowBandsTests
{
    [TestCase(0, ExpectedResult = FlowBand.Idle, TestName = "Nothing moved is idle")]
    [TestCase(1, ExpectedResult = FlowBand.Low, TestName = "One permille is already low, not idle")]
    [TestCase(329, ExpectedResult = FlowBand.Low, TestName = "Low ends at 329 permille")]
    [TestCase(330, ExpectedResult = FlowBand.Normal, TestName = "Normal begins at 330 permille")]
    [TestCase(799, ExpectedResult = FlowBand.Normal, TestName = "Normal ends at 799 permille")]
    [TestCase(800, ExpectedResult = FlowBand.High, TestName = "High begins at 800 permille")]
    [TestCase(1000, ExpectedResult = FlowBand.High, TestName = "A saturated line is high")]
    public FlowBand Classify_BandsByPermilleOfThroughput(long moved) =>
        FlowBands.Classify(moved, throughputPerTick: 1000, blocked: false);

    [Test]
    public void APartialPermille_FloorsRatherThanRounding()
    {
        // 3 of 10 is 300 permille, one band below the 330 boundary. Rounding up would report a
        // line as busier than it is, which is the direction that misleads.
        Assert.That(FlowBands.Classify(3, 10, blocked: false), Is.EqualTo(FlowBand.Low));
    }

    [Test]
    public void Blocked_OverridesEvenASaturatedReading()
    {
        Assert.That(
            FlowBands.Classify(1000, 1000, blocked: true), Is.EqualTo(FlowBand.Blocked),
            "a line that moved its full throughput and then blocked is blocked; the load is stale");
    }

    [Test]
    public void Blocked_OverridesAnIdleReading()
    {
        Assert.That(FlowBands.Classify(0, 1000, blocked: true), Is.EqualTo(FlowBand.Blocked));
    }

    [TestCase(0L, TestName = "Zero throughput")]
    [TestCase(-1L, TestName = "Negative throughput")]
    public void ALineThatCarriesNothing_IsIdleAndNeverDivides(long throughput)
    {
        Assert.That(FlowBands.Classify(500, throughput, blocked: false), Is.EqualTo(FlowBand.Idle));
    }

    [Test]
    public void MovingMoreThanThroughput_ClampsToHighRatherThanFallingOffTheEnd()
    {
        Assert.That(FlowBands.Classify(5000, 1000, blocked: false), Is.EqualTo(FlowBand.High));
    }

    [Test]
    public void ANegativeReading_IsIdle()
    {
        // Not reachable from the engine, but a band is a display value and a negative permille
        // must not fall through every comparison to land on High.
        Assert.That(FlowBands.Classify(-10, 1000, blocked: false), Is.EqualTo(FlowBand.Idle));
    }
}
