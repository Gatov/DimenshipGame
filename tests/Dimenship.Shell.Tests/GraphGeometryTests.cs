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
    public void ASameRowEdge_RunsStraightBetweenTheFacingSides()
    {
        var from = GraphGeometry.CellRect(0, 0);
        var to = GraphGeometry.CellRect(1, 0);

        var points = GraphGeometry.EdgePolyline(from, to, parallelIndex: 0);

        var midY = from.Y + (from.H / 2);
        Assert.That(points, Is.EqualTo(new[]
        {
            (from.X + from.W, midY),
            ((from.X + from.W + to.X) / 2, midY),
            (to.X, midY),
        }));
    }

    [Test]
    public void ASameRowEdge_LeavesTheLeftSideWhenTheTargetIsToTheLeft()
    {
        var from = GraphGeometry.CellRect(1, 0);
        var to = GraphGeometry.CellRect(0, 0);

        var points = GraphGeometry.EdgePolyline(from, to, parallelIndex: 0);

        Assert.That(points[0].X, Is.EqualTo(from.X), "it leaves the side facing the target");
        Assert.That(points[^1].X, Is.EqualTo(to.X + to.W), "and arrives at the target's facing side");
    }

    [Test]
    public void ASameColumnEdge_RunsVerticallyBetweenTheFacingSides()
    {
        var from = GraphGeometry.CellRect(0, 0);
        var to = GraphGeometry.CellRect(0, 1);

        var points = GraphGeometry.EdgePolyline(from, to, parallelIndex: 0);

        var midX = from.X + (from.W / 2);
        Assert.That(points, Is.EqualTo(new[]
        {
            (midX, from.Y + from.H),
            (midX, (from.Y + from.H + to.Y) / 2),
            (midX, to.Y),
        }));
    }

    [Test]
    public void ADiagonalEdge_ElbowsInTheGutterBetweenTheColumns()
    {
        var from = GraphGeometry.CellRect(0, 0);
        var to = GraphGeometry.CellRect(1, 1);

        var points = GraphGeometry.EdgePolyline(from, to, parallelIndex: 0);

        var gutterX = (from.X + from.W + to.X) / 2;
        Assert.That(points, Is.EqualTo(new[]
        {
            (from.X + from.W, from.Y + (from.H / 2)),
            (gutterX, from.Y + (from.H / 2)),
            (gutterX, to.Y + (to.H / 2)),
            (to.X, to.Y + (to.H / 2)),
        }));
    }

    [TestCase(0, 0, 1, 0, TestName = "Same row")]
    [TestCase(0, 0, 0, 1, TestName = "Same column")]
    [TestCase(0, 0, 1, 1, TestName = "Diagonal down-right")]
    [TestCase(2, 1, 0, 0, TestName = "Diagonal up-left")]
    public void EverySegment_IsAxisAligned(int fromCol, int fromRow, int toCol, int toRow)
    {
        var points = GraphGeometry.EdgePolyline(
            GraphGeometry.CellRect(fromCol, fromRow), GraphGeometry.CellRect(toCol, toRow), 1);

        for (var i = 1; i < points.Count; i++)
        {
            Assert.That(
                points[i].X == points[i - 1].X || points[i].Y == points[i - 1].Y, Is.True,
                $"segment {i} runs diagonally; an orthogonal edge must turn, never slant");
        }
    }

    [TestCase(0, 0, 1, 0, TestName = "Same row")]
    [TestCase(0, 0, 0, 1, TestName = "Same column")]
    [TestCase(0, 0, 1, 1, TestName = "Diagonal")]
    public void ParallelEdges_DoNotOverprint(int fromCol, int fromRow, int toCol, int toRow)
    {
        var from = GraphGeometry.CellRect(fromCol, fromRow);
        var to = GraphGeometry.CellRect(toCol, toRow);

        var first = GraphGeometry.EdgePolyline(from, to, parallelIndex: 0);
        var second = GraphGeometry.EdgePolyline(from, to, parallelIndex: 1);

        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(second.Count, Is.EqualTo(first.Count), "the offset must not change the shape");
    }

    [Test]
    public void AnIdenticalPair_CollapsesRatherThanDrawingBackwards()
    {
        var cell = GraphGeometry.CellRect(0, 0);

        var points = GraphGeometry.EdgePolyline(cell, cell, parallelIndex: 0);

        Assert.That(
            points.Distinct().Count(), Is.EqualTo(1),
            "a self-route is a definition error; the view must not render it as a stray line");
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
