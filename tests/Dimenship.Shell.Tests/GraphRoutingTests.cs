using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class GraphRoutingTests
{
    private static (int X, int Y, int W, int H) Cell(int column, int row) => GraphGeometry.CellRect(column, row);

    private static IReadOnlyList<(int X, int Y)> One((int, int, int, int) from, (int, int, int, int) to) =>
        GraphRouting.Route(new[] { (from, to) })[0];

    /// <summary>
    /// The shipped vessel's cards, by cell: power, the extractor, Resource Storage, both reactors,
    /// the three factories and both pads.
    /// </summary>
    private static readonly (int X, int Y, int W, int H)[] Cards =
        new[] { (4, 0), (2, 0), (2, 2), (0, 2), (0, 3), (4, 1), (4, 2), (4, 3), (1, 4), (3, 4) }
            .Select(cell => Cell(cell.Item1, cell.Item2))
            .ToArray();

    private static readonly (int X, int Y, int W, int H) Storage = Cell(2, 2);
    private static readonly (int X, int Y, int W, int H) ReactorAlpha = Cell(0, 2);
    private static readonly (int X, int Y, int W, int H) FactoryAlpha = Cell(4, 1);
    private static readonly (int X, int Y, int W, int H) FactoryBeta = Cell(4, 2);
    private static readonly (int X, int Y, int W, int H) FactoryGamma = Cell(4, 3);

    /// <summary>
    /// The shipped vessel's drawn edges, as the base graph merges them: one per route, except an
    /// opposing pair, in route declaration order.
    /// </summary>
    private static readonly ((int X, int Y, int W, int H) From, (int X, int Y, int W, int H) To)[] Edges =
    {
        (Cell(2, 0), Storage),          // extractor out
        (Storage, ReactorAlpha),        // reactor alpha feed and return
        (Storage, Cell(0, 3)),          // reactor beta feed and return
        (Storage, FactoryAlpha),        // factory alpha feed and return
        (Storage, FactoryBeta),         // factory beta feed and return
        (FactoryAlpha, FactoryBeta),    // link a-b
        (FactoryBeta, FactoryGamma),    // link b-c
        (FactoryGamma, Storage),        // factory gamma return and feed
        (Storage, FactoryBeta),         // factory beta component feed
        (FactoryAlpha, ReactorAlpha),   // reactor alpha treatment pair
        (FactoryAlpha, Cell(0, 3)),     // reactor beta treatment pair
        (Storage, Cell(1, 4)),          // launch pad 1
        (Storage, Cell(3, 4)),          // launch pad 2
    };

    private static IReadOnlyList<IReadOnlyList<(int X, int Y)>> Vessel() => GraphRouting.Route(Edges, Cards);

    [Test]
    public void ALoneSameRowEdge_RunsStraightBetweenTheFacingSides()
    {
        var from = Cell(0, 0);
        var to = Cell(1, 0);

        var midY = from.Y + (from.H / 2);
        Assert.That(One(from, to), Is.EqualTo(new[]
        {
            (from.X + from.W, midY),
            ((from.X + from.W + to.X) / 2, midY),
            (to.X, midY),
        }));
    }

    [Test]
    public void ASameRowEdge_LeavesTheLeftSideWhenTheTargetIsToTheLeft()
    {
        var from = Cell(1, 0);
        var to = Cell(0, 0);

        var points = One(from, to);

        Assert.That(points[0].X, Is.EqualTo(from.X), "it leaves the side facing the target");
        Assert.That(points[^1].X, Is.EqualTo(to.X + to.W), "and arrives at the target's facing side");
    }

    [Test]
    public void ALoneSameColumnEdge_RunsVerticallyBetweenTheFacingSides()
    {
        var from = Cell(0, 0);
        var to = Cell(0, 1);

        var midX = from.X + (from.W / 2);
        Assert.That(One(from, to), Is.EqualTo(new[]
        {
            (midX, from.Y + from.H),
            (midX, (from.Y + from.H + to.Y) / 2),
            (midX, to.Y),
        }));
    }

    [Test]
    public void ALoneDiagonalEdge_ElbowsMidwayBetweenTheCards()
    {
        var from = Cell(0, 0);
        var to = Cell(1, 1);

        var gutterX = (from.X + from.W + to.X) / 2;
        Assert.That(One(from, to), Is.EqualTo(new[]
        {
            (from.X + from.W, from.Y + (from.H / 2)),
            (gutterX, from.Y + (from.H / 2)),
            (gutterX, to.Y + (to.H / 2)),
            (to.X, to.Y + (to.H / 2)),
        }));
    }

    [TestCase(0, 0, 1, 0, TestName = "Same row")]
    [TestCase(0, 0, 0, 1, TestName = "Same column")]
    [TestCase(0, 0, 1, 1, TestName = "Diagonal")]
    public void TwoEdgesBetweenOnePair_NeitherOverprintNorCross(int fromCol, int fromRow, int toCol, int toRow)
    {
        var from = Cell(fromCol, fromRow);
        var to = Cell(toCol, toRow);

        var routes = GraphRouting.Route(new[] { (from, to), (from, to) });

        Assert.That(routes[1], Is.Not.EqualTo(routes[0]));
        Assert.That(GraphRouting.Crossings(routes[0], routes[1]), Is.Zero);
    }

    [Test]
    public void OnTheShippedVessel_EverySegmentIsAxisAligned()
    {
        foreach (var route in Vessel())
        {
            for (var i = 1; i < route.Count; i++)
            {
                Assert.That(
                    route[i].X == route[i - 1].X || route[i].Y == route[i - 1].Y, Is.True,
                    "an orthogonal edge must turn, never slant");
            }
        }
    }

    [Test]
    public void OnTheShippedVessel_NoEdgeRunsThroughACard_IncludingTheTwoItJoins()
    {
        var routes = Vessel();

        for (var i = 0; i < routes.Count; i++)
        {
            Assert.That(Cards.Where(card => GraphGeometry.Crosses(routes[i], card)), Is.Empty, $"edge {i}");
        }
    }

    [Test]
    public void OnTheShippedVessel_EveryEdgeStartsAndEndsOnTheSideOfItsCard()
    {
        var routes = Vessel();

        for (var i = 0; i < routes.Count; i++)
        {
            Assert.That(OnBorder(routes[i][0], Edges[i].From), Is.True, $"edge {i} leaves its card");
            Assert.That(OnBorder(routes[i][^1], Edges[i].To), Is.True, $"edge {i} arrives at its card");
        }
    }

    [Test]
    public void OnTheShippedVessel_NoTwoEdgesShareAnEndpoint()
    {
        var ends = Vessel().SelectMany(route => new[] { route[0], route[^1] }).ToList();

        Assert.That(ends, Is.Unique, "two arrowheads on one point read as one line");
    }

    [Test]
    public void OnTheShippedVessel_SameRowRoutesStayStraight()
    {
        var routes = Vessel();

        foreach (var i in new[] { 1, 4, 8 })
        {
            Assert.That(routes[i].Select(point => point.Y).Distinct().Count(), Is.EqualTo(1), $"edge {i}");
        }
    }

    [Test]
    public void OnOneSide_AnEdgeHeadingUpLeavesAboveOneHeadingDown()
    {
        var routes = Vessel();

        var toFactoryAlpha = routes[3][0].Y;
        var toFactoryBeta = routes[4][0].Y;
        var fromFactoryGamma = routes[7][^1].Y;

        Assert.That(toFactoryAlpha, Is.LessThan(toFactoryBeta));
        Assert.That(toFactoryBeta, Is.LessThan(fromFactoryGamma));
    }

    /// <summary>
    /// The number the router exists to lower. The per-edge route it replaced crossed itself 11
    /// times on this vessel, counting a touch as one and an overlap as two. A change that raises this
    /// has made the graph harder to read; one that lowers it should lower the bound with it.
    /// </summary>
    [Test]
    public void OnTheShippedVessel_TheEdgesCrossAtMostAHandfulOfTimes()
    {
        var routes = Vessel();
        var crossings = 0;

        for (var i = 0; i < routes.Count; i++)
        {
            for (var j = i + 1; j < routes.Count; j++)
            {
                crossings += GraphRouting.Crossings(routes[i], routes[j]);
            }
        }

        Assert.That(crossings, Is.LessThanOrEqualTo(MaxCrossings));
    }

    private const int MaxCrossings = 5;

    [Test]
    public void TheRoute_IsTheSameEveryTime()
    {
        Assert.That(Vessel(), Is.EqualTo(Vessel()));
    }

    [Test]
    public void AnIdenticalPair_CollapsesRatherThanDrawingBackwards()
    {
        var cell = Cell(0, 0);

        Assert.That(
            One(cell, cell).Distinct().Count(), Is.EqualTo(1),
            "a self-route is a definition error; the view must not render it as a stray line");
    }

    [Test]
    public void Crossings_CountsAPerpendicularCross_AndAnOverlapDouble()
    {
        var level = new[] { (0, 5), (10, 5) };

        Assert.That(GraphRouting.Crossings(level, new[] { (5, 0), (5, 10) }), Is.EqualTo(1));
        Assert.That(GraphRouting.Crossings(level, new[] { (5, 6), (5, 10) }), Is.Zero);
        Assert.That(GraphRouting.Crossings(level, new[] { (5, 5), (20, 5) }), Is.EqualTo(2));
    }

    private static bool OnBorder((int X, int Y) point, (int X, int Y, int W, int H) card)
    {
        var onVertical = (point.X == card.X || point.X == card.X + card.W) &&
                         point.Y > card.Y && point.Y < card.Y + card.H;
        var onHorizontal = (point.Y == card.Y || point.Y == card.Y + card.H) &&
                           point.X > card.X && point.X < card.X + card.W;
        return onVertical || onHorizontal;
    }
}
