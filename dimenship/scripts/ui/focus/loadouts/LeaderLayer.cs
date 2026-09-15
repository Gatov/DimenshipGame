using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The stage's leader lines and connector dots, drawn between the frame art and the boxes. A
/// drawing layer and nothing else — it takes no input.
/// <para>
/// The selected socket's line, dot and ring are <see cref="ShellPalette.Accent"/>, in step with its
/// box's border: one selection signal carried by three elements at once, never a second colour.
/// It is drawn last so no guide crosses over it.
/// </para>
/// </summary>
public sealed partial class LeaderLayer : Control
{
    private const float LineWidth = 1f;
    private const float DotRadius = 3f;
    private const float RingRadius = 7f;
    private const int RingSegments = 24;

    private readonly List<Vector2[]> _leaders = new();
    private int _selected = -1;

    public LeaderLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Index <c>i</c> of <paramref name="leaders"/> belongs to socket <c>i</c>; its last point is the anchor.</summary>
    public void Show(IReadOnlyList<IReadOnlyList<(int X, int Y)>> leaders, int selected)
    {
        _leaders.Clear();

        foreach (var leader in leaders)
        {
            _leaders.Add(leader.Select(point => new Vector2(point.X, point.Y)).ToArray());
        }

        _selected = selected;
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var i = 0; i < _leaders.Count; i++)
        {
            if (i != _selected)
            {
                DrawLeader(_leaders[i], selected: false);
            }
        }

        if (_selected >= 0 && _selected < _leaders.Count)
        {
            DrawLeader(_leaders[_selected], selected: true);
        }
    }

    private void DrawLeader(Vector2[] points, bool selected)
    {
        if (points.Length == 0)
        {
            return;
        }

        if (points.Length > 1)
        {
            DrawPolyline(
                points, selected ? ShellPalette.Accent : ShellPalette.ProjectionGuide, LineWidth, antialiased: true);
        }

        var anchor = points[^1];
        DrawCircle(anchor, DotRadius, selected ? ShellPalette.Accent : ShellPalette.Projection);

        if (selected)
        {
            DrawArc(anchor, RingRadius, 0f, Mathf.Tau, RingSegments, ShellPalette.Accent, LineWidth, antialiased: true);
        }
    }
}
