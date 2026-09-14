namespace Dimenship.Shell;

/// <summary>How a frame's artwork is fitted into the stage: a uniform scale and a centring offset.</summary>
public readonly record struct StageFit(int ScalePermille, int OffsetX, int OffsetY);

public enum StageProblemKind
{
    BoxesOverlap,
    LeadersCross,
    LeaderThroughBox,
}

/// <summary>
/// One authoring problem at the current stage size. <see cref="First"/> and <see cref="Second"/>
/// are socket indices: two boxes, two leaders, or a leader and the box it passes through.
/// </summary>
public sealed record StageProblem(StageProblemKind Kind, int First, int Second);

/// <summary>
/// Where the loadout composer's frame art, fitting boxes, leader lines and popovers sit. Artwork
/// coordinates in, stage pixels out — the stage-side counterpart of <see cref="GraphGeometry"/>, in
/// its idiom: integer tuples throughout, scale as permille, so every case is exactly testable
/// without an editor.
/// <para>
/// Box <b>positions</b> scale with the artwork and box <b>sizes</b> do not: a box carries text, and
/// text stays at the palette's sizes so it is sharp at every scale. The consequence is that boxes
/// can collide at small sizes, and <see cref="Problems"/> is how the view finds out. Nothing here
/// moves a box to avoid a collision — positions are authored, and an automatic layout would be a
/// second, unauthored answer to where a box belongs.
/// </para>
/// </summary>
public static class StageGeometry
{
    /// <summary>How far a leader runs straight out of its box before turning toward its anchor.</summary>
    public const int LeaderStub = 16;

    /// <summary>The space between a box and a popover opened beside it.</summary>
    public const int PopoverGap = 12;

    /// <summary>
    /// Letterboxes the canvas into the stage and centres it. Uniform scale only: a stretched
    /// drawing is a different machine.
    /// </summary>
    public static StageFit Fit((int W, int H) canvas, (int W, int H) stage)
    {
        if (canvas.W <= 0 || canvas.H <= 0 || stage.W <= 0 || stage.H <= 0)
        {
            return new StageFit(0, 0, 0);
        }

        var scale = (int)Math.Min((long)stage.W * 1000 / canvas.W, (long)stage.H * 1000 / canvas.H);

        return new StageFit(
            scale,
            (stage.W - Scale(canvas.W, scale)) / 2,
            (stage.H - Scale(canvas.H, scale)) / 2);
    }

    public static int Scale(int value, int permille) => (int)((long)value * permille / 1000);

    public static (int X, int Y) ToStage(StageFit fit, (int X, int Y) point) =>
        (fit.OffsetX + Scale(point.X, fit.ScalePermille), fit.OffsetY + Scale(point.Y, fit.ScalePermille));

    /// <summary>A box of fixed pixel size centred on its scaled centre, clamped inside the stage.</summary>
    public static (int X, int Y, int W, int H) BoxRect(
        StageFit fit, (int X, int Y) centre, (int W, int H) box, (int W, int H) stage)
    {
        var (x, y) = ToStage(fit, centre);

        return (
            Clamp(x - (box.W / 2), stage.W - box.W),
            Clamp(y - (box.H / 2), stage.H - box.H),
            box.W,
            box.H);
    }

    /// <summary>
    /// A leader from a box to its anchor: leave the middle of the side facing the anchor, run
    /// <see cref="LeaderStub"/> straight out, then go directly to the anchor — the shape the ticket's
    /// sketch draws. The stub never runs past an anchor closer than its length.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Leader((int X, int Y, int W, int H) box, (int X, int Y) anchor)
    {
        var midX = box.X + (box.W / 2);
        var midY = box.Y + (box.H / 2);
        var right = box.X + box.W;
        var bottom = box.Y + box.H;

        if (anchor.X >= right)
        {
            return new[] { (right, midY), (Math.Min(right + LeaderStub, anchor.X), midY), anchor };
        }

        if (anchor.X <= box.X)
        {
            return new[] { (box.X, midY), (Math.Max(box.X - LeaderStub, anchor.X), midY), anchor };
        }

        if (anchor.Y >= bottom)
        {
            return new[] { (midX, bottom), (midX, Math.Min(bottom + LeaderStub, anchor.Y)), anchor };
        }

        if (anchor.Y <= box.Y)
        {
            return new[] { (midX, box.Y), (midX, Math.Max(box.Y - LeaderStub, anchor.Y)), anchor };
        }

        // The anchor is under the box. Drawing a line to it would be drawing inside the box, and
        // Problems cannot see it either; the box covering its own connector is visible on its own.
        return new[] { (midX, midY), anchor };
    }

    /// <summary>
    /// Every box overlapping another, leader crossing another, and leader passing through a box
    /// other than its own. Boxes that only share an edge do not overlap. Index <c>i</c> of
    /// <paramref name="leaders"/> belongs to box <c>i</c>.
    /// </summary>
    public static IReadOnlyList<StageProblem> Problems(
        IReadOnlyList<(int X, int Y, int W, int H)> boxes,
        IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders)
    {
        var problems = new List<StageProblem>();

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (Overlaps(boxes[i], boxes[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.BoxesOverlap, i, j));
                }
            }
        }

        for (var i = 0; i < leaders.Count; i++)
        {
            for (var j = i + 1; j < leaders.Count; j++)
            {
                if (Cross(leaders[i], leaders[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.LeadersCross, i, j));
                }
            }
        }

        for (var i = 0; i < leaders.Count; i++)
        {
            for (var j = 0; j < boxes.Count; j++)
            {
                if (i != j && Enters(leaders[i], boxes[j]))
                {
                    problems.Add(new StageProblem(StageProblemKind.LeaderThroughBox, i, j));
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Where a popover opens beside a box: on the side facing away from the artwork's centre, so it
    /// covers margin rather than the machine; on the other side when the preferred one has no room;
    /// clamped inside the stage either way, top-aligned with the box.
    /// </summary>
    public static (int X, int Y, int W, int H) PopoverRect(
        (int X, int Y, int W, int H) box, (int W, int H) popover, int centreX, (int W, int H) stage)
    {
        var left = box.X - PopoverGap - popover.W;
        var right = box.X + box.W + PopoverGap;
        var preferRight = box.X + (box.W / 2) >= centreX;

        var x = preferRight
            ? (Fits(right) ? right : Fits(left) ? left : right)
            : (Fits(left) ? left : Fits(right) ? right : left);

        return (Clamp(x, stage.W - popover.W), Clamp(box.Y, stage.H - popover.H), popover.W, popover.H);

        bool Fits(int candidate) => candidate >= 0 && candidate + popover.W <= stage.W;
    }

    private static int Clamp(int value, int max) => Math.Max(0, Math.Min(value, max));

    private static bool Overlaps((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    private static bool Cross(IReadOnlyList<(int X, int Y)> a, IReadOnlyList<(int X, int Y)> b)
    {
        for (var i = 1; i < a.Count; i++)
        {
            for (var j = 1; j < b.Count; j++)
            {
                if (SegmentsIntersect(a[i - 1], a[i], b[j - 1], b[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Enters(IReadOnlyList<(int X, int Y)> leader, (int X, int Y, int W, int H) box)
    {
        var corners = new[]
        {
            (box.X, box.Y), (box.X + box.W, box.Y), (box.X + box.W, box.Y + box.H), (box.X, box.Y + box.H),
        };

        for (var i = 0; i < leader.Count; i++)
        {
            if (Inside(leader[i], box))
            {
                return true;
            }

            if (i == 0)
            {
                continue;
            }

            for (var edge = 0; edge < 4; edge++)
            {
                if (SegmentsIntersect(leader[i - 1], leader[i], corners[edge], corners[(edge + 1) % 4]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Inside((int X, int Y) p, (int X, int Y, int W, int H) box) =>
        p.X > box.X && p.X < box.X + box.W && p.Y > box.Y && p.Y < box.Y + box.H;

    private static bool SegmentsIntersect((int X, int Y) p1, (int X, int Y) p2, (int X, int Y) q1, (int X, int Y) q2)
    {
        var o1 = Orientation(p1, p2, q1);
        var o2 = Orientation(p1, p2, q2);
        var o3 = Orientation(q1, q2, p1);
        var o4 = Orientation(q1, q2, p2);

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return (o1 == 0 && OnSegment(p1, p2, q1))
            || (o2 == 0 && OnSegment(p1, p2, q2))
            || (o3 == 0 && OnSegment(q1, q2, p1))
            || (o4 == 0 && OnSegment(q1, q2, p2));
    }

    private static int Orientation((int X, int Y) p, (int X, int Y) q, (int X, int Y) r) =>
        Math.Sign(((long)(q.X - p.X) * (r.Y - p.Y)) - ((long)(q.Y - p.Y) * (r.X - p.X)));

    private static bool OnSegment((int X, int Y) p, (int X, int Y) q, (int X, int Y) r) =>
        r.X >= Math.Min(p.X, q.X) && r.X <= Math.Max(p.X, q.X)
        && r.Y >= Math.Min(p.Y, q.Y) && r.Y <= Math.Max(p.Y, q.Y);
}
