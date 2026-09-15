using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The centre of the loadout composer: the selected template's frame drawn as projected line art,
/// one <see cref="FittingBox"/> per socket around it, and a leader line from each box to its
/// connector. Drawn the way <see cref="GraphCanvas"/> draws the base graph — lines drawn, cards as
/// child controls — back to front: the art, the <see cref="LeaderLayer"/>, the boxes, then any
/// popover.
/// <para>
/// The frame art never changes with what is fitted. The ticket's asset budget is one illustration
/// per frame, and every connector is drawn whether or not anything is in it; fittings appear only
/// in their boxes.
/// </para>
/// <para>
/// Boxes are placed where the frame's sidecar puts them and are <b>never moved</b> to avoid one
/// another. At a stage size where they collide, the stage says so in one line and warns once; an
/// automatic layout would be a second, unauthored answer to where a box belongs. When a frame's
/// art is unusable the stage draws no art at all, stacks the boxes in a plain column, and names
/// the first problem — composing keeps working, because nothing in the model depends on the art.
/// </para>
/// <para>
/// Boxes are kept across refreshes while the frame is unchanged, so an edit does not take keyboard
/// focus away from the box the player is on.
/// </para>
/// </summary>
public sealed partial class LoadoutStage : Control
{
    private const string OverlapNotice = "BOXES OVERLAP AT THIS SIZE — WIDEN THE VIEW";

    /// <summary>Frames already warned about overlapping at some size, so a window drag warns once.</summary>
    private static readonly HashSet<string> WarnedOverlap = new();

    private readonly List<FittingBox> _boxes = new();

    private TextureRect _art = null!;
    private LeaderLayer _leaders = null!;
    private Label _notice = null!;

    private FrameDef? _frame;
    private FrameArtEntry? _entry;
    private int _selected;
    private StageFit _fit;

    /// <summary>A box was clicked or activated with <c>ui_accept</c>.</summary>
    public Action<int>? SocketChosen { get; set; }

    /// <summary>A box took keyboard focus.</summary>
    public Action<int>? SocketFocused { get; set; }

    /// <summary>The artwork's horizontal centre in stage pixels: which side of the machine a box is on.</summary>
    public int CanvasCentreX => _entry?.Art is { } art
        ? _fit.OffsetX + StageGeometry.Scale(art.Canvas.W / 2, _fit.ScalePermille)
        : (int)(Size.X / 2);

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Pass;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _art = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };

        if (ProjectionGlow.Create() is { } glow)
        {
            _art.Material = glow;
        }
        else
        {
            _art.SelfModulate = ShellPalette.Projection;
        }

        AddChild(_art);

        _leaders = new LeaderLayer();
        AddChild(_leaders);

        _notice = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _notice.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
        _notice.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        AddChild(_notice);

        Resized += Layout;
    }

    public void Show(FrameDef frame, LoadoutDraft template, int selected)
    {
        _selected = selected;

        if (_frame?.Id != frame.Id || _boxes.Count != frame.Sockets.Count)
        {
            _frame = frame;
            _entry = FrameArtLibrary.For(frame);

            foreach (var box in _boxes)
            {
                box.QueueFree();
            }

            _boxes.Clear();

            for (var i = 0; i < frame.Sockets.Count; i++)
            {
                var box = new FittingBox(i, frame.Sockets[i])
                {
                    Chosen = index => SocketChosen?.Invoke(index),
                    Focused = index => SocketFocused?.Invoke(index),
                };

                _boxes.Add(box);
                AddChild(box);
            }
        }

        for (var i = 0; i < _boxes.Count; i++)
        {
            var fitting = i < template.Fitted.Count ? LoadoutCatalog.Fitting(template.Fitted[i]) : null;
            _boxes[i].Refresh(fitting, i == selected);
        }

        Layout();
    }

    public void FocusBox(int socket)
    {
        if (socket >= 0 && socket < _boxes.Count)
        {
            _boxes[socket].GrabFocus();
        }
    }

    /// <summary>A box's rectangle in stage coordinates, for placing a popover beside it.</summary>
    public Rect2 BoxRect(int socket) =>
        socket >= 0 && socket < _boxes.Count
            ? new Rect2(_boxes[socket].Position, _boxes[socket].Size)
            : new Rect2();

    private void Layout()
    {
        if (_frame is null || _entry is null || !IsNodeReady())
        {
            return;
        }

        _leaders.Position = Vector2.Zero;
        _leaders.Size = Size;

        if (_entry.Art is not { } art)
        {
            Fallback(_entry);
            return;
        }

        var stage = ((int)Size.X, (int)Size.Y);
        _fit = StageGeometry.Fit(art.Canvas, stage);

        _art.Visible = true;
        _art.Position = new Vector2(_fit.OffsetX, _fit.OffsetY);
        _art.Size = new Vector2(
            StageGeometry.Scale(art.Canvas.W, _fit.ScalePermille),
            StageGeometry.Scale(art.Canvas.H, _fit.ScalePermille));
        _art.Texture = FrameArtLibrary.Rasterise(_frame, _fit.ScalePermille);

        var rects = new List<(int X, int Y, int W, int H)>();
        var leaders = new List<IReadOnlyList<(int X, int Y)>>();

        for (var i = 0; i < _boxes.Count; i++)
        {
            // FrameArtLibrary has already checked that the placement names exactly this frame's
            // sockets, so every socket has exactly one.
            var placement = art.Sockets.First(socket => socket.Id == _frame.Sockets[i].Id);
            var rect = StageGeometry.BoxRect(
                _fit, placement.Box, (FittingBox.Width, FittingBox.Height), stage);

            Place(_boxes[i], rect);
            rects.Add(rect);
            leaders.Add(StageGeometry.Leader(rect, StageGeometry.ToStage(_fit, placement.Anchor)));
        }

        _leaders.Show(leaders, _selected);

        var overlapping = StageGeometry.Problems(rects, leaders).Count > 0;
        ShowNotice(overlapping ? OverlapNotice : string.Empty, atTop: false);

        if (overlapping && WarnedOverlap.Add(_frame.Id))
        {
            GD.PushWarning(
                $"Frame art '{_frame.Id}': boxes overlap or leaders cross at a {stage.Item1}x{stage.Item2} stage.");
        }
    }

    /// <summary>
    /// No art: boxes in socket declaration order down a plain column, wrapping into a second
    /// column when the stage is too short, under a line naming what is wrong.
    /// </summary>
    private void Fallback(FrameArtEntry entry)
    {
        _art.Visible = false;
        _art.Texture = null;
        _leaders.Show(Array.Empty<IReadOnlyList<(int X, int Y)>>(), -1);

        ShowNotice($"FRAME ART UNAVAILABLE — {entry.Warnings[0]}", atTop: true);

        var top = (ShellPalette.SpaceMd * 2) + (int)_notice.GetCombinedMinimumSize().Y;
        var x = ShellPalette.SpaceMd;
        var y = top;

        foreach (var box in _boxes)
        {
            if (y + FittingBox.Height > Size.Y && y > top)
            {
                x += FittingBox.Width + ShellPalette.SpaceMd;
                y = top;
            }

            Place(box, (x, y, FittingBox.Width, FittingBox.Height));
            y += FittingBox.Height + ShellPalette.SpaceMd;
        }
    }

    private void ShowNotice(string text, bool atTop)
    {
        _notice.Text = text;

        var height = _notice.GetCombinedMinimumSize().Y;
        _notice.Position = new Vector2(
            ShellPalette.SpaceMd,
            atTop ? ShellPalette.SpaceMd : Size.Y - height - ShellPalette.SpaceMd);
        _notice.MoveToFront();
    }

    private static void Place(Control control, (int X, int Y, int W, int H) rect)
    {
        control.Position = new Vector2(rect.X, rect.Y);
        control.Size = new Vector2(rect.W, rect.H);
    }
}
