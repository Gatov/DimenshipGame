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
    /// </summary>
    public sealed record Edge(
        string Id,
        string? BackId,
        IReadOnlyList<(int X, int Y)> Points,
        FlowBand Band,
        string Code);

    private const float LineWidth = 2f;
    private const float GlowWidth = 7f;
    private const float SelectionWidth = 6f;
    private const float ArrowLength = 9f;

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
            var points = new Vector2[edge.Points.Count];
            for (var i = 0; i < edge.Points.Count; i++)
            {
                points[i] = new Vector2(edge.Points[i].X, edge.Points[i].Y);
            }

            if (points.Length < 2)
            {
                continue;
            }

            var color = ColorOf(edge.Band);

            if (edge.Id == _selected || (edge.BackId is not null && edge.BackId == _selected))
            {
                DrawPolyline(points, ShellPalette.TextPrimary, SelectionWidth);
            }

            // An edge is a live measured value, so it may glow. The grid, the borders and the
            // labels may not, and none of them does.
            if (edge.Band != FlowBand.Idle)
            {
                DrawPolyline(points, color with { A = 0.22f }, GlowWidth);
            }

            DrawPolyline(points, color, LineWidth);

            Arrow(points[^2], points[^1], color);
            if (edge.BackId is not null)
            {
                Arrow(points[1], points[0], color);
            }

            // Colour never carries meaning alone. An edge has no card to put a status line on, so
            // its band is written along it.
            var mid = points[points.Length / 2];
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
