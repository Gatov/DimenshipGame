using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What the edge colours mean: one row per band, a stroke in the band's own colour beside the word
/// for it. It sits outside the panned canvas because a key that scrolled away with the graph would
/// be missing exactly when a new player needed it.
/// </summary>
public sealed partial class GraphLegend : PanelContainer
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        AddChild(column);

        var heading = new Label { Text = "FLOW" };
        heading.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(heading);

        Entry(column, FlowBand.Idle);
        Entry(column, FlowBand.Low);
        Entry(column, FlowBand.Normal);
        Entry(column, FlowBand.High);
        Entry(column, FlowBand.Blocked);
    }

    /// <summary>A sample of the stroke itself, at the width edges are drawn at, then its name.</summary>
    private static void Entry(VBoxContainer column, FlowBand band)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(row);

        row.AddChild(new ColorRect
        {
            Color = GraphCanvas.ColorOf(band),
            CustomMinimumSize = new Vector2(24, 2),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var label = new Label { Text = GraphCode.Of(band) };
        label.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(label);
    }
}

/// <summary>
/// The short word for a flow band. One place, so the legend and the labels drawn along the edges
/// cannot drift apart and describe the same colour differently.
/// </summary>
public static class GraphCode
{
    public static string Of(FlowBand band) => band switch
    {
        FlowBand.Low => "LOW",
        FlowBand.Normal => "NORMAL",
        FlowBand.High => "HIGH",
        FlowBand.Blocked => "BLOCKED",
        _ => "IDLE",
    };
}
