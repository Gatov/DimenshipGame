using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One socket of the selected template: what it accepts, what is in it, and what that part
/// contributes. It is a click target, a drag source when filled, and a drop target when the kinds
/// match.
/// <para>
/// An empty socket says so in words and keeps its full height. GDD §5.10 makes an empty socket a
/// legal state — the machine runs at its unequipped rating — so it must read as a socket with
/// nothing in it, not as a gap in the list.
/// </para>
/// </summary>
public sealed partial class SocketRow : PanelContainer
{
    /// <summary>Tall enough for the icon, the kind, the part and its contribution on one line.</summary>
    private const int RowHeight = 34;

    private const int IndicatorWidth = 2;

    private readonly int _index;
    private readonly SocketKind _kind;
    private readonly PartDef? _fitted;
    private readonly bool _selected;

    private bool _hinted;

    public SocketRow(int index, SocketKind kind, PartDef? fitted, bool selected)
    {
        _index = index;
        _kind = kind;
        _fitted = fitted;
        _selected = selected;

        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, RowHeight);
    }

    public Action<int>? Selected { get; set; }

    /// <summary>Raised with the payload and this socket's index when a drop lands.</summary>
    public Action<LoadoutDragData, int>? Dropped { get; set; }

    public Action<int>? Cleared { get; set; }

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Card(_selected));

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(row);

        row.AddChild(new IconSlot(
            "status", LoadoutCatalog.Icon(_kind), IconSlot.RowSize,
            _fitted is null ? ShellPalette.TextFaint : ShellPalette.Accent));

        var kind = new Label
        {
            Text = LoadoutCatalog.Label(_kind).ToUpperInvariant(),
            CustomMinimumSize = new Vector2(96, 0),
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        kind.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        kind.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(kind);

        var part = new Label
        {
            Text = _fitted?.Label ?? "EMPTY — RUNNING AT FRAME BASELINE",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        part.AddThemeColorOverride(
            "font_color", _fitted is null ? ShellPalette.TextFaint : ShellPalette.TextTitle);
        part.AddThemeFontSizeOverride(
            "font_size", _fitted is null ? ShellPalette.FontMicro : ShellPalette.FontBody);
        row.AddChild(part);

        if (_fitted is null)
        {
            return;
        }

        var contribution = new Label
        {
            Text = StatFormat.Summary(_fitted.Delta),
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        contribution.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        contribution.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(contribution);

        var clear = new Button { Text = "✕", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(clear);
        clear.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        clear.Pressed += () => Cleared?.Invoke(_index);
        row.AddChild(clear);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Selected?.Invoke(_index);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_fitted is null)
        {
            return default;
        }

        var payload = new LoadoutDragData { Part = _fitted, FromSocket = _index };
        SetDragPreview(Preview(_fitted.Label));

        // Through the implicit GodotObject conversion, as BlockView does: Variant.From carries a
        // generic constraint the payload has no reason to satisfy.
        return payload;
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
        var payload = Payload(data);

        _hinted = false;
        QueueRedraw();

        if (payload is not null && payload.Fits(_kind))
        {
            Dropped?.Invoke(payload, _index);
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

    /// <summary>
    /// A kind mismatch simply does not highlight. Reporting it afterwards would be a validation
    /// message for a state the editor can refuse to enter, which is a message the player learns to
    /// ignore.
    /// </summary>
    private bool Accepts(Variant data) => Payload(data) is { } payload && payload.Fits(_kind);

    private static LoadoutDragData? Payload(Variant data) =>
        data.VariantType == Variant.Type.Object ? data.AsGodotObject() as LoadoutDragData : null;

    private static Control Preview(string label)
    {
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: true));

        var text = new Label { Text = label };
        text.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        text.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        chip.AddChild(text);

        return chip;
    }
}
