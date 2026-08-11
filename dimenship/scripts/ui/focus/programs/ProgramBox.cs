using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The box vocabulary's second primitive, assembled: a bordered, unfrosted sub-container with a
/// dim uppercase header and a body to fill. Three columns of the programming view want one, and a
/// fourth copy of the same six lines is how a vocabulary stops being one.
/// </summary>
public static class ProgramBox
{
    public static PanelContainer Create(string header, out VBoxContainer body)
    {
        var box = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        box.AddChild(column);

        var title = new Label { Text = header.ToUpperInvariant() };
        title.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        column.AddChild(title);

        body = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        column.AddChild(body);

        return box;
    }

    /// <summary>A label-and-value line: dim uppercase label, bright value, baseline aligned.</summary>
    public static Control Row(string label, string value, Color? colour = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var name = new Label { Text = label.ToUpperInvariant() };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(name);

        var reading = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        reading.AddThemeColorOverride("font_color", colour ?? ShellPalette.TextTitle);
        reading.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(reading);

        return row;
    }
}
