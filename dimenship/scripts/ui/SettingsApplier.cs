using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The one place a <see cref="SettingsState"/> reaches the engine. Everything here is a write to
/// <c>DisplayServer</c>, <c>AudioServer</c> or the root viewport; nothing here decides anything,
/// so a question about what a setting means is answered in <see cref="SettingsState"/> and a
/// question about how it takes effect is answered here.
/// </summary>
public static class SettingsApplier
{
    /// <summary>The permille divisor. Named so the bare 1000 never appears in a calculation.</summary>
    private const float Permille = 1000f;

    /// <summary>
    /// False where the operating system owns the window: a web canvas is sized by the page and a
    /// phone has no windows at all. Both the applier and the menu consult this, so a platform that
    /// cannot honour a window setting is not offered one.
    /// </summary>
    public static bool WindowIsPlayerControlled =>
        OS.GetName() is not ("Android" or "iOS" or "Web");

    /// <summary>
    /// The window configuration last written, so an unrelated setting does not rewrite it. This
    /// matters because dragging a volume slider applies the whole state on every mouse-motion
    /// frame: without this the window would be re-centred sixty times a second, dragging itself
    /// back under a player who had just moved it.
    /// </summary>
    private static (WindowMode Mode, ScreenSize Resolution)? _window;

    public static void Apply(SettingsState state, SceneTree tree)
    {
        ApplyGraphics(state.Graphics, tree);
        ApplySound(state.Sound);
    }

    private static void ApplyGraphics(GraphicsSettings graphics, SceneTree tree)
    {
        // The project stretches canvas items, so this scales the whole console — type, chrome,
        // meters and graph alike — rather than resampling a fixed-size image of it.
        tree.Root.ContentScaleFactor = graphics.UiScalePermille / Permille;

        if (!WindowIsPlayerControlled)
        {
            return;
        }

        var window = (graphics.Mode, graphics.Resolution);
        if (_window == window)
        {
            return;
        }

        _window = window;

        switch (graphics.Mode)
        {
            case WindowMode.Windowed:
                // Mode first, then size: a size set while the window is still full-screen is
                // discarded, and the player would see their choice snap back to the display size.
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(
                    new Vector2I(graphics.Resolution.Width, graphics.Resolution.Height));
                Centre();
                break;

            // Godot's Fullscreen is already a borderless full-screen window, so the Borderless
            // flag is not set on top of it. Setting both is how a window ends up undecorated at
            // windowed size — unmovable, unclosable, and reachable only by editing the file.
            case WindowMode.BorderlessFullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;

            case WindowMode.ExclusiveFullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
        }
    }

    /// <summary>
    /// Centres the window on the screen it is already on, within the usable rect so it does not
    /// sit under a taskbar. A window resized about its top-left corner walks off the screen as the
    /// player tries the sizes, which is how a settings menu loses its own OK button.
    /// </summary>
    private static void Centre()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var size = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetPosition(usable.Position + ((usable.Size - size) / 2));
    }

    private static void ApplySound(SoundSettings sound)
    {
        AudioBuses.Ensure();

        // Master carries no mute switch of its own: the level reaching zero is the mute, and a
        // second control that silences everything is a second thing to forget having set.
        SetBus(AudioBuses.Master, sound.MasterVolumePermille, muted: false);
        SetBus(AudioBuses.Music, sound.MusicVolumePermille, !sound.MusicEnabled);
        SetBus(AudioBuses.Effects, sound.EffectsVolumePermille, !sound.EffectsEnabled);
    }

    private static void SetBus(string name, int permille, bool muted)
    {
        var index = AudioBuses.IndexOf(name);
        if (index < 0)
        {
            return;
        }

        // A level of zero mutes rather than converting: LinearToDb(0) is negative infinity, and a
        // bus volume of negative infinity is a value the mixer carries around and prints.
        var silent = permille <= 0;
        AudioServer.SetBusMute(index, muted || silent);
        AudioServer.SetBusVolumeDb(index, silent ? 0f : Mathf.LinearToDb(permille / Permille));
    }
}
