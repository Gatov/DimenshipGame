using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Fixed-width selector strip: focus views on top, zone toggles beneath.</summary>
public sealed partial class Rail : VBoxContainer
{
    private readonly PanelRegistry _registry;
    private readonly ShellActions _actions;
    private readonly Dictionary<PanelId, Button> _focusButtons = new();

    private Button _inspectorToggle = null!;
    private Button _consoleToggle = null!;

    public Rail(PanelRegistry registry, ShellActions actions)
    {
        _registry = registry;
        _actions = actions;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(150, 0);
        AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        AddChild(Heading("FOCUS"));

        foreach (var descriptor in _registry.OfKind(ZoneKind.Focus).OrderBy(d => d.Title))
        {
            var id = descriptor.Id;
            var button = new Button
            {
                Text = descriptor.Title,
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => _actions.FocusRequested?.Invoke(id);
            _focusButtons[id] = button;
            AddChild(button);
        }

        AddChild(Heading("ZONES"));

        _inspectorToggle = new Button { Alignment = HorizontalAlignment.Left };
        _inspectorToggle.Pressed += () => _actions.InspectorToggled?.Invoke();
        AddChild(_inspectorToggle);

        _consoleToggle = new Button { Alignment = HorizontalAlignment.Left };
        _consoleToggle.Pressed += () => _actions.ConsoleToggled?.Invoke();
        AddChild(_consoleToggle);

        Glass();
    }

    /// <summary>
    /// Frosts the rail's buttons. One pane for all of them rather than one each: they share a
    /// material and a coordinate space, and a single node keeps the rail's own gaps unfrosted, so
    /// the backdrop stays sharp between the buttons.
    /// </summary>
    private void Glass()
    {
        if (ShellBackdrop.CreateFrostMaterial() is not { } frost)
        {
            return;
        }

        var buttons = _focusButtons.Values.Append(_inspectorToggle).Append(_consoleToggle).ToArray();
        foreach (var button in buttons)
        {
            ShellTheme.ApplyGlass(button);
        }

        // Ahead of the buttons in child order, because that is the draw order: the pane lays down
        // the frosted surface, then each button draws its border and label over it.
        var pane = new FrostPane(buttons)
        {
            Name = "FrostPane",
            Material = frost,
            Radius = ShellPalette.RadiusMd,
        };
        AddChild(pane);
        MoveChild(pane, 0);
    }

    public void SetActive(PanelId active)
    {
        foreach (var (id, button) in _focusButtons)
        {
            button.AddThemeColorOverride(
                "font_color", id == active ? ShellPalette.StateWarn : ShellPalette.TextDim);
        }
    }

    public void SetZoneState(bool inspectorVisible, bool consoleVisible)
    {
        _inspectorToggle.Text = inspectorVisible ? "Inspector \u2713" : "Inspector";
        _consoleToggle.Text = consoleVisible ? "Console \u2713" : "Console";
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
    }
}
