using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The box vocabulary's second primitive, assembled: a bordered, unfrosted sub-container with a
/// dim uppercase header and a body to fill. Three columns of the programming view want one and
/// four places in the loadout composer want another, and a fifth copy of the same six lines is how
/// a vocabulary stops being one.
/// </summary>
public static class BoxSection
{
    public static PanelContainer Create(string header, out VBoxContainer body)
    {
        var box = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", ShellTheme.Box());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        box.AddChild(column);

        var title = ShellTheme.Text(header.ToUpperInvariant(), ShellTheme.Caption);
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

        var name = ShellTheme.Text(label.ToUpperInvariant(), ShellTheme.Caption);
        row.AddChild(name);

        var reading = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ThemeTypeVariation = ShellTheme.Value,
        };
        reading.AddThemeColorOverride("font_color", colour ?? ShellPalette.TextTitle);
        row.AddChild(reading);

        return row;
    }
}
