using NUnit.Framework;
using View = Dimenship.Shell.GraphCamera.View;

namespace Dimenship.Shell.Tests;

public class GraphCameraTests
{
    // Content 1000 x 600 in a 900 x 700 view: too wide at 100%, fits at 75% (750 x 450).
    private const float ContentW = 1000;
    private const float ContentH = 600;
    private const float ViewW = 900;
    private const float ViewH = 700;

    private static View Out(View view, float x = 100, float y = 100) =>
        GraphCamera.ZoomOut(view, x, y, ContentW, ContentH, ViewW, ViewH);

    [Test]
    public void FitStep_IsTheLargestStepAtWhichEverythingFits()
    {
        Assert.That(GraphCamera.FitStep(ContentW, ContentH, ViewW, ViewH), Is.EqualTo(1), "75%");
        Assert.That(GraphCamera.FitStep(400, 300, ViewW, ViewH), Is.EqualTo(4), "200% still fits");
        Assert.That(GraphCamera.FitStep(5000, 5000, ViewW, ViewH), Is.EqualTo(0), "nothing fits: the smallest step");
    }

    [Test]
    public void FitStep_BeforeAnythingIsMeasured_SetsNoFloor()
    {
        Assert.That(GraphCamera.FitStep(0, 0, ViewW, ViewH), Is.Zero);
        Assert.That(GraphCamera.FitStep(ContentW, ContentH, 0, 0), Is.Zero);
    }

    [Test]
    public void ZoomIn_KeepsThePointUnderTheCursorWhereItIs()
    {
        var view = new View(GraphCamera.RestingStep, 40, 20);
        var (cx, cy) = (300f, 200f);
        var contentX = (cx - view.PanX) / view.Scale;
        var contentY = (cy - view.PanY) / view.Scale;

        var zoomed = GraphCamera.ZoomIn(view, cx, cy);

        Assert.That(zoomed.Step, Is.EqualTo(view.Step + 1));
        Assert.That(zoomed.PanX + (contentX * zoomed.Scale), Is.EqualTo(cx).Within(0.01f));
        Assert.That(zoomed.PanY + (contentY * zoomed.Scale), Is.EqualTo(cy).Within(0.01f));
    }

    [Test]
    public void ZoomIn_AtTheLargestStep_ChangesNothing()
    {
        var view = new View(GraphCamera.Steps.Count - 1, 5, 6);

        Assert.That(GraphCamera.ZoomIn(view, 100, 100), Is.EqualTo(view));
    }

    [Test]
    public void ZoomOut_NeverGoesBelowTheStepAtWhichEverythingFits()
    {
        var view = new View(1, -300, 80);

        Assert.That(Out(view).Step, Is.EqualTo(1));
    }

    [Test]
    public void ZoomOut_AtTheFloor_CentresTheScheme()
    {
        var view = Out(new View(1, -300, 80));

        Assert.That(view.PanX, Is.EqualTo(MathF.Floor((ViewW - 750) / 2)));
        Assert.That(view.PanY, Is.EqualTo(MathF.Floor((ViewH - 450) / 2)));
    }

    [Test]
    public void ZoomOut_OntoTheFloor_LandsExactlyCentred_WhereverTheCursorIs()
    {
        var view = Out(new View(2, -400, -250), x: 850, y: 30);

        Assert.That(view, Is.EqualTo(GraphCamera.Fit(ContentW, ContentH, ViewW, ViewH)));
    }

    [Test]
    public void ZoomOut_TwoStepsAboveTheFloor_GoesHalfwayFromTheCursorsAnchorToTheCentre()
    {
        var start = new View(3, -500, -300);
        var (cx, cy) = (100f, 100f);

        var view = Out(start, cx, cy);

        // Anchored on the cursor from 150% to 100%, then halfway to the 100% centre.
        var anchoredX = cx - ((cx - start.PanX) * (100f / 150f));
        var centredX = MathF.Floor((ViewW - ContentW) / 2);
        Assert.That(view.Step, Is.EqualTo(2));
        Assert.That(view.PanX, Is.EqualTo(MathF.Floor(anchoredX + ((centredX - anchoredX) / 2))));
    }

    [Test]
    public void Fit_IsTheFloorStep_Centred()
    {
        var fit = GraphCamera.Fit(ContentW, ContentH, ViewW, ViewH);

        Assert.That(fit, Is.EqualTo(new View(1, MathF.Floor((ViewW - 750) / 2), MathF.Floor((ViewH - 450) / 2))));
    }
}
