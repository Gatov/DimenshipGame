using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class ScreenSizesTests
{
    private static readonly ScreenSize Fhd = new(1920, 1080);

    [Test]
    public void Offered_OmitsModesLargerThanTheScreen()
    {
        var offered = ScreenSizes.Offered(new ScreenSize(1920, 1080), Fhd);

        Assert.That(offered, Does.Not.Contain(new ScreenSize(2560, 1440)));
        Assert.That(offered, Does.Contain(Fhd));
        Assert.That(offered, Does.Contain(new ScreenSize(1280, 720)));
    }

    [Test]
    public void Offered_JudgesBothAxes_NotJustWidth()
    {
        // A 1920x1200 panel fits 1920x1080; a 1920x800 one does not, and only the height says so.
        Assert.That(ScreenSizes.Offered(new ScreenSize(1920, 1200), Fhd), Does.Contain(Fhd));
        Assert.That(
            ScreenSizes.Offered(new ScreenSize(1920, 800), new ScreenSize(1280, 720)),
            Does.Not.Contain(Fhd));
    }

    [Test]
    public void Offered_KeepsACurrentSizeThatIsNotOnTheLadder()
    {
        var dragged = new ScreenSize(1401, 907);

        var offered = ScreenSizes.Offered(new ScreenSize(1920, 1080), dragged);

        Assert.That(
            offered,
            Does.Contain(dragged),
            "a dropdown whose selected value is missing has nothing honest to display");
    }

    [Test]
    public void Offered_KeepsACurrentSizeLargerThanTheScreen()
    {
        var fromABiggerMonitor = new ScreenSize(3840, 2160);

        var offered = ScreenSizes.Offered(new ScreenSize(1366, 768), fromABiggerMonitor);

        Assert.That(offered, Does.Contain(fromABiggerMonitor), "a settings file can outlive the monitor it was written on");
    }

    [Test]
    public void Offered_IsNeverEmpty_EvenOnAScreenNothingFits()
    {
        var offered = ScreenSizes.Offered(new ScreenSize(320, 240), new ScreenSize(320, 240));

        Assert.That(offered, Is.Not.Empty, "the caller must never have to render a dropdown with no options");
    }

    [Test]
    public void Offered_IsAscendingAndDistinct()
    {
        var offered = ScreenSizes.Offered(new ScreenSize(3840, 2160), Fhd);

        Assert.That(offered, Is.Unique, "the current size must not be listed twice when it is on the ladder");
        Assert.That(
            offered.Select(s => s.Width),
            Is.Ordered,
            "an unsorted dropdown makes the player hunt for the size they want");
    }
}
