using Godot;

namespace Dimenship.Ui;

/// <summary>Builds the shell's Godot theme from <see cref="ShellPalette"/>.</summary>
public static class ShellTheme
{
    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = ShellPalette.FontBody };

        theme.SetColor("font_color", "Label", ShellPalette.TextPrimary);

        // Theme uses SetStylebox. AddThemeStyleboxOverride is the Control-level method and does
        // not exist here; SetStyleboxOverride exists on neither.
        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetColor("font_color", "Button", ShellPalette.TextPrimary);
        theme.SetColor("font_hover_color", "Button", ShellPalette.StateWarn);
        theme.SetColor("font_disabled_color", "Button", ShellPalette.TextDim);
        theme.SetStylebox("normal", "Button", Button(ShellPalette.BgPanel));
        theme.SetStylebox("hover", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("pressed", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("disabled", "Button", Button(ShellPalette.BgBase));

        theme.SetConstant("separation", "HBoxContainer", ShellPalette.SpaceMd);
        theme.SetConstant("separation", "VBoxContainer", ShellPalette.SpaceSm);
        theme.SetConstant("separation", "HSplitContainer", ShellPalette.SpaceXs);
        theme.SetConstant("separation", "VSplitContainer", ShellPalette.SpaceXs);

        return theme;
    }

    /// <summary>A flat fill with a 1px hairline border and square corners.</summary>
    public static StyleBoxFlat Surface(Color fill)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = ShellPalette.Border,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
        };
        box.SetBorderWidthAll(1);
        return box;
    }

    private static StyleBoxFlat Panel()
    {
        var box = Surface(ShellPalette.BgPanel);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
        return box;
    }

    private static StyleBoxFlat Button(Color fill)
    {
        var box = Surface(fill);
        box.SetContentMarginAll(ShellPalette.SpaceSm);
        return box;
    }
}
