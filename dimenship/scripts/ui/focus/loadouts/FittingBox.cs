using System;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One socket on the stage: what kind it is, what is fitted in it, and that fitting's image. The
/// ticket's <i>equipment box</i> — a visual element, not an inventory object; nothing is stored in
/// it.
/// <para>
/// Its size is fixed in pixels and never scales with the frame art, because it carries text and
/// text stays at the palette's sizes. Only its centre moves with the drawing.
/// </para>
/// <para>
/// An empty socket says <c>EMPTY</c> in words over a dashed <c>+</c>, drawn with
/// <see cref="ShellTheme.DrawDashedPolyline"/> — the base graph's vocabulary for "nothing is here
/// yet", so the two views say one thing one way. Selection is the card's border colour and nothing
/// else, the shell's one selection signal.
/// </para>
/// </summary>
public sealed partial class FittingBox : PanelContainer
{
    public const int Width = 184;

    /// <summary>Kind line, name line, a 64px image, the card's padding, and nothing to spare.</summary>
    public const int Height = 112;

    public const int ImageSize = 64;

    private readonly int _index;
    private readonly SocketDef _socket;

    private FittingDef? _fitting;
    private bool _selected;

    private Label _name = null!;
    private IconSlot _image = null!;
    private EmptyMark _empty = null!;

    public FittingBox(int index, SocketDef socket)
    {
        _index = index;
        _socket = socket;

        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(Width, Height);
        ClipContents = true;
    }

    /// <summary>Raised with this box's socket index on a click or <c>ui_accept</c>.</summary>
    public Action<int>? Chosen { get; set; }

    /// <summary>Raised when keyboard focus lands here, so focus and selection are one thing.</summary>
    public Action<int>? Focused { get; set; }

    public override void _Ready()
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        AddChild(column);

        var kind = new Label
        {
            Text = LoadoutCatalog.Label(_socket.Kind).ToUpperInvariant(),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        kind.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        kind.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(kind);

        _name = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        column.AddChild(_name);

        var art = new CenterContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(art);

        _image = new IconSlot(ImageSize, ShellPalette.Projection);
        art.AddChild(_image);

        _empty = new EmptyMark();
        art.AddChild(_empty);

        FocusEntered += () => Focused?.Invoke(_index);
        ApplyChrome();
    }

    public void Refresh(FittingDef? fitting, bool selected)
    {
        _fitting = fitting;
        _selected = selected;

        if (IsNodeReady())
        {
            ApplyChrome();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            GrabFocus();
            Chosen?.Invoke(_index);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Chosen?.Invoke(_index);
            AcceptEvent();
        }
    }

    private void ApplyChrome()
    {
        AddThemeStyleboxOverride("panel", ShellTheme.Card(_selected));

        _name.Text = _fitting?.Label ?? "EMPTY";
        _name.AddThemeColorOverride(
            "font_color", _fitting is null ? ShellPalette.TextDim : ShellPalette.TextTitle);

        if (_fitting is null)
        {
            _image.Clear();
            _image.Visible = false;
            _empty.Visible = true;
            return;
        }

        _image.SetPath(LoadoutCatalog.FittingArtPath(_fitting.Id));
        _image.Visible = true;
        _empty.Visible = false;
    }

    /// <summary>The empty state's dashed square and plus, the size of the image it stands in for.</summary>
    private sealed partial class EmptyMark : Control
    {
        public EmptyMark()
        {
            CustomMinimumSize = new Vector2(ImageSize, ImageSize);
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            const float inset = ShellPalette.SpaceMd;
            const float far = ImageSize - ShellPalette.SpaceMd;
            const float mid = ImageSize / 2f;
            const float arm = ShellPalette.SpaceMd;

            ShellTheme.DrawDashedPolyline(
                this,
                new[]
                {
                    new Vector2(inset, inset), new Vector2(far, inset), new Vector2(far, far),
                    new Vector2(inset, far), new Vector2(inset, inset),
                },
                ShellPalette.TextDim,
                1f);

            DrawLine(new Vector2(mid - arm, mid), new Vector2(mid + arm, mid), ShellPalette.TextDim, 1f, true);
            DrawLine(new Vector2(mid, mid - arm), new Vector2(mid, mid + arm), ShellPalette.TextDim, 1f, true);
        }
    }
}
