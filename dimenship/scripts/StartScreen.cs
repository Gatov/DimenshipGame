using Dimenship.Core;
using Godot;

namespace Dimenship;

public partial class StartScreen : Control
{
    private const string GameScenePath = "res://scenes/Shell.tscn";

    private Label _titleLabel = null!;
    private Label _versionLabel = null!;
    private Button _playButton = null!;
    private Button _quitButton = null!;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");
        _versionLabel = GetNode<Label>("CenterContainer/VBoxContainer/VersionLabel");
        _playButton = GetNode<Button>("CenterContainer/VBoxContainer/PlayButton");
        _quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");

        _titleLabel.Text = GameInfo.Title;
        _versionLabel.Text = GameInfo.DisplayVersion;

        _playButton.Pressed += OnPlayPressed;
        _quitButton.Pressed += OnQuitPressed;

        // Platforms where the OS owns app lifetime have no business showing a Quit button.
        _quitButton.Visible = OS.GetName() is not ("Android" or "iOS" or "Web");

        _playButton.GrabFocus();
    }

    private void OnPlayPressed()
    {
        var error = GetTree().ChangeSceneToFile(GameScenePath);
        if (error != Error.Ok)
        {
            GD.PushError($"Failed to load {GameScenePath}: {error}");
        }
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
