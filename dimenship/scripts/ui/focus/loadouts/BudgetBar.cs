using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The power budget as a bar: its length is supply, its solid fill is draw. A preview hatches the
/// difference between the current draw and the candidate's, in whichever direction it goes, and a
/// preview that changes supply marks where the new end would be.
/// <para>
/// Over budget, the fill runs to the supply mark in <see cref="ShellPalette.StateFault"/> and a
/// tick stands at that mark. The words beside the bar — <c>OVER BUDGET BY 120</c> — carry the
/// state; the colour only agrees with them.
/// </para>
/// <para>
/// Drawn rather than built from a <c>ProgressBar</c>, because a progress bar has one value and this
/// has up to four, and a hatched segment is not a stylebox.
/// </para>
/// </summary>
public sealed partial class BudgetBar : Control
{
    private const float BarHeight = 8f;
    private const float Mark = 3f;
    private const float HatchStep = 4f;

    private long _supply;
    private long _draw;
    private long? _previewSupply;
    private long? _previewDraw;

    public BudgetBar()
    {
        CustomMinimumSize = new Vector2(160, BarHeight + (2 * Mark));
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(long supply, long draw, long? previewSupply, long? previewDraw)
    {
        _supply = supply;
        _draw = draw;
        _previewSupply = previewSupply;
        _previewDraw = previewDraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var width = Size.X;
        var trough = new Rect2(0, Mark, width, BarHeight);

        DrawRect(trough, ShellPalette.BgBase);
        DrawRect(trough, ShellPalette.Border, filled: false, width: 1f);

        var capacity = Math.Max(_supply, _previewSupply ?? 0);

        if (capacity <= 0)
        {
            return;
        }

        float X(long value) => Math.Clamp(value, 0, capacity) * width / capacity;

        var over = _draw > _supply;
        DrawRect(
            new Rect2(0, Mark, X(Math.Min(_draw, _supply)), BarHeight),
            over ? ShellPalette.StateFault : ShellPalette.Accent);

        if (over)
        {
            var at = X(_supply);
            DrawLine(new Vector2(at, 0), new Vector2(at, BarHeight + (2 * Mark)), ShellPalette.StateFault, 2f);
        }

        if (_previewDraw is { } previewDraw && previewDraw != _draw)
        {
            Hatch(X(Math.Min(_draw, previewDraw)), X(Math.Max(_draw, previewDraw)));
        }

        if (_previewSupply is { } previewSupply && previewSupply != _supply)
        {
            var at = X(previewSupply);
            DrawLine(new Vector2(at, 0), new Vector2(at, BarHeight + (2 * Mark)), ShellPalette.TextTitle, 1f);
        }
    }

    private void Hatch(float from, float to)
    {
        if (to - from < 1f)
        {
            return;
        }

        DrawRect(new Rect2(from, Mark, to - from, BarHeight), ShellPalette.TextTitle, filled: false, width: 1f);

        for (var x = from; x < to; x += HatchStep)
        {
            var end = Math.Min(x + BarHeight, to);
            DrawLine(
                new Vector2(x, Mark + BarHeight),
                new Vector2(end, Mark + BarHeight - (end - x)),
                ShellPalette.TextTitle,
                1f);
        }
    }
}
