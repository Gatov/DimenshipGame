using Dimenship.Core;
using Dimenship.Ui;
using Godot;

namespace Dimenship;

public partial class StartScreen : Control
{
    private const string GameScenePath = "res://scenes/Shell.tscn";

    private Label _titleLabel = null!;
    private Label _versionLabel = null!;
    private Button _playButton = null!;
    private Button _settingsButton = null!;
    private Button _quitButton = null!;
    private SettingsOverlay? _settings;

    public override void _Ready()
    {
        // The first scene the process shows, so this is where the engine stops running on the
        // values in project.godot and starts running on the player's.
        Settings.ApplyTo(GetTree());

        _titleLabel = GetNode<Label>("CenterContainer/VBoxContainer/TitleLabel");
        _versionLabel = GetNode<Label>("CenterContainer/VBoxContainer/VersionLabel");
        _playButton = GetNode<Button>("CenterContainer/VBoxContainer/PlayButton");
        _settingsButton = GetNode<Button>("CenterContainer/VBoxContainer/SettingsButton");
        _quitButton = GetNode<Button>("CenterContainer/VBoxContainer/QuitButton");

        _titleLabel.Text = GameInfo.Title;
        _versionLabel.Text = GameInfo.DisplayVersion;

        _playButton.Pressed += OnPlayPressed;
        _settingsButton.Pressed += OnSettingsPressed;
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

    /// <summary>
    /// Opens the same menu the shell's Vessel menu opens. The overlay carries the shell's theme
    /// with it, which is what lets this screen — which has none of its own — show it unchanged.
    /// </summary>
    private void OnSettingsPressed()
    {
        if (_settings is not null)
        {
            return;
        }

        _settings = SettingsOverlay.Open(this);
        _settings.Closed += () =>
        {
            _settings = null;
            // Focus returns to the button the player left, not to Play: a menu that moves the
            // keyboard somewhere else on the way out loses whoever was navigating by keyboard.
            _settingsButton.GrabFocus();
        };
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
