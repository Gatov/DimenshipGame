using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Horizontal selector strip for focus views.</summary>
public sealed partial class Rail : HBoxContainer
{
    private readonly PanelRegistry _registry;
    private readonly ShellActions _actions;
    private readonly Dictionary<PanelId, Button> _focusButtons = new();

    public Rail(PanelRegistry registry, ShellActions actions)
    {
        _registry = registry;
        _actions = actions;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 26);
        AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        foreach (var descriptor in _registry.OfKind(ZoneKind.Focus).OrderBy(d => d.Title))
        {
            var id = descriptor.Id;
            // A preview says so on the rail itself, before it is opened (UX-08). The title stays
            // the descriptor's own, because FocusOrder sorts by it and the accelerators follow.
            var button = new Button
            {
                Text = descriptor.PreviewNote is null ? descriptor.Title : $"{descriptor.Title} · PREVIEW",
                TooltipText = descriptor.PreviewNote ?? string.Empty,
            };
            button.Pressed += () => _actions.FocusRequested.Invoke(id);
            _focusButtons[id] = button;
            AddChild(button);
        }

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

        foreach (var button in _focusButtons.Values)
        {
            ShellTheme.ApplyGlass(button);
        }

        // Ahead of the buttons in child order, because that is the draw order: the pane lays down
        // the frosted surface, then each button draws its border and label over it.
        var pane = new FrostPane(_focusButtons.Values.ToArray())
        {
            Name = "FrostPane",
            Material = frost,
            Radius = ShellPalette.RadiusMd,
        };
        AddChild(pane);
        MoveChild(pane, 0);
    }

    /// <summary>
    /// The active view in <see cref="ShellPalette.Accent"/>, the others in readable text (UX-06).
    /// The active entry used to be drawn in <see cref="ShellPalette.StateWarn"/>, which taught that
    /// selection means warning, the exact lesson <c>Accent</c> was introduced to stop; and the
    /// others in <see cref="ShellPalette.TextDim"/>, which is for what can be ignored, not for a
    /// control.
    /// </summary>
    public void SetActive(PanelId active)
    {
        foreach (var (id, button) in _focusButtons)
        {
            button.AddThemeColorOverride(
                "font_color", id == active ? ShellPalette.Accent : ShellPalette.TextPrimary);
        }
    }

}
