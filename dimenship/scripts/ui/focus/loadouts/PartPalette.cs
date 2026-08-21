using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Core.Simulation;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The composer's right column: every part there is, filtered by socket kind and searchable, and
/// each row both a click target and a drag source.
/// <para>
/// It lives inside the focus zone rather than as a panel in the inspector zone, for the same reason
/// the block palette does: a palette the player can replace with the Energy Budget panel is a
/// palette that will be missing exactly when it is needed.
/// </para>
/// <para>
/// It is also a drop target. Dragging a fitted part out of a socket and onto the palette removes
/// it — the gesture the player already expects from having dragged it the other way.
/// </para>
/// </summary>
public sealed partial class PartPalette : VBoxContainer
{
    private static readonly IReadOnlyList<SocketKind> Kinds =
        Enum.GetValues<SocketKind>().ToList();

    private readonly List<Button> _tabs = new();
    private readonly HashSet<SocketKind> _available = new();

    private LineEdit _search = null!;
    private VBoxContainer _rows = null!;
    private SocketKind _kind = SocketKind.Tool;

    /// <summary>Raised when a part row is clicked, which fits it without a drag.</summary>
    public Action<PartDef>? Chosen { get; set; }

    /// <summary>Raised when a fitted part is dragged out of its socket and dropped here.</summary>
    public Action<int>? Removed { get; set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        CustomMinimumSize = new Vector2(300, 0);

        AddChild(Tabs());

        _search = new LineEdit { PlaceholderText = "Search parts" };
        _search.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _search.TextChanged += _ => Refresh();
        AddChild(_search);

        // Pass rather than Stop on both, so a part dragged out of a socket and dropped anywhere
        // over the list reaches the palette's own drop handler instead of stopping on whatever
        // container happened to be under the cursor.
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
        };
        AddChild(scroll);

        _rows = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _rows.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        scroll.AddChild(_rows);

        Refresh();
    }

    /// <summary>
    /// The socket kinds the selected frame actually offers. A part of any other kind still lists —
    /// browsing what a different frame could take is the point of the tab strip — but it lists
    /// dimmed and says why, rather than being a row that swallows a click.
    /// </summary>
    public void ShowFrame(IEnumerable<SocketKind> sockets)
    {
        _available.Clear();

        foreach (var socket in sockets)
        {
            _available.Add(socket);
        }

        Refresh();
    }

    /// <summary>
    /// Follows the composer's socket selection. Selecting a socket and then hunting for its tab
    /// would be two gestures for one intention, and the tab strip still lets the player browse a
    /// kind no socket on this frame accepts.
    /// </summary>
    public void ShowKind(SocketKind kind)
    {
        if (_kind == kind)
        {
            return;
        }

        _kind = kind;
        Refresh();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Payload(data) is { FromSocket: not null };

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (Payload(data) is { FromSocket: { } socket })
        {
            Removed?.Invoke(socket);
        }
    }

    private static LoadoutDragData? Payload(Variant data) =>
        data.VariantType == Variant.Type.Object ? data.AsGodotObject() as LoadoutDragData : null;

    private Control Tabs()
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        foreach (var kind in Kinds)
        {
            var tab = new Button
            {
                Text = LoadoutCatalog.Label(kind).ToUpperInvariant(),
                FocusMode = FocusModeEnum.None,
                Icon = IconSlot.Load("status", LoadoutCatalog.Icon(kind)),
            };
            ShellTheme.ApplyGlass(tab);
            tab.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);

            var chosen = kind;
            tab.Pressed += () =>
            {
                _kind = chosen;
                Refresh();
            };

            _tabs.Add(tab);
            strip.AddChild(tab);
        }

        return strip;
    }

    private void Refresh()
    {
        if (!IsNodeReady())
        {
            return;
        }

        for (var i = 0; i < _tabs.Count && i < Kinds.Count; i++)
        {
            var active = Kinds[i] == _kind;
            _tabs[i].AddThemeStyleboxOverride(
                "normal", ShellTheme.Block(ShellPalette.BgGlass, active));
            _tabs[i].AddThemeColorOverride(
                "font_color", active ? ShellPalette.TextTitle : ShellPalette.TextDim);
        }

        foreach (var child in _rows.GetChildren())
        {
            child.QueueFree();
        }

        var term = _search.Text.Trim();

        foreach (var part in LoadoutCatalog.OfKind(_kind))
        {
            if (term.Length > 0
                && !part.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !part.Note.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _rows.AddChild(new PartRow(part, _available.Contains(part.Kind))
            {
                Chosen = () => Chosen?.Invoke(part),
                Removed = socket => Removed?.Invoke(socket),
            });
        }
    }

    /// <summary>
    /// One part: its stats, what it is for, and what it costs. The note is not decoration — with
    /// two or three parts per socket the whole question is what the player gives up by taking one,
    /// and a row of numbers alone does not say it.
    /// </summary>
    private sealed partial class PartRow : PanelContainer
    {
        private readonly PartDef _part;
        private readonly bool _available;

        public PartRow(PartDef part, bool available)
        {
            _part = part;
            _available = available;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Chosen { get; set; }

        /// <summary>
        /// A row is a drop target too, and for removal rather than for fitting: a part dragged out
        /// of a socket has to be droppable over the list it came from, not only over the gap
        /// beneath it.
        /// </summary>
        public Action<int>? Removed { get; set; }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(selected: false));

            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            var title = new Label
            {
                Text = _part.Label,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            title.AddThemeColorOverride(
                "font_color", _available ? ShellPalette.TextTitle : ShellPalette.TextFaint);
            title.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            column.AddChild(title);

            if (!_available)
            {
                column.AddChild(Line(
                    "NO SOCKET ON THIS FRAME", ShellPalette.StateWarn));
            }

            column.AddChild(Line(StatFormat.Summary(_part.Delta), ShellPalette.TextPrimary));
            column.AddChild(Line(Cost(), ShellPalette.TextDim));

            var note = new Label
            {
                Text = _part.Note,
                MouseFilter = MouseFilterEnum.Ignore,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            note.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            note.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(note);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                if (_available)
                {
                    Chosen?.Invoke();
                }

                AcceptEvent();
            }
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            Payload(data) is { FromSocket: not null };

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            if (Payload(data) is { FromSocket: { } socket })
            {
                Removed?.Invoke(socket);
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (!_available)
            {
                return default;
            }

            var payload = new LoadoutDragData { Part = _part };

            var preview = new Label { Text = _part.Label };
            preview.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            preview.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            SetDragPreview(preview);

            return payload;
        }

        private string Cost() => string.Join(
            "   ",
            _part.Cost.Select(line =>
                $"{line.ItemId.ToUpperInvariant()} {Units.Format(line.Amount)}"));

        private static Label Line(string text, Color colour)
        {
            var label = new Label
            {
                Text = text,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);

            return label;
        }
    }
}
