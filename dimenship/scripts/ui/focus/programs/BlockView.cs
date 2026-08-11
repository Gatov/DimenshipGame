using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One row of the rule ladder: a keyword cap, the slots its kind declares, and the drag and drop
/// behaviour that makes the row authorable.
/// <para>
/// A block is dragged <b>by its keyword cap or its padding</b>, never by a slot — a slot's own
/// control takes the press so that clicking a dropdown opens it instead of starting a drag. That is
/// a deliberate division and one of the things this mock exists for the player to judge.
/// </para>
/// <para>
/// Selection and focus are both a border colour change, following <see cref="NodeCard"/>: no glow,
/// no growth, because growth would shift the row's hit area and reflow every row beneath it.
/// </para>
/// </summary>
public sealed partial class BlockView : PanelContainer
{
    /// <summary>The spec's row height. A block is one row, always.</summary>
    public const int RowHeight = 28;

    /// <summary>The drop line, and the border of a condition slot about to be replaced.</summary>
    private const int IndicatorWidth = 2;

    private readonly BlockKind _kind;
    private readonly string _keyword;
    private readonly IReadOnlyList<SlotSpec> _slots;
    private readonly IReadOnlyList<SlotValue> _values;
    private readonly string? _note;

    private StyleBoxFlat? _resting;
    private StyleBoxFlat? _highlighted;
    private DropHint _hint;
    private bool _selected;
    private bool _focused;

    public BlockView(
        BlockKind kind,
        string keyword,
        IReadOnlyList<SlotSpec> slots,
        IReadOnlyList<SlotValue> values,
        string? note = null)
    {
        _kind = kind;
        _keyword = keyword;
        _slots = slots;
        _values = values;
        _note = note;

        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(0, RowHeight);
    }

    /// <summary>Where a drop would land relative to this block.</summary>
    private enum DropHint
    {
        None,
        Before,
        After,
        Replace,
    }

    /// <summary>The statement this row stands for. Null on a row that is not itself a statement.</summary>
    public StatementDraft? Statement { get; set; }

    /// <summary>The list <see cref="Statement"/> sits in. Set together with it, or not at all.</summary>
    public List<StatementDraft>? Body { get; set; }

    /// <summary>
    /// Set on a row that holds a condition: a rule's <c>WHEN</c>, or a branch arm's <c>IF</c>.
    /// Such a row takes a condition and nothing else — an action dropped on it is refused rather
    /// than guessed at, because a drop that silently lands somewhere other than where the player
    /// aimed is worse than one that does not land.
    /// </summary>
    public Action<ConditionDraft>? OnCondition { get; set; }

    /// <summary>Called with the payload, the list it should land in, and the index it lands at.</summary>
    public Action<BlockDragData, List<StatementDraft>, int>? OnDropped { get; set; }

    /// <summary>Called after a slot has been written, so the canvas can record an undo entry.</summary>
    public Action? OnEdited { get; set; }

    public Action<BlockView>? OnSelected { get; set; }

    /// <summary>True when this row can be picked up. A bare <c>ELSE</c> header cannot.</summary>
    public bool Draggable => Statement is not null && Body is not null;

    public override void _Ready()
    {
        _resting = ShellTheme.Block(ShellPalette.BgPanel, highlighted: false);
        _highlighted = ShellTheme.Block(ShellPalette.BgPanel, highlighted: true);

        // Applied rather than assumed resting: a block may already have been marked selected by the
        // canvas that built it, before it ever entered the tree.
        ApplyChrome();

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(row);

        if (_keyword.Length > 0)
        {
            row.AddChild(Cap());
        }

        for (var i = 0; i < _slots.Count && i < _values.Count; i++)
        {
            row.AddChild(SlotControl.Build(_slots[i], _values[i], () => OnEdited?.Invoke()));
        }

        if (_note is not null)
        {
            row.AddChild(Note(_note));
        }

        FocusEntered += () => SetFocused(true);
        FocusExited += () => SetFocused(false);
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected)
        {
            return;
        }

        _selected = selected;
        ApplyChrome();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                GrabFocus();
                OnSelected?.Invoke(this);
                AcceptEvent();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }:
                OnSelected?.Invoke(this);
                AcceptEvent();
                break;
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!Draggable)
        {
            return default;
        }

        var payload = new BlockDragData
        {
            Statement = Statement,
            Body = Body,
            Label = _keyword.Length > 0 ? _keyword : "BLOCK",
        };

        SetDragPreview(Preview(payload.Label));

        // Through the implicit GodotObject conversion. Variant.From carries a generic constraint
        // the payload has no reason to satisfy.
        return payload;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var payload = Payload(data);
        var hint = Resolve(payload, atPosition);

        if (hint != _hint)
        {
            _hint = hint;
            QueueRedraw();
        }

        return hint != DropHint.None;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var payload = Payload(data);
        var hint = Resolve(payload, atPosition);

        ClearHint();

        switch (hint)
        {
            case DropHint.Replace when payload is not null:
                OnCondition?.Invoke(payload.ToCondition());
                break;

            case DropHint.Before or DropHint.After when payload is not null:
                var index = Body!.IndexOf(Statement!) + (hint == DropHint.After ? 1 : 0);
                OnDropped?.Invoke(payload, Body!, index);
                break;
        }
    }

    /// <summary>
    /// Godot broadcasts the end of a drag to every control, which is the only reliable moment to
    /// drop a hint that was left painted on a row the pointer never returned to.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd || what == NotificationMouseExit)
        {
            ClearHint();
        }
    }

    public override void _Draw()
    {
        switch (_hint)
        {
            case DropHint.Replace:
                DrawRect(
                    new Rect2(Vector2.Zero, Size), ShellPalette.Accent,
                    filled: false, width: IndicatorWidth);
                break;

            case DropHint.Before:
                DrawRect(
                    new Rect2(0, 0, Size.X, IndicatorWidth), ShellPalette.Accent);
                break;

            case DropHint.After:
                DrawRect(
                    new Rect2(0, Size.Y - IndicatorWidth, Size.X, IndicatorWidth),
                    ShellPalette.Accent);
                break;
        }
    }

    /// <summary>
    /// The category colour, and the word that says the same thing. The colour is redundant by
    /// construction — a player who cannot tell <c>BlockAction</c> from <c>BlockBranch</c> still
    /// reads <c>HOLD</c> and <c>ELSE IF</c> — which is what the no-colour-alone rule requires.
    /// </summary>
    private Control Cap()
    {
        var cap = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", ShellTheme.Block(Fill(_kind), highlighted: false));

        var label = new Label
        {
            Text = _keyword.ToUpperInvariant(),
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        cap.AddChild(label);

        return cap;
    }

    private static Color Fill(BlockKind kind) => kind switch
    {
        BlockKind.Control => ShellPalette.BlockControl,
        BlockKind.Branch => ShellPalette.BlockBranch,
        BlockKind.Action => ShellPalette.BlockAction,
        _ => ShellPalette.BlockCondition,
    };

    private static Label Note(string text)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
    }

    private static Control Preview(string label)
    {
        var preview = new PanelContainer { Modulate = new Color(1, 1, 1, 0.8f) };
        preview.AddThemeStyleboxOverride(
            "panel", ShellTheme.Block(ShellPalette.BgPanel, highlighted: true));

        var text = new Label { Text = label.ToUpperInvariant() };
        text.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        preview.AddChild(text);

        return preview;
    }

    /// <summary>
    /// Null for anything that is not a block payload, which is how a drag from outside the editor
    /// is refused rather than crashed on.
    /// </summary>
    private static BlockDragData? Payload(Variant data) =>
        data.VariantType == Variant.Type.Object
            ? data.AsGodotObject() as BlockDragData
            : null;

    /// <summary>
    /// Where this payload would land. A condition row takes conditions only; a statement row takes
    /// anything that can be a statement, before it or after it by which half the pointer is in.
    /// </summary>
    private DropHint Resolve(BlockDragData? payload, Vector2 atPosition)
    {
        if (payload is null)
        {
            return DropHint.None;
        }

        if (OnCondition is not null)
        {
            return payload.IsCondition ? DropHint.Replace : DropHint.None;
        }

        if (Statement is null || Body is null || !payload.IsStatement)
        {
            return DropHint.None;
        }

        return payload.WouldOrphan(Body)
            ? DropHint.None
            : atPosition.Y > Size.Y / 2f ? DropHint.After : DropHint.Before;
    }

    private void ClearHint()
    {
        if (_hint == DropHint.None)
        {
            return;
        }

        _hint = DropHint.None;
        QueueRedraw();
    }

    private void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        ApplyChrome();
    }

    /// <summary>
    /// The override, not the stylebox, is what has to be deduped: it allocates. Silent before
    /// <see cref="_Ready"/>, which builds the two styleboxes — the state is kept and applied there.
    /// </summary>
    private void ApplyChrome()
    {
        if (_resting is null || _highlighted is null)
        {
            return;
        }

        AddThemeStyleboxOverride("panel", _selected || _focused ? _highlighted : _resting);
    }
}
