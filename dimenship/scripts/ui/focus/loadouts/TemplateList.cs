using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The composer's left column: one card per template — its frame drawn small, its name, and where
/// it stands — and a way to add one. The card's micro line carries what the removed <i>Selected
/// Template Info</i> box carried (frame fill and verdict); the template's description is the
/// card's tooltip.
/// </summary>
public sealed partial class TemplateList : VBoxContainer
{
    private const int ThumbnailHeight = 72;

    /// <summary>A quarter of the canvas, rasterised once per frame and cached: 300×180, above the card's size.</summary>
    private const int ThumbnailScalePermille = 250;

    private VBoxContainer _rows = null!;

    /// <summary>Raised with the index of the template the player picked.</summary>
    public Action<int>? Chosen { get; set; }

    public Action? NewRequested { get; set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        CustomMinimumSize = new Vector2(220, 0);

        AddChild(BoxSection.Create("Loadouts", out _rows));

        var add = new Button { Text = "+ NEW TEMPLATE" };
        ShellTheme.ApplyGlass(add);
        add.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        add.Pressed += () => NewRequested?.Invoke();
        AddChild(add);
    }

    public void Refresh(IReadOnlyList<LoadoutDraft> templates, int selected)
    {
        if (!IsNodeReady())
        {
            return;
        }

        Clear(_rows);

        for (var i = 0; i < templates.Count; i++)
        {
            var index = i;
            _rows.AddChild(new Entry(templates[i], i == selected)
            {
                Pressed = () => Chosen?.Invoke(index),
            });
        }
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
            TooltipText = _template.Description;

            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            var rollup = LoadoutRollup.Of(_template);

            // The same frame art the stage draws, small, tinted, and without the glow: a column of
            // glowing machines would compete with the one on the stage.
            column.AddChild(new TextureRect
            {
                Texture = FrameArtLibrary.Rasterise(rollup.Frame, ThumbnailScalePermille),
                CustomMinimumSize = new Vector2(0, ThumbnailHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = ShellPalette.Projection,
                MouseFilter = MouseFilterEnum.Ignore,
            });

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

            var line = new Label
            {
                Text = $"{_template.FilledSockets}/{rollup.Frame.Sockets.Count} · {VerdictText.Of(rollup.Verdict)}",
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
