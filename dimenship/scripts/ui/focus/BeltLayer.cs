using System;
using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The cargo on the base graph's lines, drawn where the kernel says it is and moving between
/// ticks. One marker per occupied belt slot, gliding one slot towards the head each tick.
/// <para>
/// A reading, not decoration. Since the conveyor work a parcel really is somewhere along its line
/// (<see cref="BeltParcel.Position"/>), so a marker a third of the way along is a third of the way
/// across. A frozen belt's markers stand still where its cargo stopped while every working line
/// flows, which is a jam you can see without reading a colour.
/// </para>
/// <para>
/// Its own node, the first child of <see cref="GraphCanvas"/>, so it draws over the routes and
/// under every card, and so it can redraw every frame while time runs without redrawing the
/// routes, the labels and the cards with it. Markers are small and quiet by default, because the
/// graph's contrast order is cards, then slots, then wiring, and motion alone is enough to pull the
/// eye. A line's item icons appear only once the player is looking closely: on the selected edge,
/// or at the closer zoom steps (<see cref="GraphCanvas.SetDetailed"/>).
/// </para>
/// </summary>
public sealed partial class BeltLayer : Control
{
    /// <summary>
    /// One direction of one drawn edge: the path it travels, source end first, and what is on it.
    /// <paramref name="Frozen"/> holds the markers at their slots, because a frozen belt advances
    /// nothing and gliding them towards a head that cannot unload would show cargo moving that is
    /// not. <paramref name="Lane"/> is how far to the right of travel the markers ride: zero on a
    /// one-way edge, and a few pixels on a merged pair, so the two directions pass each other
    /// rather than run through each other.
    /// </summary>
    public sealed record Run(
        Vector2[] Path,
        IReadOnlyList<BeltParcel> Parcels,
        long LengthTicks,
        bool Frozen,
        Color Colour,
        bool Detailed,
        float Lane);

    /// <summary>How far in from the source card the first slot's stretch begins.</summary>
    private const float SourceInset = 4f;

    /// <summary>How far short of the destination card the head's stretch ends: clear of the
    /// arrowhead, which is <see cref="GraphCanvas"/>'s eight pixels.</summary>
    private const float HeadInset = 11f;

    /// <summary>How many pixels a parcel takes to fade out as it reaches its destination.</summary>
    private const float EdgeFade = 8f;

    private const float PipRadius = 2.5f;
    private const float PipRing = 1.2f;

    private const float IconSize = 14f;
    private const float IconDisc = 9f;

    private readonly Dictionary<ItemId, Texture2D?> _icons = new();

    private IReadOnlyList<Run> _runs = Array.Empty<Run>();
    private float[][] _lengths = Array.Empty<float[]>();
    private Func<float>? _phase;
    private float _drawnPhase = -1f;
    private bool _moving;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>Where the fraction of a tick comes from. Unbound, markers sit on their slots.</summary>
    public void BindPhase(Func<float> phase) => _phase = phase;

    public void SetRuns(IReadOnlyList<Run> runs)
    {
        _runs = runs;
        _lengths = new float[runs.Count][];
        _moving = false;

        for (var i = 0; i < runs.Count; i++)
        {
            _lengths[i] = Cumulative(runs[i].Path);
            _moving |= !runs[i].Frozen && runs[i].Parcels.Count > 0;
        }

        _drawnPhase = -1f;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Only a frame whose phase moved needs drawing, and only while something on a belt is free
        // to move. A quiet vessel, or a paused one, costs this node nothing per frame.
        if (!_moving)
        {
            return;
        }

        var phase = Phase();
        if (!Mathf.IsEqualApprox(phase, _drawnPhase))
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var phase = Phase();
        _drawnPhase = phase;

        for (var i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            var lengths = _lengths[i];
            if (run.Parcels.Count == 0 || run.LengthTicks <= 0 || run.Path.Length < 2)
            {
                continue;
            }

            var total = lengths[^1];
            var usable = total - SourceInset - HeadInset;
            if (usable <= 0f)
            {
                // A route too short to show travel along it still shows that something is on it.
                usable = total;
            }

            var start = usable == total ? 0f : SourceInset;
            var glide = run.Frozen ? 0f : phase;

            foreach (var parcel in run.Parcels)
            {
                // A parcel is in flight for exactly LengthTicks ticks, from the tick it is loaded
                // into the last slot to the tick it unloads from the head, so the whole route is
                // that many ticks of travel. Measured from the head: a parcel at slot p entered it
                // at p + 1 and has the tick's phase still to cover, so it starts the source end at
                // phase 0 on the tick it is loaded and reaches the destination at phase 1 on the
                // tick before it unloads. At phase 1 it is exactly where the next snapshot puts it,
                // so a tick boundary is never a jump. Placing it at its slot's middle instead lost
                // the first and last half slot, which on a two-tick line carrying one load at a
                // time read as cargo appearing and vanishing in the middle of the route.
                //
                // A frozen belt advances nothing, and its parcels wait at the head end of their
                // slots: the head parcel at the destination it cannot unload into. That is also
                // where a moving parcel's last frame left it, so a belt freezing is never a jump
                // either.
                var fromHead = run.Frozen ? parcel.Position : parcel.Position + 1f - glide;
                var along = Mathf.Clamp(1f - (fromHead / run.LengthTicks), 0f, 1f);
                var travelled = along * usable;

                // Out into the destination over a few pixels rather than popping. Not in from the
                // source: paused, a parcel loaded this tick sits exactly at the source end, and a
                // fade there would hide it. A frozen head stays at full strength at the destination,
                // because that is the cargo the player needs to see.
                var alpha = run.Frozen ? 1f : Mathf.Clamp((usable - travelled) / EdgeFade, 0f, 1f);

                if (alpha <= 0f)
                {
                    continue;
                }

                var (point, direction) = At(run.Path, lengths, start + travelled);
                point += new Vector2(-direction.Y, direction.X) * run.Lane;

                var colour = run.Colour;
                colour.A *= alpha;
                var ring = ShellPalette.BgBase;
                ring.A *= alpha;

                if (run.Detailed && Icon(parcel.Id) is { } icon)
                {
                    DrawCircle(point, IconDisc, ring);
                    DrawArc(point, IconDisc, 0f, Mathf.Tau, 24, colour, 1f, antialiased: true);
                    DrawTextureRect(
                        icon,
                        new Rect2(point - new Vector2(IconSize / 2f, IconSize / 2f), new Vector2(IconSize, IconSize)),
                        tile: false,
                        modulate: colour);
                }
                else
                {
                    DrawCircle(point, PipRadius + PipRing, ring);
                    DrawCircle(point, PipRadius, colour);
                }
            }
        }
    }

    private float Phase() => _phase is null ? 0f : Mathf.Clamp(_phase(), 0f, 1f);

    /// <summary>
    /// Remembered, missing ones included, so a frame never reloads a texture and an item with no
    /// icon is warned about once rather than sixty times a second. A missing icon draws as a pip.
    /// </summary>
    private Texture2D? Icon(ItemId item)
    {
        if (!_icons.TryGetValue(item, out var texture))
        {
            texture = IconSlot.Load("item", item.Value);
            _icons[item] = texture;
        }

        return texture;
    }

    private static float[] Cumulative(Vector2[] path)
    {
        var lengths = new float[path.Length];
        for (var i = 1; i < path.Length; i++)
        {
            lengths[i] = lengths[i - 1] + path[i - 1].DistanceTo(path[i]);
        }

        return lengths;
    }

    /// <summary>The point a given distance along the path, and the direction of travel there.</summary>
    private static (Vector2 Point, Vector2 Direction) At(Vector2[] path, float[] lengths, float distance)
    {
        for (var i = 1; i < path.Length; i++)
        {
            if (lengths[i] < distance && i < path.Length - 1)
            {
                continue;
            }

            var span = lengths[i] - lengths[i - 1];
            var t = span <= 0f ? 0f : Mathf.Clamp((distance - lengths[i - 1]) / span, 0f, 1f);
            var direction = (path[i] - path[i - 1]).Normalized();
            return (path[i - 1].Lerp(path[i], t), direction);
        }

        return (path[0], Vector2.Right);
    }
}
