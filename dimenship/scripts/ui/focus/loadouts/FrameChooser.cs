using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The frames a template can be built on, each with its thumbnail, socket count and note. Hovering
/// or focusing a frame says what a swap to it would keep and drop, in words, before the player
/// makes it — <see cref="LoadoutDraft.CarryOver"/>'s rule, read out rather than discovered.
/// </summary>
public sealed partial class FrameChooser : StagePopover
{
    private const int ThumbnailWidth = 96;
    private const int ThumbnailHeight = 58;

    /// <summary>A quarter of the canvas: 300×180 for a 1200×720 frame, comfortably above the thumbnail.</summary>
    private const int ThumbnailScalePermille = 250;

    private readonly FrameDef _current;
    private readonly IReadOnlyList<string?> _fitted;

    public FrameChooser(FrameDef current, IReadOnlyList<string?> fitted)
        : base("CHANGE FRAME")
    {
        _current = current;
        _fitted = fitted;
    }

    public Action<FrameDef>? Chosen { get; set; }

    /// <summary>
    /// <c>KEEPS tool · sensor · power — DROPS investigation</c>: the socket ids whose fittings a swap
    /// carries, then the ids of fitted sockets it loses.
    /// </summary>
    public static string CarryOverLine(FrameDef from, IReadOnlyList<string?> fitted, FrameDef to)
    {
        var carried = LoadoutDraft.CarryOver(from, fitted, to);

        var kept = to.Sockets
            .Where((_, i) => carried[i] is not null)
            .Select(socket => socket.Id)
            .ToList();

        var dropped = from.Sockets
            .Where((socket, i) => i < fitted.Count && fitted[i] is not null && !kept.Contains(socket.Id))
            .Select(socket => socket.Id)
            .ToList();

        if (kept.Count == 0 && dropped.Count == 0)
        {
            return "NOTHING FITTED TO CARRY";
        }

        var keeps = kept.Count > 0 ? $"KEEPS {string.Join(" · ", kept)}" : "KEEPS NOTHING";

        return dropped.Count > 0 ? $"{keeps} — DROPS {string.Join(" · ", dropped)}" : keeps;
    }

    protected override void Fill(VBoxContainer rows)
    {
        foreach (var frame in LoadoutCatalog.Frames)
        {
            var current = frame.Id == _current.Id;

            rows.AddChild(new Row(frame, current, current ? string.Empty : CarryOverLine(_current, _fitted, frame))
            {
                Chosen = () => Chosen?.Invoke(frame),
            });
        }
    }

    private sealed partial class Row : PanelContainer
    {
        private readonly FrameDef _frame;
        private readonly bool _current;
        private readonly string _carry;

        private Label _carryLine = null!;

        public Row(FrameDef frame, bool current, string carry)
        {
            _frame = frame;
            _current = current;
            _carry = carry;

            FocusMode = current ? FocusModeEnum.None : FocusModeEnum.All;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Chosen { get; set; }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            AddChild(row);

            row.AddChild(new TextureRect
            {
                Texture = FrameArtLibrary.Rasterise(_frame, ThumbnailScalePermille),
                CustomMinimumSize = new Vector2(ThumbnailWidth, ThumbnailHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = ShellPalette.Projection,
                MouseFilter = MouseFilterEnum.Ignore,
            });

            var column = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            row.AddChild(column);

            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
            column.AddChild(line);

            var name = new Label
            {
                Text = _frame.Label,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            name.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            line.AddChild(name);

            if (_current)
            {
                var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
                chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: true));

                var text = new Label { Text = "CURRENT", MouseFilter = MouseFilterEnum.Ignore };
                text.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
                text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
                chip.AddChild(text);
                line.AddChild(chip);
            }

            column.AddChild(Micro($"{_frame.Sockets.Count} SOCKETS", ShellPalette.TextDim));

            // Trimmed rather than wrapped, with the whole note as a tooltip: a wrapping label's
            // height depends on a width the popover does not have until it is placed, and the
            // popover is sized from its minimum before that.
            var note = Micro(_frame.Note, ShellPalette.TextFaint);
            note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            TooltipText = _frame.Note;
            column.AddChild(note);

            _carryLine = Micro(_carry, ShellPalette.TextPrimary);
            _carryLine.Visible = false;
            _carryLine.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            column.AddChild(_carryLine);

            MouseEntered += () => Hot(true);
            MouseExited += () => Hot(false);
            FocusEntered += () => Hot(true);
            FocusExited += () => Hot(false);
        }

        public override void _GuiInput(InputEvent @event)
        {
            var pressed = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || @event.IsActionPressed("ui_accept");

            if (!pressed)
            {
                return;
            }

            if (!_current)
            {
                Chosen?.Invoke();
            }

            AcceptEvent();
        }

        private void Hot(bool hot)
        {
            if (_current)
            {
                return;
            }

            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: hot));
            _carryLine.Visible = hot;
        }

        private static Label Micro(string text, Color colour)
        {
            var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            return label;
        }
    }
}
