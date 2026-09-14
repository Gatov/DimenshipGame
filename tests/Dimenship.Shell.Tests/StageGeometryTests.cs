using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class StageGeometryTests
{
    private static readonly StageFit Identity = new(1000, 0, 0);

    [Test]
    public void Fit_ANarrowStage_LetterboxesVertically_AndCentres()
    {
        var fit = StageGeometry.Fit((1200, 720), (600, 720));

        Assert.That(fit, Is.EqualTo(new StageFit(500, 0, 180)));
    }

    [Test]
    public void Fit_AWideStage_LetterboxesHorizontally_AndCentres()
    {
        var fit = StageGeometry.Fit((1200, 720), (2400, 720));

        Assert.That(fit, Is.EqualTo(new StageFit(1000, 600, 0)));
    }

    [Test]
    public void Fit_AnEmptyStage_ScalesToNothing_RatherThanDividingByZero()
    {
        Assert.That(StageGeometry.Fit((1200, 720), (0, 0)), Is.EqualTo(new StageFit(0, 0, 0)));
    }

    [Test]
    public void ToStage_ScalesThenOffsets()
    {
        Assert.That(StageGeometry.ToStage(new StageFit(500, 10, 20), (200, 100)), Is.EqualTo((110, 70)));
    }

    [Test]
    public void BoxRect_CentresTheBoxOnItsScaledCentre()
    {
        var rect = StageGeometry.BoxRect(Identity, (100, 100), (184, 112), (1200, 720));

        Assert.That(rect, Is.EqualTo((8, 44, 184, 112)));
    }

    [Test]
    public void BoxRect_ABoxPastTheEdge_IsClampedInsideTheStage()
    {
        var rect = StageGeometry.BoxRect(Identity, (50, 700), (184, 112), (1200, 720));

        Assert.That(rect, Is.EqualTo((0, 608, 184, 112)));
    }

    [Test]
    public void BoxRect_TheBoxSizeDoesNotScale_BecauseItsTextMustNot()
    {
        var rect = StageGeometry.BoxRect(new StageFit(500, 0, 0), (400, 400), (184, 112), (1200, 720));

        Assert.That((rect.W, rect.H), Is.EqualTo((184, 112)));
        Assert.That((rect.X, rect.Y), Is.EqualTo((108, 144)));
    }

    [Test]
    public void Leader_ToAnAnchorOnTheRight_LeavesTheRightSide_WithAHorizontalStub()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (300, 10));

        Assert.That(leader, Is.EqualTo(new[] { (100, 25), (116, 25), (300, 10) }));
    }

    [Test]
    public void Leader_ToAnAnchorOnTheLeft_LeavesTheLeftSide()
    {
        var leader = StageGeometry.Leader((300, 0, 100, 50), (0, 40));

        Assert.That(leader, Is.EqualTo(new[] { (300, 25), (284, 25), (0, 40) }));
    }

    [Test]
    public void Leader_ToAnAnchorBelowTheBoxsSpan_LeavesTheBottom()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (50, 200));

        Assert.That(leader, Is.EqualTo(new[] { (50, 50), (50, 66), (50, 200) }));
    }

    [Test]
    public void Leader_TheStubNeverOvershootsAnAnchorCloserThanItsLength()
    {
        var leader = StageGeometry.Leader((0, 0, 100, 50), (108, 25));

        Assert.That(leader, Is.EqualTo(new[] { (100, 25), (108, 25), (108, 25) }));
    }

    [Test]
    public void Problems_ACleanLayout_HasNone()
    {
        var boxes = new[] { (0, 0, 100, 50), (0, 100, 100, 50) };
        var leaders = new[]
        {
            StageGeometry.Leader(boxes[0], (300, 25)),
            StageGeometry.Leader(boxes[1], (300, 125)),
        };

        Assert.That(StageGeometry.Problems(boxes, leaders), Is.Empty);
    }

    [Test]
    public void Problems_OverlappingBoxes_AreReported()
    {
        var boxes = new[] { (0, 0, 100, 50), (50, 25, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[] { new[] { (100, 25) }, new[] { (150, 50) } };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Does.Contain(new StageProblem(StageProblemKind.BoxesOverlap, 0, 1)));
    }

    [Test]
    public void Problems_BoxesThatOnlyTouch_DoNotOverlap()
    {
        var boxes = new[] { (0, 0, 100, 50), (100, 0, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[] { new[] { (50, 0) }, new[] { (150, 0) } };

        Assert.That(StageGeometry.Problems(boxes, leaders), Is.Empty);
    }

    [Test]
    public void Problems_CrossingLeaders_AreReported()
    {
        var boxes = new[] { (0, 0, 10, 10), (0, 200, 10, 10) };
        var leaders = new IReadOnlyList<(int X, int Y)>[]
        {
            new[] { (10, 5), (300, 205) },
            new[] { (10, 205), (300, 5) },
        };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Is.EqualTo(new[] { new StageProblem(StageProblemKind.LeadersCross, 0, 1) }));
    }

    [Test]
    public void Problems_ALeaderThroughAnotherBox_IsReported_ButNotThroughItsOwn()
    {
        var boxes = new[] { (0, 0, 100, 50), (200, 0, 100, 50) };
        var leaders = new IReadOnlyList<(int X, int Y)>[]
        {
            new[] { (100, 25), (400, 40) },
            new[] { (300, 25), (500, 0) },
        };

        Assert.That(
            StageGeometry.Problems(boxes, leaders),
            Is.EqualTo(new[] { new StageProblem(StageProblemKind.LeaderThroughBox, 0, 1) }));
    }

    [Test]
    public void PopoverRect_ABoxRightOfCentre_OpensToItsRight_WhenThereIsRoom()
    {
        var rect = StageGeometry.PopoverRect((800, 100, 184, 112), (300, 200), 600, (1400, 720));

        Assert.That(rect, Is.EqualTo((996, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_FlipsToTheOtherSide_WhenThePreferredSideHasNoRoom()
    {
        var rect = StageGeometry.PopoverRect((800, 100, 184, 112), (300, 200), 600, (1200, 720));

        Assert.That(rect, Is.EqualTo((488, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_ABoxLeftOfCentre_PrefersItsLeft_AndFlipsRightWhenCramped()
    {
        var rect = StageGeometry.PopoverRect((100, 100, 184, 112), (300, 200), 600, (1200, 720));

        Assert.That(rect, Is.EqualTo((296, 100, 300, 200)));
    }

    [Test]
    public void PopoverRect_IsClampedVertically_InsideTheStage()
    {
        var rect = StageGeometry.PopoverRect((800, 600, 184, 112), (300, 200), 600, (1400, 720));

        Assert.That(rect, Is.EqualTo((996, 520, 300, 200)));
    }
}
