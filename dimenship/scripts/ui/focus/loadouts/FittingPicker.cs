using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The fittings one socket accepts, opened beside that socket's box. Compatibility is the
/// catalog's (<see cref="LoadoutCatalog.OfKind"/>), so there is no incompatible row to list and
/// explain. The last row is <c>CLEAR SOCKET</c>, the ticket's explicit clear action, disabled and
/// marked <c>ALREADY EMPTY</c> on an empty socket.
/// <para>
/// Hovering or focusing a row previews it: the row shows what choosing it would change, and the
/// view hatches the same change onto the strip. A preview is never an edit — the view rolls up a
/// copy of the template — so it cannot reach the undo stack or the template.
/// </para>
/// </summary>
public sealed partial class FittingPicker : StagePopover
{
    private const int ImageSize = 40;

    private readonly SocketDef _socket;
    private readonly string? _fitted;
    private readonly List<Row> _rows = new();

    public FittingPicker(SocketDef socket, string? fitted)
        : base($"COMPATIBLE {LoadoutCatalog.Label(socket.Kind).ToUpperInvariant()} FITTINGS")
    {
        _socket = socket;
        _fitted = fitted;
    }

    /// <summary>Raised with the chosen fitting's id, or null for <c>CLEAR SOCKET</c>.</summary>
    public Action<string?>? Chosen { get; set; }

    /// <summary>Raised with the previewed fitting's id, or null when <c>CLEAR SOCKET</c> is previewed.</summary>
    public Action<string?>? Previewed { get; set; }

    public Action? PreviewCleared { get; set; }

    /// <summary>The fitted row when there is one, so opening the picker previews no change.</summary>
    public override void FocusFirst()
    {
        foreach (var row in _rows)
        {
            if (row.Fitted)
            {
                row.GrabFocus();
                return;
            }
        }

        base.FocusFirst();
    }

    protected override void Fill(VBoxContainer rows)
    {
        var current = LoadoutCatalog.Fitting(_fitted);
        var baseline = current?.Delta ?? default;

        foreach (var fitting in LoadoutCatalog.OfKind(_socket.Kind))
        {
            Add(rows, new Row(
                fitting.Label,
                LoadoutCatalog.FittingArtPath(fitting.Id),
                fitting.Id == _fitted ? "FITTED" : null,
                StatFormat.Change(fitting.Delta - baseline),
                enabled: true)
            {
                Previewed = () => Previewed?.Invoke(fitting.Id),
                Left = () => PreviewCleared?.Invoke(),
                Chosen = () => Chosen?.Invoke(fitting.Id),
            });
        }

        Add(rows, new Row(
            "CLEAR SOCKET",
            art: null,
            _fitted is null ? "ALREADY EMPTY" : null,
            StatFormat.Change(default(StatBlock) - baseline),
            enabled: _fitted is not null)
        {
            Previewed = () => Previewed?.Invoke(null),
            Left = () => PreviewCleared?.Invoke(),
            Chosen = () => Chosen?.Invoke(null),
        });
    }

    private void Add(VBoxContainer rows, Row row)
    {
        _rows.Add(row);
        rows.AddChild(row);
    }

    /// <summary>
    /// One candidate: image, name, a chip for the fitting already in the socket, and — while it is
    /// hovered or focused — the line saying what choosing it would change.
    /// </summary>
    private sealed partial class Row : PanelContainer
    {
        private readonly string _label;
        private readonly string? _art;
        private readonly string? _chip;
        private readonly string _change;
        private readonly bool _enabled;

        private Label _changeLine = null!;
        private bool _hot;

        public Row(string label, string? art, string? chip, string change, bool enabled)
        {
            _label = label;
            _art = art;
            _chip = chip;
            _change = change;
            _enabled = enabled;

            FocusMode = enabled ? FocusModeEnum.All : FocusModeEnum.None;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Previewed { get; set; }

        public Action? Left { get; set; }

        public Action? Chosen { get; set; }

        public bool Fitted => _chip == "FITTED";

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            AddChild(row);

            row.AddChild(_art is null
                ? new IconSlot("control", "close", ImageSize, ShellPalette.TextDim)
                : IconSlot.FromPath(_art, ImageSize, ShellPalette.Projection));

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
                Text = _label,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeColorOverride("font_color", _enabled ? ShellPalette.TextTitle : ShellPalette.TextDim);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            line.AddChild(name);

            if (_chip is not null)
            {
                var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
                chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: Fitted));

                var text = new Label { Text = _chip, MouseFilter = MouseFilterEnum.Ignore };
                text.AddThemeColorOverride("font_color", Fitted ? ShellPalette.TextTitle : ShellPalette.TextDim);
                text.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
                chip.AddChild(text);
                line.AddChild(chip);
            }

            _changeLine = new Label
            {
                Text = _change,
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _changeLine.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            _changeLine.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(_changeLine);

            MouseEntered += Enter;
            MouseExited += Exit;
            FocusEntered += Enter;
            FocusExited += Exit;
        }

        public override void _GuiInput(InputEvent @event)
        {
            var pressed = @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                || @event.IsActionPressed("ui_accept");

            if (!pressed)
            {
                return;
            }

            if (_enabled)
            {
                Chosen?.Invoke();
            }

            AcceptEvent();
        }

        private void Enter()
        {
            if (!_enabled)
            {
                return;
            }

            _hot = true;
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: true));
            _changeLine.Visible = true;
            Previewed?.Invoke();
        }

        private void Exit()
        {
            if (!_hot)
            {
                return;
            }

            _hot = false;
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));
            _changeLine.Visible = false;
            Left?.Invoke();
        }
    }
}
