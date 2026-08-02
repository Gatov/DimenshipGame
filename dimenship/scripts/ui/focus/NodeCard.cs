using System;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The chrome every graph node shares: a border, a title, a status line, a selection highlight and
/// a hit area. Subclasses supply the body and say how to read themselves out of a snapshot.
/// <para>
/// Cards are focusable, so the shell's existing Tab traversal reaches them and Enter selects.
/// There is no graph-specific key.
/// </para>
/// </summary>
public abstract partial class NodeCard : PanelContainer
{
    private static readonly StyleBoxFlat Resting = Chrome(ShellPalette.Border);
    private static readonly StyleBoxFlat Selected = Chrome(ShellPalette.StateWarn);

    private Label _title = null!;
    private Label _status = null!;
    private Color? _lastStatusColor;
    private bool _selected;

    protected NodeCard(GraphSelection selection, string title)
    {
        Selection = selection;
        CardTitle = title;
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
        AddThemeStyleboxOverride("panel", Resting);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);

        _title = new Label { Text = CardTitle.ToUpperInvariant() };
        _title.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        column.AddChild(_title);

        _status = new Label();
        _status.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(_status);

        BuildBody(column);
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected)
        {
            return;
        }

        _selected = selected;
        AddThemeStyleboxOverride("panel", selected ? Selected : Resting);
    }

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

    /// <summary>Adds the per-kind rows beneath the shared title and status line.</summary>
    protected abstract void BuildBody(VBoxContainer column);

    /// <summary>
    /// Sets the status line. Colour never travels alone: the caller always passes the code that
    /// says the same thing in words.
    /// </summary>
    protected void Status(string text, Color color)
    {
        _status.Text = text;

        // Text dedupes internally; the colour override does not.
        if (_lastStatusColor != color)
        {
            _lastStatusColor = color;
            _status.AddThemeColorOverride("font_color", color);
        }
    }

    protected static Label Row(VBoxContainer column, Color color)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(label);
        return label;
    }

    protected static ProgressBar Bar(VBoxContainer column, Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 4),
        };
        bar.AddThemeStyleboxOverride("fill", ShellTheme.Surface(fill));
        bar.AddThemeStyleboxOverride("background", ShellTheme.Surface(ShellPalette.BgBase));
        column.AddChild(bar);
        return bar;
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

    private static StyleBoxFlat Chrome(Color border)
    {
        var box = new StyleBoxFlat
        {
            BgColor = ShellPalette.BgGlass,
            BorderColor = border,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
        };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(ShellPalette.SpaceSm);
        return box;
    }
}
