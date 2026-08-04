using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Builds the shell's Godot theme from <see cref="ShellPalette"/>, and supplies the box vocabulary
/// everything in the console is made of: pane, box, card, chip, meter and divider. Each recipe is
/// built here and nowhere else, so a site that wants a rounded bordered surface asks for one by
/// name rather than assembling a <see cref="StyleBoxFlat"/> and choosing its own radius.
/// </summary>
public static class ShellTheme
{
    /// <summary>
    /// The shortest bar a rounded end is legible on. Below it, a <see cref="ShellPalette.RadiusSm"/>
    /// corner rounds away a third of the bar's length at each end and a low fill becomes a lozenge
    /// that never reaches zero width.
    /// </summary>
    private const int MinRoundedBarHeight = 6;

    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = ShellPalette.FontBody };

        theme.SetColor("font_color", "Label", ShellPalette.TextPrimary);

        // Theme uses SetStylebox. AddThemeStyleboxOverride is the Control-level method and does
        // not exist here; SetStyleboxOverride exists on neither.
        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetColor("font_color", "Button", ShellPalette.TextPrimary);
        theme.SetColor("font_hover_color", "Button", ShellPalette.TextTitle);
        theme.SetColor("font_disabled_color", "Button", ShellPalette.TextDim);
        theme.SetStylebox("normal", "Button", Button(ShellPalette.BgPanel));
        theme.SetStylebox("hover", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("pressed", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("disabled", "Button", Button(ShellPalette.BgBase));
        theme.SetStylebox("focus", "Button", Focus(ShellPalette.BgPanel));

        theme.SetConstant("separation", "HBoxContainer", ShellPalette.SpaceMd);
        theme.SetConstant("separation", "VBoxContainer", ShellPalette.SpaceSm);
        theme.SetConstant("separation", "HSplitContainer", ShellPalette.SpaceXs);
        theme.SetConstant("separation", "VSplitContainer", ShellPalette.SpaceXs);

        return theme;
    }

    /// <summary>
    /// Restyles a button that sits over a <see cref="FrostPane"/>: the pane supplies the surface,
    /// so the button's own fills drop out and only mark state. Applied per node rather than in the
    /// theme because glass is a property of where a button sits, not of buttons in general.
    /// </summary>
    public static void ApplyGlass(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Button(Colors.Transparent));
        button.AddThemeStyleboxOverride("hover", Button(ShellPalette.BgGlassHover));
        button.AddThemeStyleboxOverride("pressed", Button(ShellPalette.BgGlassPressed));
        button.AddThemeStyleboxOverride("disabled", Button(Colors.Transparent));
        button.AddThemeStyleboxOverride("focus", Focus(Colors.Transparent));
    }

    /// <summary>
    /// A flat fill with a 1px hairline border and the given corner radius.
    /// <para>
    /// The radius is a parameter rather than a constant because this one recipe serves both pane
    /// chrome and progress-bar fills. A bar under 6px tall must pass zero: <see
    /// cref="ShellPalette.RadiusSm"/> on a 4px bar rounds away a third of its length at each end,
    /// and a bar at 3% fill becomes a lozenge that never reaches zero width.
    /// </para>
    /// </summary>
    public static StyleBoxFlat Surface(Color fill, int radius) =>
        Surface(fill, radius, ShellPalette.Border);

    /// <summary>The top-level frosted container: a pane's own chrome, drawn beneath its frost.</summary>
    public static StyleBoxFlat Pane()
    {
        var box = Surface(ShellPalette.BgPanel, ShellPalette.RadiusLg);
        box.SetContentMarginAll(ShellPalette.Space2Xl);
        return box;
    }

    /// <summary>
    /// A bordered sub-container that is not itself frosted: the legend, a dock box, a grouped
    /// section inside a pane. It does not frost, because a second frosted layer samples the same
    /// backdrop and produces no visible depth — only cost.
    /// </summary>
    public static StyleBoxFlat Box()
    {
        var box = Surface(ShellPalette.BgPanel, ShellPalette.RadiusLg);
        box.SetContentMarginAll(ShellPalette.SpaceLg);
        return box;
    }

    /// <summary>
    /// A graph node's chrome. Selection is a border colour change and nothing else: growth would
    /// shift the card's hit area and reflow its neighbours on a laid-out canvas.
    /// </summary>
    public static StyleBoxFlat Card(bool selected)
    {
        var box = Surface(
            ShellPalette.BgGlass,
            ShellPalette.RadiusLg,
            selected ? ShellPalette.Accent : ShellPalette.Border);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
        return box;
    }

    /// <summary>A small pill: identifier badges, speed multipliers, alert counts. No border.</summary>
    public static StyleBoxFlat Chip(bool active)
    {
        var box = new StyleBoxFlat
        {
            BgColor = active ? ShellPalette.Accent : ShellPalette.Border,
            ContentMarginLeft = ShellPalette.SpaceSm,
            ContentMarginRight = ShellPalette.SpaceSm,
            ContentMarginTop = ShellPalette.SpaceXs,
            ContentMarginBottom = ShellPalette.SpaceXs,
        };
        box.SetCornerRadiusAll(ShellPalette.RadiusMd);
        return box;
    }

    /// <summary>
    /// A bar's trough. Squared below 6px for the same reason its fill is: a curve larger than the
    /// shape carrying it reads as a rendering fault rather than as a style.
    /// </summary>
    public static StyleBoxFlat MeterTrough(int height) =>
        Surface(ShellPalette.BgBase, BarRadius(height));

    /// <summary>A bar's fill, in a single flat state colour. Flat, never a gradient.</summary>
    public static StyleBoxFlat MeterFill(Color color, int height) =>
        Surface(color, BarRadius(height));

    /// <summary>
    /// A 1px rule, square because a 1px rule has no corner to round. It carries no margin of its
    /// own — the surrounding layout owns the space around it.
    /// </summary>
    public static Control Divider() => new ColorRect
    {
        Color = ShellPalette.Border,
        CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static int BarRadius(int height) =>
        height >= MinRoundedBarHeight ? ShellPalette.RadiusSm : 0;

    private static StyleBoxFlat Surface(Color fill, int radius, Color border)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(radius);
        return box;
    }

    private static StyleBoxFlat Panel()
    {
        var box = Surface(ShellPalette.BgPanel, ShellPalette.RadiusLg);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
        return box;
    }

    private static StyleBoxFlat Button(Color fill)
    {
        var box = Surface(fill, ShellPalette.RadiusMd);
        box.SetContentMarginAll(ShellPalette.SpaceSm);
        return box;
    }

    /// <summary>
    /// Focus replaces the resting border rather than adding a ring around it: a ring grows the
    /// control's drawn bounds inside a container that has already laid it out, and the neighbours
    /// shift as the player tabs through them.
    /// </summary>
    private static StyleBoxFlat Focus(Color fill)
    {
        var box = Surface(fill, ShellPalette.RadiusMd, ShellPalette.Accent);
        box.SetContentMarginAll(ShellPalette.SpaceSm);
        return box;
    }
}
