using System;
using Dimenship.Core.Content;
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
    private HBoxContainer _body = null!;
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

        // The frame's one child is a row, not the text column, so a card can hang a full-height
        // gauge down its right edge beside every row at once. A card without one is unaffected:
        // the column expands into the whole frame exactly as it did when it was the only child.
        _body = new HBoxContainer();
        _body.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        _frame.AddChild(_body);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        _body.AddChild(column);

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

    /// <summary>
    /// A row whose subject has an icon of its own: the glyph, then the same text an ordinary
    /// <see cref="Row(VBoxContainer, Color)"/> would carry. The domain is fixed per row because a
    /// row lists one kind of thing — a storage's contents are items, and stay items.
    /// </summary>
    protected static IconRow Row(VBoxContainer column, string domain, Color color)
    {
        var row = new IconRow(domain, color);
        column.AddChild(row);
        return row;
    }

    /// <summary>A bar and its percentage. The percentage sits in a fixed-width slot, so the bar's
    /// length does not change when the value crosses from 9% to 10%.</summary>
    protected static CardMeter Meter(VBoxContainer column, Color fill)
    {
        var meter = new CardMeter(fill);
        column.AddChild(meter);
        return meter;
    }

    /// <summary>
    /// A vertical bar down the card's right edge, full card height rather than one row's. It is for
    /// the reading a facility has to be watched on continuously — the fullness of its own local
    /// storage — which an inline meter competing with the status rows for width cannot carry.
    /// It never travels alone: the card still prints the same number as text, because a bar is a
    /// length and a length is not a value.
    /// </summary>
    protected CardGauge Gauge(Color fill)
    {
        var gauge = new CardGauge(fill);
        _body.AddChild(gauge);
        return gauge;
    }

    /// <summary>An empty bar rather than a division, whenever there is no denominator to divide by.</summary>
    protected static float Fill(long amount, long capacity) =>
        capacity <= 0 ? 0f : Mathf.Clamp((float)((double)amount / capacity), 0f, 1f);

    /// <summary>
    /// A bar's length from a storage's fill, which the kernel already reduced to a permille of
    /// one shared volume. Clamped rather than trusted: a scenario may seed a storage over its own
    /// capacity, and a bar drawn past its track would be a rendering fault on screen.
    /// </summary>
    protected static float Fill(long permille) =>
        Mathf.Clamp(permille / (float)StorageArchetype.FullHold, 0f, 1f);

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

    /// <summary>
    /// An icon and a line of text, sized and spaced like the plain rows beside it. The slot keeps
    /// its width whether or not it has anything in it, so the text of every row on a card starts
    /// at the same x however many of them currently have icons.
    /// </summary>
    protected sealed partial class IconRow : HBoxContainer
    {
        private readonly string _domain;
        private readonly Color _color;

        private IconSlot _icon = null!;
        private Label _text = null!;

        public IconRow(string domain, Color color)
        {
            _domain = domain;
            _color = color;
        }

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            MouseFilter = MouseFilterEnum.Ignore;

            _icon = new IconSlot(IconSlot.RowSize, _color);
            AddChild(_icon);

            // Ellipsised for the reason every card row is: a card is a fixed cell on a grid.
            _text = new Label
            {
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _text.AddThemeColorOverride("font_color", _color);
            _text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            AddChild(_text);
        }

        /// <summary>
        /// Sets both halves at once, because they describe the same thing and a row that updated
        /// its text without its icon would be captioning the previous subject. An empty name
        /// empties the slot, which is what a row with nothing in it needs.
        /// </summary>
        public void Set(string name, string text)
        {
            if (string.IsNullOrEmpty(name))
            {
                _icon.Clear();
            }
            else
            {
                _icon.SetIcon(_domain, name);
            }

            _text.Text = text;
        }
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

    /// <summary>
    /// A <see cref="MeterHeight"/>-wide bar filling from the bottom, sized by anchors rather than
    /// by a <see cref="ProgressBar"/>: Godot's bar fills left to right, and a rotated control would
    /// carry a rotated hit area across the card behind it.
    /// <para>
    /// Two panels rather than one panel and a draw call, so the trough and the fill are the same
    /// <see cref="ShellTheme.MeterTrough"/> and <see cref="ShellTheme.MeterFill"/> styleboxes every
    /// other bar in the shell uses — a gauge that drew its own rectangle would be a colour outside
    /// the palette the first time either changed.
    /// </para>
    /// </summary>
    protected sealed partial class CardGauge : Control
    {
        private readonly Color _fill;

        private Panel _level = null!;

        public CardGauge(Color fill)
        {
            _fill = fill;
            MouseFilter = MouseFilterEnum.Ignore;

            // Width is fixed and height is taken from the row, so the gauge is as tall as the card
            // whatever the card's body turns out to hold.
            CustomMinimumSize = new Vector2(MeterHeight, 0);
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public override void _Ready()
        {
            var trough = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            trough.SetAnchorsPreset(LayoutPreset.FullRect);
            trough.AddThemeStyleboxOverride("panel", ShellTheme.MeterTrough(MeterHeight));
            AddChild(trough);

            _level = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            _level.AddThemeStyleboxOverride("panel", ShellTheme.MeterFill(_fill, MeterHeight));
            AddChild(_level);

            // An empty gauge until a snapshot says otherwise, rather than a full one for a frame.
            Set(0f);
        }

        /// <summary>
        /// Anchors the fill to the bottom of the track and to the given fraction of its height.
        /// The offsets are rewritten every call because Godot keeps them when an anchor moves,
        /// which would leave the bar a few pixels off its own track.
        /// </summary>
        public void Set(float fill)
        {
            var clamped = Mathf.Clamp(fill, 0f, 1f);

            _level.AnchorLeft = 0f;
            _level.AnchorRight = 1f;
            _level.AnchorTop = 1f - clamped;
            _level.AnchorBottom = 1f;
            _level.OffsetLeft = 0f;
            _level.OffsetRight = 0f;
            _level.OffsetTop = 0f;
            _level.OffsetBottom = 0f;
        }
    }
}
