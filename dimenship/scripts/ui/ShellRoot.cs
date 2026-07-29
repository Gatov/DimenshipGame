using Godot;

namespace Dimenship.Ui;

/// <summary>Root of the shell scene. Builds the whole interface in code.</summary>
public sealed partial class ShellRoot : Control
{
    private SimulationDriver _driver = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _driver = new SimulationDriver { Name = "SimulationDriver" };
        AddChild(_driver);

        Theme = ShellTheme.Build();

        var background = new ColorRect
        {
            Color = ShellPalette.BgBase,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var column = new VBoxContainer();
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(column);

        var centre = new Label
        {
            Text = "Shell frame — panels arrive in Task 5.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(centre);

        column.AddChild(new StatusBar(_driver) { Name = "StatusBar" });
    }
}
