using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class GraphGeometryTests
{
    private const int StrideX = GraphGeometry.CellWidth + GraphGeometry.GutterX;
    private const int StrideY = GraphGeometry.CellHeight + GraphGeometry.GutterY;

    [Test]
    public void TheOriginCell_SitsAtZeroAndIsOneCellBig()
    {
        Assert.That(
            GraphGeometry.CellRect(0, 0),
            Is.EqualTo((0, 0, GraphGeometry.CellWidth, GraphGeometry.CellHeight)));
    }

    [Test]
    public void EveryCell_IsOneStrideFromItsNeighbour()
    {
        Assert.That(GraphGeometry.CellRect(2, 3).X, Is.EqualTo(2 * StrideX));
        Assert.That(GraphGeometry.CellRect(2, 3).Y, Is.EqualTo(3 * StrideY));
    }

    [Test]
    public void AdjacentCells_AreSeparatedByExactlyTheGutter()
    {
        var left = GraphGeometry.CellRect(0, 0);
        var right = GraphGeometry.CellRect(1, 0);

        Assert.That(right.X - (left.X + left.W), Is.EqualTo(GraphGeometry.GutterX));
    }

    [Test]
    public void ContentSize_BoundsEveryCellFromTheOrigin()
    {
        var size = GraphGeometry.ContentSize(new[] { (1, 1), (2, 0) });

        Assert.That(
            size.W, Is.EqualTo(2 * StrideX + GraphGeometry.CellWidth),
            "the widest cell is column 2, and its whole card must fit");
        Assert.That(
            size.H, Is.EqualTo(StrideY + GraphGeometry.CellHeight),
            "the lowest cell is row 1");
    }

    [Test]
    public void ContentSize_OfNothing_IsNothing()
    {
        Assert.That(GraphGeometry.ContentSize(Array.Empty<(int, int)>()), Is.EqualTo((0, 0)));
    }

    [Test]
    public void ALineAlongACardsEdge_DoesNotCrossIt()
    {
        var card = GraphGeometry.CellRect(1, 1);

        Assert.That(GraphGeometry.Crosses(new[] { (card.X, 0), (card.X, 1000) }, card), Is.False);
        Assert.That(GraphGeometry.Crosses(new[] { (card.X + 1, 0), (card.X + 1, 1000) }, card), Is.True);
    }

    [Test]
    public void HitDistance_IsZeroOnAVertex()
    {
        var polyline = new[] { (0, 0), (10, 0), (10, 10) };

        Assert.That(GraphGeometry.HitDistanceSquared(polyline, 10, 0), Is.EqualTo(0));
    }

    [Test]
    public void HitDistance_IsZeroAlongASegment()
    {
        var polyline = new[] { (0, 0), (10, 0), (10, 10) };

        Assert.That(GraphGeometry.HitDistanceSquared(polyline, 4, 0), Is.EqualTo(0));
    }

    [Test]
    public void HitDistance_GrowsWithPerpendicularDistance()
    {
        var polyline = new[] { (0, 0), (10, 0) };

        Assert.That(GraphGeometry.HitDistanceSquared(polyline, 5, 3), Is.EqualTo(9));
        Assert.That(
            GraphGeometry.HitDistanceSquared(polyline, 5, 6),
            Is.GreaterThan(GraphGeometry.HitDistanceSquared(polyline, 5, 3)));
    }

    [Test]
    public void HitDistance_MeasuresToTheEndpoint_NotTheInfiniteLine()
    {
        var polyline = new[] { (0, 0), (10, 0) };

        Assert.That(
            GraphGeometry.HitDistanceSquared(polyline, 14, 3), Is.EqualTo((4 * 4) + (3 * 3)),
            "a click past the end of a line is not a click on it");
    }

    [Test]
    public void HitDistance_TakesTheNearestSegment()
    {
        var polyline = new[] { (0, 0), (10, 0), (10, 10) };

        Assert.That(
            GraphGeometry.HitDistanceSquared(polyline, 12, 9), Is.EqualTo(4),
            "the second segment is nearer than the first");
    }

    [Test]
    public void HitDistance_OfASinglePoint_IsTheDistanceToIt()
    {
        Assert.That(GraphGeometry.HitDistanceSquared(new[] { (3, 4) }, 0, 0), Is.EqualTo(25));
    }

    [Test]
    public void HitDistance_OfNoPointsAtAll_IsTheLargestPossibleValue()
    {
        Assert.That(
            GraphGeometry.HitDistanceSquared(Array.Empty<(int, int)>(), 0, 0),
            Is.EqualTo(int.MaxValue),
            "an empty polyline is never the nearest thing to a click");
    }
}
