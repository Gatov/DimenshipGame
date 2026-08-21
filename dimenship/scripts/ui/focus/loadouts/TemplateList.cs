using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The composer's left column: the library, a way to add to it, and what is known about whichever
/// template is selected.
/// </summary>
public sealed partial class TemplateList : VBoxContainer
{
    private VBoxContainer _rows = null!;
    private VBoxContainer _info = null!;

    /// <summary>Raised with the index of the template the player picked.</summary>
    public Action<int>? Chosen { get; set; }

    public Action? NewRequested { get; set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        CustomMinimumSize = new Vector2(260, 0);

        AddChild(BoxSection.Create("Loadout Templates", out _rows));

        var add = new Button { Text = "+ NEW TEMPLATE" };
        ShellTheme.ApplyGlass(add);
        add.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        add.Pressed += () => NewRequested?.Invoke();
        AddChild(add);

        AddChild(BoxSection.Create("Selected Template Info", out _info));
    }

    public void Refresh(IReadOnlyList<LoadoutDraft> templates, int selected)
    {
        if (!IsNodeReady())
        {
            return;
        }

        Clear(_rows);
        Clear(_info);

        for (var i = 0; i < templates.Count; i++)
        {
            var index = i;
            _rows.AddChild(new Entry(templates[i], i == selected)
            {
                Pressed = () => Chosen?.Invoke(index),
            });
        }

        if (selected < 0 || selected >= templates.Count)
        {
            return;
        }

        var template = templates[selected];
        var rollup = LoadoutRollup.Of(template);

        var description = new Label
        {
            Text = template.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        description.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        description.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _info.AddChild(description);

        _info.AddChild(BoxSection.Row("Frame", rollup.Frame.Label));
        _info.AddChild(BoxSection.Row(
            "Sockets", $"{template.FilledSockets} / {rollup.Frame.Sockets.Count}"));
        _info.AddChild(BoxSection.Row(
            "Status", VerdictText.Of(rollup.Verdict), VerdictText.Colour(rollup.Verdict)));
        _info.AddChild(BoxSection.Row("Mass", StatFormat.Plain(rollup.Totals.Mass)));
        _info.AddChild(BoxSection.Row(
            "Power", StatFormat.Signed(rollup.Totals.Power),
            rollup.Totals.Power < 0 ? ShellPalette.StateFault : ShellPalette.TextTitle));
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            child.QueueFree();
        }
    }

    /// <summary>One template in the library: its name, its frame, and where it stands.</summary>
    private sealed partial class Entry : PanelContainer
    {
        private readonly LoadoutDraft _template;
        private readonly bool _selected;

        public Entry(LoadoutDraft template, bool selected)
        {
            _template = template;
            _selected = selected;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public Action? Pressed { get; set; }

        public override void _Ready()
        {
            AddThemeStyleboxOverride("panel", ShellTheme.Card(_selected));

            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            var name = new Label
            {
                Text = _template.Name,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeColorOverride(
                "font_color", _selected ? ShellPalette.TextTitle : ShellPalette.TextPrimary);
            name.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            column.AddChild(name);

            var rollup = LoadoutRollup.Of(_template);

            var line = new Label
            {
                Text = $"{rollup.Frame.Label.ToUpperInvariant()}   "
                    + $"{_template.FilledSockets}/{rollup.Frame.Sockets.Count}   "
                    + VerdictText.Of(rollup.Verdict),
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            line.AddThemeColorOverride("font_color", VerdictText.Colour(rollup.Verdict));
            line.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            column.AddChild(line);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Pressed?.Invoke();
                AcceptEvent();
            }
        }
    }
}
