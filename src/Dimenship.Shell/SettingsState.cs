namespace Dimenship.Shell;

/// <summary>
/// How the game occupies the display. Three named modes rather than a pair of booleans
/// (fullscreen, borderless): the fourth combination — borderless windowed — is a mode nobody asks
/// for, and a player who picks it by accident gets a window they cannot move.
/// </summary>
public enum WindowMode
{
    Windowed,

    /// <summary>A full-screen window with no decorations. Alt-tabs instantly; shares the GPU.</summary>
    BorderlessFullscreen,

    /// <summary>The display handed to the game alone. Lower latency, slower to alt-tab out of.</summary>
    ExclusiveFullscreen,
}

/// <summary>
/// A window size in pixels. A record struct rather than the engine's vector type because this
/// assembly must not reference an engine, and rather than a bare pair because a settings file that
/// mixed up width and height would be a silent bug rather than a compile error.
/// </summary>
public readonly record struct ScreenSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// What the player chose about how the game is drawn.
/// <para>
/// <see cref="UiScalePermille"/> is permille like every other ratio in this repository — 1000 is
/// 100%. It matters more here than a list of effect toggles would: the console is 11px text laid
/// out against a 1920x1080 design, and on a dense laptop panel that is the first thing a player
/// needs to change.
/// </para>
/// <para>
/// <see cref="FrostedPanels"/> is meaningful only while <see cref="Backdrop"/> is on. The frost is
/// a blur *of* the backdrop image, so with no backdrop there is nothing to blur; the two are kept
/// as separate fields anyway, so that turning the backdrop off and on again restores the frost
/// choice the player actually made instead of silently resetting it.
/// </para>
/// </summary>
public sealed record GraphicsSettings(
    WindowMode Mode,
    ScreenSize Resolution,
    int UiScalePermille,
    bool Backdrop,
    bool FrostedPanels);

/// <summary>
/// What the player chose about volume. Levels are permille of linear amplitude, converted to
/// decibels at the point of use — storing decibels instead would make a slider's travel
/// non-linear in the file and unreadable to anyone editing it by hand.
/// <para>
/// The enabled flags are mute switches, kept separate from the levels for the reason every mixer
/// keeps them separate: muting must not destroy the level the player spent time setting.
/// </para>
/// </summary>
public sealed record SoundSettings(
    int MasterVolumePermille,
    int MusicVolumePermille,
    bool MusicEnabled,
    int EffectsVolumePermille,
    bool EffectsEnabled);

/// <summary>
/// Everything the settings menu can change, and the whole of what <c>user://settings.json</c>
/// holds. Process-wide and not part of a save: these are preferences about this machine, and a
/// world carried to another one must not bring a resolution with it.
/// <para>
/// There is deliberately no gameplay group. The Gameplay tab is a stub, and inventing fields for
/// it now would mean shipping a settings file whose values nothing reads.
/// </para>
/// </summary>
public sealed record SettingsState(GraphicsSettings Graphics, SoundSettings Sound)
{
    /// <summary>
    /// What a player who has never opened this menu gets. The resolution matches the design size
    /// in <c>project.godot</c>, and the volumes start below unity so that the first sound the
    /// game ever plays has somewhere to go up.
    /// </summary>
    public static readonly SettingsState Defaults = new(
        new GraphicsSettings(
            WindowMode.Windowed,
            new ScreenSize(1920, 1080),
            UiScalePermille: 1000,
            Backdrop: true,
            FrostedPanels: true),
        new SoundSettings(
            MasterVolumePermille: 800,
            MusicVolumePermille: 800,
            MusicEnabled: true,
            EffectsVolumePermille: 800,
            EffectsEnabled: true));
}
