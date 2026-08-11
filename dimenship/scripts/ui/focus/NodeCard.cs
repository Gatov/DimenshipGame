using System;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The chrome every graph node shares: a bordered frame, an identifier badge over its corner, an
/// icon, a title, a caption-and-value line, a selection highlight and a hit area. Subclasses supply
/// the body and say how to read themselves out of a snapshot.
/// <para>
/// A plain <see cref="Control"/> holding a <see cref="PanelContainer"/> rather than being one. A
/// container lays out every <see cref="Control"/> child to fill it, badge included, and the badge
/// has to hang off the frame's top-left corner and overlap its border. A non-container parent lays
/// out nothing, so the frame can fill it and the badge can sit where it belongs.
/// </para>
/// <para>
/// Cards are focusable, so the shell's existing Tab traversal reaches them and Enter selects.
/// There is no graph-specific key.
/// </para>
/// </summary>
public abstract partial class NodeCard : Control
{
    /// <summary>Inline card meters are 4px, which is below the height a rounded fill is legible at.</summary>
    protected const int MeterHeight = 4;

    private static readonly StyleBoxFlat Resting = ShellTheme.Card(selected: false);
    private static readonly StyleBoxFlat Selected = ShellTheme.Card(selected: true);

    private readonly string _badge;
    private readonly string _icon;

    private PanelContainer _frame = null!;
    private Label _caption = null!;
    private Label _value = null!;
    private Color? _lastValueColor;
    private bool _selected;
    private bool _focused;

    protected NodeCard(GraphSelection selection, string title, string badge, string icon)
    {
        Selection = selection;
        CardTitle = title;
        _badge = badge;
        _icon = icon;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(GraphGeometry.CellWidth, GraphGeometry.CellHeight);
    }

    /// <summary>Raised when the player picks this card, by click or by Enter while focused.</summary>
    public event Action<GraphSelection>? Chosen;

    public GraphSelection Selection { get; }

    protected string CardTitle { get; }

    /// <summary>Reads this card's own subject out of the snapshot. A card no longer in the world says so.</summary>
    public abstract void Refresh(WorldSnapshot snapshot);

    public sealed override void _Ready()
    {
        _frame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        _frame.AddThemeStyleboxOverride("panel", Resting);
        AddChild(_frame);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        _frame.AddChild(column);

        column.AddChild(Header());
        BuildBody(column);

        // Added after the frame so it draws over the frame's border, and positioned outward by
        // SpaceSm so it overlaps that border rather than sitting inside the padding.
        AddChild(Badge());

        // Focus is drawn the same way selection is, because a card has no chrome to spare for a
        // second indicator and a ring around it would grow its hit area.
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

    private void SetFocused(bool focused)
    {
        if (_focused == focused)
        {
            return;
        }

        _focused = focused;
        ApplyChrome();
    }

    /// <summary>The override, not the stylebox itself, is what has to be deduped: it allocates.</summary>
    private void ApplyChrome() =>
        _frame.AddThemeStyleboxOverride("panel", _selected || _focused ? Selected : Resting);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                GrabFocus();
                Chosen?.Invoke(Selection);
                AcceptEvent();
                break;

            // Enter on a focused card, which is how the shell's Tab traversal reaches a node.
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }:
                Chosen?.Invoke(Selection);
                AcceptEvent();
                break;
        }
    }

    /// <summary>Adds the per-kind rows beneath the shared header.</summary>
    protected abstract void BuildBody(VBoxContainer column);

    /// <summary>
    /// Sets the card's headline reading: a caption saying what is being reported, and the value
    /// itself. Colour never travels alone — the value is always a word or a quantity that says the
    /// same thing the colour does.
    /// </summary>
    protected void Status(string caption, string value, Color color)
    {
        _caption.Text = caption;
        _value.Text = value;

        // Text dedupes internally; the colour override does not.
        if (_lastValueColor != color)
        {
            _lastValueColor = color;
            _value.AddThemeColorOverride("font_color", color);
        }
    }

    protected static Label Row(VBoxContainer column, Color color)
    {
        // Ellipsised rather than allowed to run: a card is a fixed cell on a grid, and a row that
        // overflowed would print across the card beside it.
        var label = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(label);
        return label;
    }

    /// <summary>A bar and its percentage. The percentage sits in a fixed-width slot, so the bar's
    /// length does not change when the value crosses from 9% to 10%.</summary>
    protected static CardMeter Meter(VBoxContainer column, Color fill)
    {
        var meter = new CardMeter(fill);
        column.AddChild(meter);
        return meter;
    }

    /// <summary>An empty bar rather than a division, whenever there is no denominator to divide by.</summary>
    protected static float Fill(long amount, long capacity) =>
        capacity <= 0 ? 0f : Mathf.Clamp((float)((double)amount / capacity), 0f, 1f);

    protected static string Describe(PostponeReason? reason) => reason switch
    {
        PostponeReason.InsufficientInputMaterial => "MISSING_INPUT",
        PostponeReason.InsufficientSourceMaterial => "NO_SOURCE_MATERIAL",
        PostponeReason.DestinationFull => "DESTINATION_FULL",
        PostponeReason.InsufficientEnergy => "INSUFFICIENT_ENERGY",
        PostponeReason.OutputRouteUnavailable => "NO_OUTPUT_ROUTE",
        PostponeReason.SafetyLock => "SAFETY_LOCK",
        _ => "UNKNOWN",
    };

    /// <summary>The icon, then the title and the headline reading beside it.</summary>
    private Control Header()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        header.AddChild(
            new IconSlot("facility", _icon, IconSlot.CardSize, ShellPalette.TextDim));

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        header.AddChild(text);

        var title = new Label
        {
            Text = CardTitle.ToUpperInvariant(),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        title.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        text.AddChild(title);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        text.AddChild(line);

        _caption = new Label();
        _caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        _caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        line.AddChild(_caption);

        _value = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _value.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        line.AddChild(_value);

        return header;
    }

    private Control Badge()
    {
        var chip = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(-ShellPalette.SpaceSm, -ShellPalette.SpaceSm),
        };
        chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: false));

        var label = new Label { Text = _badge.ToUpperInvariant() };
        label.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        chip.AddChild(label);

        return chip;
    }

    /// <summary>A 4px bar with a right-aligned percentage in a fixed-width slot.</summary>
    protected sealed partial class CardMeter : HBoxContainer
    {
        /// <summary>Wide enough for "100%" at <see cref="ShellPalette.FontMicro"/>.</summary>
        private const int PercentWidth = 30;

        private readonly Color _fill;

        private ProgressBar _bar = null!;
        private Label _percent = null!;

        public CardMeter(Color fill) => _fill = fill;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", ShellPalette.SpaceSm);

            _bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, MeterHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            _bar.AddThemeStyleboxOverride("fill", ShellTheme.MeterFill(_fill, MeterHeight));
            _bar.AddThemeStyleboxOverride("background", ShellTheme.MeterTrough(MeterHeight));
            AddChild(_bar);

            _percent = new Label
            {
                CustomMinimumSize = new Vector2(PercentWidth, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            _percent.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            _percent.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            AddChild(_percent);
        }

        public void Set(float fill)
        {
            var clamped = Mathf.Clamp(fill, 0f, 1f);
            _bar.Value = clamped;

            // Floored rather than rounded: a bar that has not filled must not read 100%.
            _percent.Text = $"{Mathf.FloorToInt(clamped * 100f)}%";
        }
    }
}
