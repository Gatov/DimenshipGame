namespace Dimenship.Shell;

/// <summary>
/// Where a graph node's card sits, whether a line runs through one, and how near a click lands to
/// a line. Grid cells in, pixels out — placements are authored as cells, and this is the one place
/// that turns them into geometry. How an edge gets from one card to another is
/// <see cref="GraphRouting"/>'s, which routes them all together. Integer throughout so every case
/// is exactly testable without an editor.
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
