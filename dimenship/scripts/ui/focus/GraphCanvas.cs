using System;
using System.Collections.Generic;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Draws the graph's edges and hosts its node cards.
/// <para>
/// A plain <see cref="Control"/>, deliberately not a container: a container lays out every
/// <see cref="Control"/> child, which would fight the explicit positions
/// <see cref="GraphGeometry.CellRect"/> gives each card. It is a <see cref="Control"/> rather than
/// a <c>Node2D</c> — which <c>FrostPane</c> chose for the same reason — because cards must be
/// focusable and take GUI input, and a <c>Node2D</c>'s children cannot be.
/// </para>
/// <para>
/// It takes no input itself. Cards, being deeper in the tree, get first refusal on a click, and
/// everything they do not consume falls through to the viewport that owns pan, zoom and edge
/// hit-testing.
/// </para>
/// </summary>
public sealed partial class GraphCanvas : Control
{
    /// <summary>
    /// One drawn route. <paramref name="BackId"/> is the opposing line when two routes join the
    /// same pair of storages: they are merged into one double-headed edge rather than drawn as
    /// two lines a few pixels apart, because that is what the player means by "the link".
    /// <paramref name="Band"/> colours the shared polyline and mid-edge label — the worse of the
    /// two legs when both exist — while <paramref name="ForwardBand"/> and <paramref name="BackBand"/>
    /// colour only their own arrowheads, so a busy A→B leg does not light the idle B→A tip.
    /// <paramref name="ForwardFillPermille"/> and <paramref name="BackFillPermille"/> are how much
    /// of each direction's belt is loaded, and they are per side for the same reason the bands are:
    /// the two directions are two conveyors, and one of them can be stuck full while the other runs
    /// empty. There is deliberately no merged figure — the worse-of-two rule that suits a shared
    /// stroke would report a jam on the side that has none.
    /// <paramref name="Built"/> is false when the route it draws — or the back leg it is merged
    /// with — is not: an authored interconnect nothing has commissioned yet. An unbuilt route
    /// never carries live throughput, so its <paramref name="Band"/> is always <see
    /// cref="FlowBand.Idle"/>, and without this flag it would be indistinguishable from a built
    /// line nothing happens to be using right now — two very different states on the graph.
    /// </summary>
    public sealed record Edge(
        string Id,
        string? BackId,
        IReadOnlyList<(int X, int Y)> Points,
        FlowBand Band,
        FlowBand ForwardBand,
        FlowBand? BackBand,
        long ForwardFillPermille,
        long BackFillPermille,
        string Code,
        bool Built);

    private const float LineWidth = 2f;

    /// <summary>A selected edge is thicker, not haloed and not recoloured: its colour is a live
    /// reading, and selection must not overwrite what the player selected it to read.</summary>
    private const float SelectionWidth = 3f;

    private const float ArrowLength = 8f;

    /// <summary>How far back from a corner the arc starts, and how far past it the arc ends.</summary>
    private const float ElbowRadius = 6f;

    /// <summary>How far back from an arrowhead its belt-fill reading sits, and how far to the
    /// side. Clear of the tip and clear of the stroke, so a merged pair's two readings never
    /// collide with each other or with the line between them.</summary>
    private const float FillLabelBack = 20f;

    private const float FillLabelSide = 11f;

    /// <summary>Segments per elbow arc. Eight matches the corner detail the styleboxes use.</summary>
    private const int ElbowSegments = 8;

    private IReadOnlyList<Edge> _edges = Array.Empty<Edge>();
    private string? _selected;

    public IReadOnlyList<Edge> Edges => _edges;

    public override void _Ready()
    {
        // Cards are children and must receive clicks; the canvas itself never does.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetEdges(IReadOnlyList<Edge> edges)
    {
        _edges = edges;
        QueueRedraw();
    }

    public void SetSelected(string? edgeId)
    {
        if (_selected == edgeId)
        {
            return;
        }

        _selected = edgeId;
        QueueRedraw();
    }

    public static Color ColorOf(FlowBand band) => band switch
    {
        FlowBand.Low => ShellPalette.FlowLow,
        FlowBand.Normal => ShellPalette.FlowNormal,
        FlowBand.High => ShellPalette.FlowHigh,
        FlowBand.Blocked => ShellPalette.FlowBlocked,
        _ => ShellPalette.FlowIdle,
    };

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();

        foreach (var edge in _edges)
        {
            if (edge.Points.Count < 2)
            {
                continue;
            }

            var corners = new Vector2[edge.Points.Count];
            for (var i = 0; i < edge.Points.Count; i++)
            {
                corners[i] = new Vector2(edge.Points[i].X, edge.Points[i].Y);
            }

            // Rounded at draw time only. GraphGeometry keeps returning the straight polyline and
            // the hit test keeps measuring it, because that is what its unit tests cover and an
            // arc in the hit path would buy nothing a click can feel.
            var points = Rounded(corners);

            // Dimmed the same way an unbuilt card is: alpha only, through the one shared modulate
            // rather than a second faded colour ramp for edges.
            Color Tint(FlowBand band)
            {
                var c = ColorOf(band);
                return edge.Built ? c : c * ShellPalette.UnbuiltModulate;
            }

            var color = Tint(edge.Band);
            var selected = edge.Id == _selected ||
                           (edge.BackId is not null && edge.BackId == _selected);

            // A route nothing has commissioned is dashed, in the vocabulary an unbuilt card already
            // wears: the two factory interconnects ship unbuilt beside unbuilt slots, and a language
            // that covered the cards but not the lines between them would be saying two different
            // things about one condition. Width is untouched — a dashed edge still thickens when it
            // is selected, because selection is about which line the player is reading, not what
            // state it is in.
            if (edge.Built)
            {
                DrawPolyline(points, color, selected ? SelectionWidth : LineWidth, antialiased: true);
            }
            else
            {
                ShellTheme.DrawDashedPolyline(
                    this, points, color, selected ? SelectionWidth : LineWidth);
            }

            Arrow(corners[^2], corners[^1], Tint(edge.ForwardBand));
            FillLabel(font, corners[^2], corners[^1], edge.ForwardFillPermille, Tint(edge.ForwardBand));
            if (edge.BackBand is { } backBand)
            {
                Arrow(corners[1], corners[0], Tint(backBand));
                FillLabel(font, corners[1], corners[0], edge.BackFillPermille, Tint(backBand));
            }

            // Colour never carries meaning alone. An edge has no card to put a status line on, so
            // its band is written along it.
            var mid = corners[corners.Length / 2];
            DrawString(
                font,
                mid + new Vector2(ShellPalette.SpaceSm, -ShellPalette.SpaceSm),
                edge.Code,
                HorizontalAlignment.Left,
                width: -1,
                fontSize: ShellPalette.FontMicro,
                modulate: color);
        }
    }

    /// <summary>
    /// The same polyline with a quarter-arc at every interior corner. Each corner is replaced by a
    /// quadratic curve from a point <see cref="ElbowRadius"/> back along the incoming segment to a
    /// point the same distance along the outgoing one, with the corner itself as the control point.
    /// A corner whose shorter neighbouring segment cannot spare the radius keeps its hard turn —
    /// an arc wider than the segment it rounds would bow the line away from where the edge runs.
    /// </summary>
    private static Vector2[] Rounded(IReadOnlyList<Vector2> corners)
    {
        var points = new List<Vector2>(corners.Count + (ElbowSegments * (corners.Count - 2)))
        {
            corners[0],
        };

        for (var i = 1; i < corners.Count - 1; i++)
        {
            var corner = corners[i];
            var into = corners[i - 1] - corner;
            var outOf = corners[i + 1] - corner;
            var radius = Mathf.Min(ElbowRadius, Mathf.Min(into.Length(), outOf.Length()) / 2f);

            if (radius <= 0f)
            {
                points.Add(corner);
                continue;
            }

            var start = corner + (into.Normalized() * radius);
            var end = corner + (outOf.Normalized() * radius);

            for (var step = 0; step <= ElbowSegments; step++)
            {
                var t = (float)step / ElbowSegments;
                points.Add(start.Lerp(corner, t).Lerp(corner.Lerp(end, t), t));
            }
        }

        points.Add(corners[^1]);
        return points.ToArray();
    }

    /// <summary>
    /// How full one direction's belt is, written beside the arrowhead that direction points at.
    /// Beside the tip rather than in the shared mid-edge label, because a merged edge is two belts
    /// and they fill independently — which is the whole reason the reading is per side.
    /// <para>
    /// A belt carrying anything at all reads at least one percent. Rounding a live load down to
    /// nothing would say the line is empty at the exact moment it has started to back up.
    /// </para>
    /// </summary>
    private void FillLabel(Font font, Vector2 from, Vector2 to, long permille, Color color)
    {
        if (permille <= 0)
        {
            return;
        }

        var direction = (to - from).Normalized();
        if (direction == Vector2.Zero)
        {
            return;
        }

        var side = new Vector2(-direction.Y, direction.X) * FillLabelSide;

        DrawString(
            font,
            to - (direction * FillLabelBack) + side,
            $"{Math.Max(1, permille / 10)}%",
            HorizontalAlignment.Left,
            width: -1,
            fontSize: ShellPalette.FontMicro,
            modulate: color);
    }

    private void Arrow(Vector2 from, Vector2 to, Color color)
    {
        var direction = (to - from).Normalized();
        if (direction == Vector2.Zero)
        {
            return;
        }

        var back = to - (direction * ArrowLength);
        var side = new Vector2(-direction.Y, direction.X) * (ArrowLength * 0.45f);

        DrawPolygon(
            new[] { to, back + side, back - side },
            new[] { color, color, color });
    }
}
