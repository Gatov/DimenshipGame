namespace Dimenship.Shell;

/// <summary>
/// Where a graph node's card sits and how an edge gets from one card to another. Grid cells in,
/// pixels out — placements are authored as cells, and this is the one place that turns them into
/// geometry. Integer throughout so every case is exactly testable without an editor.
/// </summary>
public static class GraphGeometry
{
    public const int CellWidth = 220;

    /// <summary>
    /// Tall enough for the card anatomy the style spec defines: a 40px icon row beside a title and
    /// a caption line, two metric rows, and a meter, inside the card's own padding.
    /// </summary>
    public const int CellHeight = 128;
    public const int GutterX = 48;
    public const int GutterY = 40;

    /// <summary>How far parallel edges between one pair of cards are pushed apart.</summary>
    private const int ParallelOffset = 6;

    private const int StrideX = CellWidth + GutterX;
    private const int StrideY = CellHeight + GutterY;

    /// <summary>The pixel rectangle of a grid cell.</summary>
    public static (int X, int Y, int W, int H) CellRect(int column, int row) =>
        (column * StrideX, row * StrideY, CellWidth, CellHeight);

    /// <summary>
    /// The canvas size that holds every supplied cell whole. Measured from the origin rather than
    /// from the topmost-leftmost cell, because cards are positioned at their absolute
    /// <see cref="CellRect"/> and a canvas smaller than that would clip them.
    /// </summary>
    public static (int W, int H) ContentSize(IEnumerable<(int Column, int Row)> cells)
    {
        var width = 0;
        var height = 0;

        foreach (var (column, row) in cells)
        {
            var rect = CellRect(column, row);
            width = Math.Max(width, rect.X + rect.W);
            height = Math.Max(height, rect.Y + rect.H);
        }

        return (width, height);
    }

    /// <summary>
    /// An orthogonal route from one card to another: leave the side facing the target, elbow in
    /// the gutter, arrive at the target's facing side. <paramref name="parallelIndex"/> pushes the
    /// whole polyline sideways so several edges between the same pair do not overprint.
    /// <para>
    /// Merging an opposing pair into one double-headed edge is the view's job, not this method's —
    /// here, A to B and B to A are two separate routes and each gets its own line.
    /// </para>
    /// <para>
    /// An elbow's vertical leg sits midway between the two cards, unless that would draw it, or
    /// either horizontal leg, through one of <paramref name="cards"/>. It then moves to the
    /// nearest gutter that keeps clear of every card. An edge that already runs clear keeps its
    /// midpoint, so a card added elsewhere never redraws it. The treatment lines (K4) are why:
    /// from Factory Alpha to either reactor the midpoint is the middle of Resource Storage's
    /// column, and the edge drew straight across that card. Moving a card would have changed the
    /// route lengths, which are the Manhattan distance between cards. Straight routes are not
    /// rerouted, because there is nowhere else for them to go.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> EdgePolyline(
        (int X, int Y, int W, int H) from,
        (int X, int Y, int W, int H) to,
        int parallelIndex,
        IReadOnlyCollection<(int X, int Y, int W, int H)>? cards = null)
    {
        var offset = parallelIndex * ParallelOffset;

        var fromMidX = from.X + (from.W / 2);
        var fromMidY = from.Y + (from.H / 2);
        var toMidX = to.X + (to.W / 2);
        var toMidY = to.Y + (to.H / 2);

        if (from.Y == to.Y && from.X != to.X)
        {
            var (leaveX, arriveX) = HorizontalSides(from, to);
            var y = fromMidY + offset;

            return new[] { (leaveX, y), ((leaveX + arriveX) / 2, y), (arriveX, y) };
        }

        if (from.X == to.X && from.Y != to.Y)
        {
            var (leaveY, arriveY) = VerticalSides(from, to);
            var x = fromMidX + offset;

            return new[] { (x, leaveY), (x, (leaveY + arriveY) / 2), (x, arriveY) };
        }

        if (from.X != to.X)
        {
            var (leaveX, arriveX) = HorizontalSides(from, to);
            var midX = (leaveX + arriveX) / 2;

            (int X, int Y)[] Elbow(int x) => new[]
            {
                (leaveX, fromMidY + offset),
                (x + offset, fromMidY + offset),
                (x + offset, toMidY + offset),
                (arriveX, toMidY + offset),
            };

            var obstacles = (cards ?? Array.Empty<(int X, int Y, int W, int H)>())
                .Where(card => card != from && card != to)
                .ToList();

            // Midpoint first, then the gutters between the cards, nearest the midpoint first and
            // nearer the target on a tie. If nothing is clear, the midpoint, as before.
            var lo = Math.Min(leaveX, arriveX);
            var hi = Math.Max(leaveX, arriveX);
            var gutters = Enumerable.Range(0, (hi / StrideX) + 1)
                .Select(column => (column * StrideX) + CellWidth + (GutterX / 2))
                .Where(x => x > lo && x < hi)
                .OrderBy(x => Math.Abs(x - midX))
                .ThenBy(x => Math.Abs(x - arriveX));

            foreach (var x in gutters.Prepend(midX))
            {
                var polyline = Elbow(x);
                if (!obstacles.Any(card => Crosses(polyline, card)))
                {
                    return polyline;
                }
            }

            return Elbow(midX);
        }

        // The same cell twice. A self-route is a definition error the kernel rejects; drawing a
        // stray backwards line here would be a worse answer than drawing nothing.
        return new[] { (fromMidX, fromMidY), (toMidX, toMidY) };
    }

    /// <summary>
    /// Whether an orthogonal polyline passes through the inside of a card. Running along its
    /// border is not crossing it.
    /// </summary>
    public static bool Crosses(IReadOnlyList<(int X, int Y)> polyline, (int X, int Y, int W, int H) card)
    {
        for (var i = 1; i < polyline.Count; i++)
        {
            var (a, b) = (polyline[i - 1], polyline[i]);
            var (minX, maxX) = (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
            var (minY, maxY) = (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));

            // Strict on both axes: a segment on the card's edge line touches it without crossing.
            if (maxX > card.X && minX < card.X + card.W && maxY > card.Y && minY < card.Y + card.H)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The squared distance from a point to the nearest part of a polyline. Squared because
    /// nothing needs the true distance — a click either lands within a threshold or it does not,
    /// and comparing squares keeps the whole hit test in integers.
    /// </summary>
    public static int HitDistanceSquared(IReadOnlyList<(int X, int Y)> polyline, int x, int y)
    {
        if (polyline.Count == 0)
        {
            return int.MaxValue;
        }

        if (polyline.Count == 1)
        {
            return (int)Math.Min(int.MaxValue, SquaredDistance(polyline[0], (x, y)));
        }

        var nearest = long.MaxValue;

        for (var i = 1; i < polyline.Count; i++)
        {
            nearest = Math.Min(nearest, SegmentDistanceSquared(polyline[i - 1], polyline[i], x, y));
        }

        return (int)Math.Min(int.MaxValue, nearest);
    }

    /// <summary>Which vertical sides two cards present to each other.</summary>
    private static (int Leave, int Arrive) HorizontalSides(
        (int X, int Y, int W, int H) from, (int X, int Y, int W, int H) to) =>
        to.X > from.X ? (from.X + from.W, to.X) : (from.X, to.X + to.W);

    /// <summary>Which horizontal sides two cards present to each other.</summary>
    private static (int Leave, int Arrive) VerticalSides(
        (int X, int Y, int W, int H) from, (int X, int Y, int W, int H) to) =>
        to.Y > from.Y ? (from.Y + from.H, to.Y) : (from.Y, to.Y + to.H);

    private static long SegmentDistanceSquared((int X, int Y) a, (int X, int Y) b, int x, int y)
    {
        long dx = b.X - a.X;
        long dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared == 0)
        {
            return SquaredDistance(a, (x, y));
        }

        var along = ((x - (long)a.X) * dx) + ((y - (long)a.Y) * dy);

        if (along <= 0)
        {
            return SquaredDistance(a, (x, y));
        }

        if (along >= lengthSquared)
        {
            return SquaredDistance(b, (x, y));
        }

        // Perpendicular distance by the cross product. Every segment this class produces is
        // axis-aligned, so one of dx or dy is zero and the division below is exact.
        var cross = ((x - (long)a.X) * dy) - ((y - (long)a.Y) * dx);

        return cross * cross / lengthSquared;
    }

    private static long SquaredDistance((int X, int Y) a, (int X, int Y) b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;

        return (dx * dx) + (dy * dy);
    }
}
