using System;
using System.Collections.Generic;
using Dimenship.Core.Simulation;
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
    /// <paramref name="EndsBuilt"/> is false when either card the edge joins is an unbuilt
    /// facility. Such an edge is dimmed like that card but keeps its solid stroke if the line
    /// itself is built: the dash says the line is not there yet, the alpha says it leads nowhere
    /// usable yet, and a built feed into an empty slot drawn at full strength read as a working
    /// route the moment the card beside it read as absent. That dimming holds only while the edge
    /// is quiet (<see cref="Faded"/>). A feed carrying a construction unit into the slot, or frozen
    /// with one aboard, is the one line doing real work there, and fading its load or its fault
    /// would hide the reading the player needs most.
    /// <paramref name="ForwardBelt"/> and <paramref name="BackBelt"/> are what each direction's
    /// belt carries, slot by slot, for <see cref="BeltLayer"/> to draw moving along it.
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
        bool Built,
        bool EndsBuilt,
        Belt ForwardBelt,
        Belt? BackBelt)
    {
        /// <summary>
        /// Whether the whole edge draws at <see cref="ShellPalette.UnbuiltModulate"/>. An unbuilt
        /// line always does. A built line into an unbuilt slot does only while both its directions
        /// are idle and their belts are empty: a belt still draining after its source ran dry
        /// loads nothing, and it is working all the same.
        /// </summary>
        public bool Faded =>
            !Built ||
            (!EndsBuilt &&
             Band == FlowBand.Idle &&
             ForwardFillPermille == 0 &&
             BackFillPermille == 0);
    }

    /// <summary>
    /// One direction's belt as the graph draws it. <paramref name="Frozen"/> is the kernel's
    /// blocked status, which for a line means exactly a head it cannot put down and nothing else,
    /// so it is the one reading that says the cargo is not moving.
    /// </summary>
    public sealed record Belt(IReadOnlyList<BeltParcel> Parcels, long LengthTicks, bool Frozen);

    /// <summary>
    /// How far to the right of travel every line's markers ride. Every line, not only a merged
    /// pair: a pair needs a lane per direction so the two pass rather than run through each other,
    /// and a one-way line riding its stroke beside a pair riding beside theirs read as two
    /// different kinds of cargo. One rule also says which way a paused pip is headed.
    /// </summary>
    private const float BeltLane = 3.5f;

    private const float LineWidth = 2f;

    /// <summary>
    /// An idle route is drawn finer than a working one. Width is the second channel, beside
    /// <see cref="ShellPalette.FlowIdle"/>, that keeps a vessel of a dozen quiet lines from
    /// out-drawing the cards: the wiring is there to be traced, not looked at first.
    /// </summary>
    private const float IdleLineWidth = 1.5f;

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

    private readonly BeltLayer _belts = new() { Name = "Belts" };

    private IReadOnlyList<Edge> _edges = Array.Empty<Edge>();
    private string? _selected;
    private bool _detailed;

    public IReadOnlyList<Edge> Edges => _edges;

    public override void _Ready()
    {
        // Cards are children and must receive clicks; the canvas itself never does.
        MouseFilter = MouseFilterEnum.Ignore;

        // The first child, so the cards added after it draw over the cargo, as they do the routes.
        AddChild(_belts);
    }

    /// <summary>Where the belt markers read the fraction of a tick from.</summary>
    public void BindTickPhase(Func<float> phase) => _belts.BindPhase(phase);

    public void SetEdges(IReadOnlyList<Edge> edges)
    {
        _edges = edges;
        QueueRedraw();
        RefreshBelts();
    }

    public void SetSelected(string? edgeId)
    {
        if (_selected == edgeId)
        {
            return;
        }

        _selected = edgeId;
        QueueRedraw();
        RefreshBelts();
    }

    /// <summary>
    /// Whether every line's cargo draws as item icons rather than pips: the closer zoom steps,
    /// where an icon is large enough to read and the graph is no longer the whole vessel at once.
    /// </summary>
    public void SetDetailed(bool detailed)
    {
        if (_detailed == detailed)
        {
            return;
        }

        _detailed = detailed;
        RefreshBelts();
    }

    private bool IsSelected(Edge edge) =>
        edge.Id == _selected || (edge.BackId is not null && edge.BackId == _selected);

    /// <summary>
    /// One run per direction per edge, on the same rounded path the stroke is drawn along. A
    /// direction whose belt is empty still gets none: there is nothing to draw on it.
    /// </summary>
    private void RefreshBelts()
    {
        var runs = new List<BeltLayer.Run>();

        foreach (var edge in _edges)
        {
            if (edge.Points.Count < 2 ||
                (edge.ForwardBelt.Parcels.Count == 0 && (edge.BackBelt?.Parcels.Count ?? 0) == 0))
            {
                continue;
            }

            var path = Rounded(Corners(edge));
            var detailed = _detailed || IsSelected(edge);

            if (edge.ForwardBelt.Parcels.Count > 0)
            {
                runs.Add(Run(edge, path, edge.ForwardBelt, edge.ForwardBand, detailed, BeltLane));
            }

            if (edge.BackBelt is { Parcels.Count: > 0 } back && edge.BackBand is { } backBand)
            {
                var reversed = (Vector2[])path.Clone();
                Array.Reverse(reversed);
                runs.Add(Run(edge, reversed, back, backBand, detailed, BeltLane));
            }
        }

        _belts.SetRuns(runs);
    }

    /// <summary>
    /// A marker takes its direction's band colour, like the arrowhead it travels towards. A belt
    /// draining after its source ran dry loads nothing and so bands idle, but its cargo is moving
    /// all the same, and drawing it in the idle colour would bury the one thing moving on the line.
    /// </summary>
    private static BeltLayer.Run Run(Edge edge, Vector2[] path, Belt belt, FlowBand band, bool detailed, float lane)
    {
        var colour = ColorOf(band == FlowBand.Idle ? FlowBand.Low : band);
        if (edge.Faded)
        {
            colour *= ShellPalette.UnbuiltModulate;
        }

        return new BeltLayer.Run(path, belt.Parcels, belt.LengthTicks, belt.Frozen, colour, detailed, lane);
    }

    private static Vector2[] Corners(Edge edge)
    {
        var corners = new Vector2[edge.Points.Count];
        for (var i = 0; i < edge.Points.Count; i++)
        {
            corners[i] = new Vector2(edge.Points[i].X, edge.Points[i].Y);
        }

        return corners;
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

            var corners = Corners(edge);

            // Rounded at draw time only. GraphGeometry keeps returning the straight polyline and
            // the hit test keeps measuring it, because that is what its unit tests cover and an
            // arc in the hit path would buy nothing a click can feel.
            var points = Rounded(corners);

            // Dimmed the same way an unbuilt card is: alpha only, through the one shared modulate
            // rather than a second faded colour ramp for edges.
            Color Tint(FlowBand band)
            {
                var c = ColorOf(band);
                return edge.Faded ? c * ShellPalette.UnbuiltModulate : c;
            }

            var color = Tint(edge.Band);
            var selected = IsSelected(edge);

            // A route nothing has commissioned is dashed, in the vocabulary an unbuilt card already
            // wears: the two factory interconnects ship unbuilt beside unbuilt slots, and a language
            // that covered the cards but not the lines between them would be saying two different
            // things about one condition. Width is untouched — a dashed edge still thickens when it
            // is selected, because selection is about which line the player is reading, not what
            // state it is in.
            var idle = edge.Band == FlowBand.Idle;
            var width = selected ? SelectionWidth : idle ? IdleLineWidth : LineWidth;

            if (edge.Built)
            {
                DrawPolyline(points, color, width, antialiased: true);
            }
            else
            {
                ShellTheme.DrawDashedPolyline(this, points, color, width);
            }

            Arrow(corners[^2], corners[^1], Tint(edge.ForwardBand));
            FillLabel(font, corners[^2], corners[^1], edge.ForwardFillPermille, Tint(edge.ForwardBand));
            if (edge.BackBand is { } backBand)
            {
                Arrow(corners[1], corners[0], Tint(backBand));
                FillLabel(font, corners[1], corners[0], edge.BackFillPermille, Tint(backBand));
            }

            // Colour never carries meaning alone. An edge has no card to put a status line on, so
            // its band is written along it — except IDLE, which is written nowhere. Idle is the
            // resting state of every line on a quiet vessel, so the word said nothing a dozen times
            // over and made the wiring the most-labelled thing on screen. The colour still is not
            // alone: no word, a fine stroke and the faintest band are three signals, and any line
            // that takes something on gains its word on the same tick. The legend names it.
            if (idle && !selected)
            {
                continue;
            }

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
