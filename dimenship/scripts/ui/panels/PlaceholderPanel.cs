using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// States plainly what a not-yet-built surface will become. Used for the four registered focus
/// views and for the fault surface shown when a panel identifier cannot be resolved.
/// </summary>
public sealed partial class PlaceholderPanel : PanelBase
{
    private readonly string _body;
    private readonly Color _accent;

    public PlaceholderPanel(PanelId id, string title, string body, ZoneKind zone, Color? accent = null)
    {
        Id = id;
        Title = title;
        Zone = zone;
        _body = body;
        _accent = accent ?? ShellPalette.TextDim;
    }

    public override PanelId Id { get; }

    public override string Title { get; }

    public ZoneKind Zone { get; }

    public override void _Ready()
    {
        var centre = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(centre);

        var column = new VBoxContainer();
        centre.AddChild(column);

        var heading = new Label
        {
            Text = Title.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeColorOverride("font_color", _accent);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        column.AddChild(heading);

        // No CustomMinimumSize here: a Label with autowrap enabled reports a minimal horizontal
        // minimum size on its own, so it wraps within whatever width the host zone actually has
        // instead of forcing that zone (and its siblings across a split) wide enough to fit one
        // unwrapped line.
        var body = new Label
        {
            Text = _body,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        column.AddChild(body);
    }

    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        // Nothing to show yet. Overriding with an empty body is the honest implementation.
    }
}
