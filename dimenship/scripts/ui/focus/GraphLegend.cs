using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// What the edge colours mean. It sits outside the panned canvas because a key that scrolled away
/// with the graph would be missing exactly when a new player needed it.
/// </summary>
public sealed partial class GraphLegend : PanelContainer
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", ShellTheme.Surface(ShellPalette.BgGlass));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(row);

        Entry(row, FlowBand.Idle);
        Entry(row, FlowBand.Low);
        Entry(row, FlowBand.Normal);
        Entry(row, FlowBand.High);
        Entry(row, FlowBand.Blocked);
    }

    private static void Entry(HBoxContainer row, FlowBand band)
    {
        var label = new Label { Text = GraphCode.Of(band) };
        label.AddThemeColorOverride("font_color", GraphCanvas.ColorOf(band));
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
