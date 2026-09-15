using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The chrome shared by the loadout stage's two popovers — the fitting picker and the frame
/// chooser: a title, a close button, a column of rows, and a notch pointing at whatever opened it.
/// <para>
/// A <see cref="Control"/> laid over the stage, never a <c>Window</c> or <c>PopupPanel</c>, for
/// the reason <see cref="SettingsOverlay"/> gives: the shell's frost shader samples
/// <c>SCREEN_UV</c>, and a second viewport would sample nothing.
/// </para>
/// <para>
/// It closes on its own close button, on a click anywhere outside it, and when the view asks
/// (<c>Esc</c>, a different socket, an undo). The outside click is seen in <see cref="_Input"/> and
/// deliberately not consumed, so the click still lands on whatever it was aimed at — a click on
/// another box closes this picker and opens that box's in one gesture.
/// </para>
/// </summary>
public partial class StagePopover : PanelContainer
{
    public const int Width = 300;

    private const float NotchSize = 8f;

    private readonly string _title;
    private bool _closing;

    public StagePopover(string title)
    {
        _title = title;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(Width, 0);
    }

    /// <summary>Raised once, as the popover closes for any reason.</summary>
    public Action? Closed { get; set; }

    /// <summary>Where the notch meets this popover's side, in its own coordinates; null for no notch.</summary>
    public float? NotchY { get; set; }

    /// <summary>True when the notch is on the left edge, pointing left at a box to this popover's left.</summary>
    public bool NotchOnLeft { get; set; }

    protected VBoxContainer Rows { get; private set; } = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Popover());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(header);

        var title = new Label
        {
            Text = _title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        header.AddChild(title);

        var close = new Button
        {
            Icon = IconSlot.Load("control", "close"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Close",
        };

        if (close.Icon is null)
        {
            close.Text = "✕";
        }

        ShellTheme.ApplyGlass(close);
        close.Pressed += Close;
        header.AddChild(close);

        Rows = new VBoxContainer();
        Rows.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(Rows);

        Fill(Rows);
    }

    public void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Closed?.Invoke();
        QueueFree();
    }

    /// <summary>Gives keyboard focus to the first row that accepts it.</summary>
    public virtual void FocusFirst()
    {
        foreach (var child in Rows.GetChildren())
        {
            if (child is Control { FocusMode: FocusModeEnum.All } row)
            {
                row.GrabFocus();
                return;
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }
            && !GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            Close();
        }
    }

    public override void _Draw()
    {
        if (NotchY is not { } y)
        {
            return;
        }

        var edge = NotchOnLeft ? 0f : Size.X;
        var tip = new Vector2(edge + (NotchOnLeft ? -NotchSize : NotchSize), y);
        var upper = new Vector2(edge, y - NotchSize);
        var lower = new Vector2(edge, y + NotchSize);

        DrawColoredPolygon(new[] { upper, tip, lower }, ShellPalette.BgPanel);
        DrawPolyline(new[] { upper, tip, lower }, ShellPalette.Accent, 1f, antialiased: true);
    }

    /// <summary>Adds this popover's rows. Called once, from <see cref="_Ready"/>.</summary>
    protected virtual void Fill(VBoxContainer rows)
    {
    }
}
