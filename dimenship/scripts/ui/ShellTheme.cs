using System.Collections.Generic;
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

    /// <summary>
    /// A slider's track. Its height is the stylebox's own content margins, because that is what
    /// Godot measures a slider's track by — a track styled like a <see cref="MeterTrough"/>, whose
    /// margins are zero, would draw as a 2px hairline.
    /// </summary>
    private const int SliderTrackHeight = 6;

    /// <summary>
    /// The slider's handle, which Godot draws from a texture rather than a stylebox. Square, and
    /// wide enough to grab with a mouse without covering the track's ends at either extreme.
    /// </summary>
    private const int SliderGrabberSize = 10;

    /// <summary>
    /// The shortest distance <see cref="DrawDashedPolyline"/> will walk in one step. The stride
    /// comes out of a modulus, and floating point can hand back a remainder of a few billionths at
    /// a pattern boundary; added to a distance of a couple of thousand pixels that rounds to no
    /// movement at all and the walk never reaches the end of its segment. A hundredth of a pixel is
    /// far below anything drawable and far above a <c>float</c>'s resolution at this scale.
    /// </summary>
    private const float MinDashStep = 0.01f;

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
        // A button draws its icon at the texture's own size, and the icons are imported at
        // svg/scale=2.0 — 80px of artwork in a 26px bar. Capped once here rather than per button,
        // so an icon added to a button anywhere in the shell is the size of the text beside it.
        theme.SetConstant("icon_max_width", "Button", IconSlot.RowSize);

        // A button's icon is white artwork, so it is put on the same colour ramp as the button's
        // label. Left alone it would draw at full white and read brighter than the word beside it.
        theme.SetColor("icon_normal_color", "Button", ShellPalette.TextPrimary);
        theme.SetColor("icon_hover_color", "Button", ShellPalette.TextTitle);
        theme.SetColor("icon_pressed_color", "Button", ShellPalette.TextTitle);
        theme.SetColor("icon_disabled_color", "Button", ShellPalette.TextDim);

        theme.SetStylebox("normal", "Button", Button(ShellPalette.BgPanel));
        theme.SetStylebox("hover", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("pressed", "Button", Button(ShellPalette.Border));
        theme.SetStylebox("disabled", "Button", Button(ShellPalette.BgBase));
        theme.SetStylebox("focus", "Button", Focus(ShellPalette.BgPanel));

        // Sliders are the settings menu's one genuinely new control. OptionButton needs no entry
        // of its own: it derives from Button, and Godot resolves a theme item through the class
        // chain, which is why the menu-bar MenuButtons already wear the styling above.
        theme.SetStylebox("slider", "HSlider", SliderTrack(ShellPalette.BgBase));
        theme.SetStylebox("grabber_area", "HSlider", SliderTrack(ShellPalette.Accent));
        theme.SetStylebox("grabber_area_highlight", "HSlider", SliderTrack(ShellPalette.Accent));
        theme.SetIcon("grabber", "HSlider", Grabber(ShellPalette.Accent));
        theme.SetIcon("grabber_highlight", "HSlider", Grabber(ShellPalette.TextTitle));
        theme.SetIcon("grabber_disabled", "HSlider", Grabber(ShellPalette.TextDim));

        // Without this the handle rides above the track rather than on it.
        theme.SetConstant("center_grabber", "HSlider", 1);

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
    /// <para>
    /// An unbuilt card keeps the fill and gives up the border, because <c>NodeCard</c> draws that
    /// border itself and draws it dashed — see <see cref="DrawDashedPolyline"/>. A solid hairline
    /// underneath a dashed stroke would read as a rendering fault rather than as a slot nothing has
    /// commissioned yet. The <c>built</c> default is what keeps every caller that only cares about
    /// selection — the loadout composer's three — compiling and behaving as it did.
    /// </para>
    /// </summary>
    public static StyleBoxFlat Card(bool selected, bool built = true)
    {
        var box = Surface(
            ShellPalette.BgGlass,
            ShellPalette.RadiusLg,
            selected ? ShellPalette.Accent : ShellPalette.Border,
            built ? 1 : 0);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
        return box;
    }

    /// <summary>
    /// A dashed stroke along a polyline, in <see cref="ShellPalette.DashLength"/> marks separated by
    /// <see cref="ShellPalette.DashGap"/> spaces. The one drawing primitive the shell needed and did
    /// not have, and it lives here beside the <see cref="Grabber"/> texture for the same reason: a
    /// mark drawn at a call site is a length and a colour outside the palette the first time either
    /// moves. Its two callers are an unbuilt card's outline (<c>NodeCard</c>) and an unbuilt route
    /// (<c>GraphCanvas</c>), so a slot and the line into it say the same thing about one condition.
    /// <para>
    /// Godot's own <c>CanvasItem.DrawDashedLine</c> is deliberately not used, and it is worth saying
    /// why so nobody swaps it back in. It takes a single <c>dash</c> length and documents the gap as
    /// being that same length, so the two constants above could not both be honoured through it. It
    /// also restarts its pattern on every call, which over the eight two-pixel segments
    /// <c>GraphCanvas</c> builds each elbow arc from would put a full-length mark on every one of
    /// them and draw a rounded corner solid.
    /// </para>
    /// <para>
    /// The phase is therefore measured along the whole path's arc length rather than reset per
    /// segment: a mark that reaches a corner carries on around it, which is what makes a rectangle a
    /// closed five-point polyline here instead of four separate strokes.
    /// </para>
    /// </summary>
    public static void DrawDashedPolyline(
        CanvasItem node, IReadOnlyList<Vector2> points, Color color, float width)
    {
        const float period = ShellPalette.DashLength + ShellPalette.DashGap;

        var travelled = 0f;

        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var length = from.DistanceTo(to);

            // A repeated point carries no length and no phase, and normalising it would divide by
            // zero. GraphGeometry's polylines can hold one where an elbow had no radius to spare.
            if (length <= 0f)
            {
                continue;
            }

            var direction = (to - from) / length;
            var walked = 0f;

            while (walked < length)
            {
                var phase = Mathf.PosMod(travelled + walked, period);
                var marking = phase < ShellPalette.DashLength;
                var remaining = marking ? ShellPalette.DashLength - phase : period - phase;
                var step = Mathf.Max(Mathf.Min(remaining, length - walked), MinDashStep);

                if (marking)
                {
                    node.DrawLine(
                        from + (direction * walked),
                        from + (direction * Mathf.Min(walked + step, length)),
                        color,
                        width,
                        antialiased: true);
                }

                walked += step;
            }

            travelled += length;
        }
    }

    /// <summary>
    /// A rule block's row in the programming view, and the keyword cap inside it. A <see
    /// cref="Row"/> is the wrong recipe: a block is not a label-and-value pair, it carries its
    /// category in its fill, and it is short enough that a <see cref="Box"/>'s inset would leave no
    /// room for the slots.
    /// <para>
    /// Highlighted covers both selection and focus, and is a border colour change only — a block
    /// that grew when selected would shift its own hit area and reflow every row beneath it.
    /// </para>
    /// </summary>
    public static StyleBoxFlat Block(Color fill, bool highlighted)
    {
        var box = Surface(
            fill,
            ShellPalette.RadiusMd,
            highlighted ? ShellPalette.Accent : ShellPalette.Border);

        box.ContentMarginLeft = ShellPalette.SpaceMd;
        box.ContentMarginRight = ShellPalette.SpaceMd;
        box.ContentMarginTop = ShellPalette.SpaceXs;
        box.ContentMarginBottom = ShellPalette.SpaceXs;
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
    /// A popover's chrome: the loadout composer's fitting picker and frame chooser. An opaque panel
    /// fill rather than glass, because a popover sits over the glowing frame art and a translucent
    /// one would put line art behind its text. The accent border ties it to the box it opened from,
    /// which is selected in the same colour.
    /// </summary>
    public static StyleBoxFlat Popover()
    {
        var box = Surface(ShellPalette.BgPanel, ShellPalette.RadiusLg, ShellPalette.Accent);
        box.SetContentMarginAll(ShellPalette.SpaceMd);
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

    /// <summary>A 1px vertical rule, for sections laid out side by side. The same square, marginless rule as <see cref="Divider"/>.</summary>
    public static Control VerticalDivider() => new ColorRect
    {
        Color = ShellPalette.Border,
        CustomMinimumSize = new Vector2(1, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static StyleBoxFlat SliderTrack(Color fill)
    {
        var box = Surface(fill, ShellPalette.RadiusSm);
        box.ContentMarginTop = SliderTrackHeight / 2;
        box.ContentMarginBottom = SliderTrackHeight / 2;
        return box;
    }

    /// <summary>
    /// A flat square in one palette colour, for the one place Godot insists on a texture. Built
    /// here rather than shipped as an asset so the handle cannot drift off the palette the way an
    /// SVG with a colour baked into it would.
    /// </summary>
    private static ImageTexture Grabber(Color color)
    {
        var image = Image.CreateEmpty(SliderGrabberSize, SliderGrabberSize, false, Image.Format.Rgba8);
        image.Fill(color);
        return ImageTexture.CreateFromImage(image);
    }

    private static int BarRadius(int height) =>
        height >= MinRoundedBarHeight ? ShellPalette.RadiusSm : 0;

    /// <summary>
    /// The one recipe every box here is built from. <paramref name="borderWidth"/> defaults to the
    /// hairline everything wears, and exists so <see cref="Card"/> can ask for none at all without a
    /// second recipe to keep in step with this one.
    /// </summary>
    private static StyleBoxFlat Surface(Color fill, int radius, Color border, int borderWidth = 1)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
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
