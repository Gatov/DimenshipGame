using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The tail of a statement body: a place to drop a block that appends it, and — when the body is
/// empty — the only thing standing where its blocks would be.
/// <para>
/// Every body carries one, whether or not it is empty. Without it, an empty branch would be a
/// target the player can see and cannot hit, and appending to a full body would mean aiming at the
/// bottom half of the last block, which is a smaller target than the thing it does deserves.
/// </para>
/// </summary>
public sealed partial class BlockDropStrip : PanelContainer
{
    /// <summary>An empty body reads as a row; a filled one only needs a landing edge.</summary>
    private const int FilledHeight = 10;

    private const int IndicatorWidth = 2;

    private readonly List<StatementDraft> _target;
    private readonly bool _empty;

    private bool _hinted;

    public BlockDropStrip(List<StatementDraft> target, bool empty)
    {
        _target = target;
        _empty = empty;

        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, empty ? BlockView.RowHeight : FilledHeight);
    }

    public Action<BlockDragData, List<StatementDraft>, int>? OnDropped { get; set; }

    public override void _Ready()
    {
        AddThemeStyleboxOverride(
            "panel", ShellTheme.Block(ShellPalette.BgBase, highlighted: false));

        if (!_empty)
        {
            return;
        }

        // Said in words, not implied by an empty space: a branch with nothing in it commands
        // nothing, and the player should be able to tell that from the canvas alone.
        var label = new Label
        {
            Text = "EMPTY — DROP A BLOCK HERE",
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        AddChild(label);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var accepts = Accepts(data);

        if (accepts != _hinted)
        {
            _hinted = accepts;
            QueueRedraw();
        }

        return accepts;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var payload = data.VariantType == Variant.Type.Object
            ? data.AsGodotObject() as BlockDragData
            : null;

        _hinted = false;
        QueueRedraw();

        if (payload is not null && payload.IsStatement && !payload.WouldOrphan(_target))
        {
            OnDropped?.Invoke(payload, _target, _target.Count);
        }
    }

    public override void _Notification(int what)
    {
        if ((what != NotificationDragEnd && what != NotificationMouseExit) || !_hinted)
        {
            return;
        }

        _hinted = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_hinted)
        {
            DrawRect(
                new Rect2(Vector2.Zero, Size), ShellPalette.Accent,
                filled: false, width: IndicatorWidth);
        }
    }

    private bool Accepts(Variant data)
    {
        var payload = data.VariantType == Variant.Type.Object
            ? data.AsGodotObject() as BlockDragData
            : null;

        return payload is not null && payload.IsStatement && !payload.WouldOrphan(_target);
    }
}
