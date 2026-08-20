using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The background music. One looping track into the <see cref="AudioBuses.Music"/> bus, playing
/// for the life of the process.
/// <para>
/// An autoload rather than a node in a scene, which is the opposite of the call
/// <see cref="Settings"/> makes, and for the opposite reason: music is a node — it needs a place
/// in the tree to play from — and it has to survive <c>ChangeSceneToFile</c>, because the track
/// restarting from the top every time the player leaves the start screen is exactly the seam a
/// score exists to hide.
/// </para>
/// <para>
/// The Sound settings reach this through the mixer and never through this node: disabling music
/// mutes the bus while the track plays on underneath, so re-enabling it resumes where the score
/// would have been rather than restarting it. That is why nothing here subscribes to
/// <see cref="Settings.Changed"/> — there is no state here for a setting to change.
/// </para>
/// </summary>
public partial class MusicPlayer : Node
{
    /// <summary>
    /// The one track the game has. A single constant rather than a playlist because there is one
    /// piece of music; a track list is a content decision and belongs in content when there is
    /// something to choose between.
    /// </summary>
    private const string TrackPath = "res://assets/audio/gravity_between_stars.mp3";

    private AudioStreamPlayer? _player;

    public override void _Ready()
    {
        // As an autoload this is the first node in the tree, ahead of whichever scene runs. The
        // engine is still on project.godot's audio values at this point, so the settings are
        // applied here before anything sounds: starting the track first would play a fraction of
        // a second at full volume to a player who had turned music off.
        Settings.ApplyTo(GetTree());

        var stream = ResourceLoader.Load<AudioStream>(TrackPath);
        if (stream is null)
        {
            // Same stance as ShellBackdrop: a missing asset leaves the game silent and says so,
            // rather than taking the process down over a file nobody can hear.
            GD.PushWarning($"Music track {TrackPath} is missing; the game will run silent.");
            return;
        }

        // Set in code as well as in the import, so a re-import that loses the flag is an audible
        // gap rather than a track that plays once and never again.
        if (stream is AudioStreamMP3 mp3)
        {
            mp3.Loop = true;
        }

        _player = new AudioStreamPlayer
        {
            Name = "Track",
            Stream = stream,
            Bus = AudioBuses.Music,
        };
        AddChild(_player);
        _player.Play();
    }
}
