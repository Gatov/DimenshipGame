using System.Text.Json;

namespace Dimenship.Shell;

/// <summary>Outcome of loading settings. Always carries a usable state, however bad the input was.</summary>
public sealed record SettingsLoadResult(SettingsState State, IReadOnlyList<string> Warnings, bool UsedDefault);

/// <summary>
/// Reads and writes <see cref="SettingsState"/> as JSON, on the same contract as
/// <see cref="LayoutSerializer"/>: every degraded input produces a valid state plus warnings
/// rather than an exception. A settings file the player edited into nonsense must never be the
/// reason the game will not start — and a settings file is the one file a player is most likely
/// to open in a text editor.
/// <para>
/// Every DTO field is nullable, which <see cref="LayoutSerializer"/>'s numeric fields are not.
/// The difference is that a missing number here has a plausible-looking wrong answer: a missing
/// volume would deserialize to zero, and the player would get a silently muted game rather than a
/// reported problem.
/// </para>
/// <para>
/// The window mode is written and parsed as its own name rather than as its ordinal. Reordering
/// the enum would otherwise re-point every existing settings file, and the file is meant to be
/// legible to whoever opens it.
/// </para>
/// </summary>
public static class SettingsSerializer
{
    public const int MinVolumePermille = 0;
    public const int MaxVolumePermille = 1000;

    /// <summary>
    /// The UI scale a player may choose. The floor is not zero: the console's body type is 11px,
    /// and a scale that could shrink it further would let a player render the settings menu — the
    /// only way back — unreadable. The ceiling leaves the 1920x1080 layout usable at 1280x720.
    /// </summary>
    public const int MinUiScalePermille = 800;

    public const int MaxUiScalePermille = 1500;

    /// <summary>
    /// Window dimension bounds. The floor is a window the menu still fits in; the ceiling is
    /// beyond any display that exists and is there to reject a garbage number, not to judge one.
    /// </summary>
    public const int MinDimension = 640;

    public const int MaxDimension = 16384;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private sealed record GraphicsDto(
        string? Mode,
        int? Width,
        int? Height,
        int? UiScalePermille,
        bool? Backdrop,
        bool? FrostedPanels);

    private sealed record SoundDto(
        int? MasterVolumePermille,
        int? MusicVolumePermille,
        bool? MusicEnabled,
        int? EffectsVolumePermille,
        bool? EffectsEnabled);

    private sealed record Dto(GraphicsDto? Graphics, SoundDto? Sound);

    public static string ToJson(SettingsState state) =>
        JsonSerializer.Serialize(
            new Dto(
                new GraphicsDto(
                    state.Graphics.Mode.ToString(),
                    state.Graphics.Resolution.Width,
                    state.Graphics.Resolution.Height,
                    state.Graphics.UiScalePermille,
                    state.Graphics.Backdrop,
                    state.Graphics.FrostedPanels),
                new SoundDto(
                    state.Sound.MasterVolumePermille,
                    state.Sound.MusicVolumePermille,
                    state.Sound.MusicEnabled,
                    state.Sound.EffectsVolumePermille,
                    state.Sound.EffectsEnabled)),
            Options);

    public static SettingsLoadResult Load(string? json, SettingsState defaults)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SettingsLoadResult(defaults, Array.Empty<string>(), UsedDefault: true);
        }

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json);
        }
        catch (JsonException e)
        {
            return new SettingsLoadResult(defaults, new[] { $"settings file is not valid JSON: {e.Message}" }, true);
        }

        if (dto is null)
        {
            return new SettingsLoadResult(defaults, new[] { "settings file deserialized to null" }, true);
        }

        var warnings = new List<string>();

        return new SettingsLoadResult(
            new SettingsState(
                ReadGraphics(dto.Graphics, defaults.Graphics, warnings),
                ReadSound(dto.Sound, defaults.Sound, warnings)),
            warnings,
            UsedDefault: false);
    }

    /// <summary>
    /// A missing group warns once and takes every default, rather than warning per field. An older
    /// file that predates a whole group should read as one thing that is absent, not as five.
    /// </summary>
    private static GraphicsSettings ReadGraphics(
        GraphicsDto? dto, GraphicsSettings defaults, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add("settings file has no graphics section; using defaults for all of it");
            return defaults;
        }

        return new GraphicsSettings(
            ReadMode(dto.Mode, defaults.Mode, warnings),
            new ScreenSize(
                Bounded(dto.Width, "Width", MinDimension, MaxDimension, defaults.Resolution.Width, warnings),
                Bounded(dto.Height, "Height", MinDimension, MaxDimension, defaults.Resolution.Height, warnings)),
            Bounded(
                dto.UiScalePermille,
                "UiScalePermille",
                MinUiScalePermille,
                MaxUiScalePermille,
                defaults.UiScalePermille,
                warnings),
            Flag(dto.Backdrop, "Backdrop", defaults.Backdrop, warnings),
            Flag(dto.FrostedPanels, "FrostedPanels", defaults.FrostedPanels, warnings));
    }

    private static SoundSettings ReadSound(SoundDto? dto, SoundSettings defaults, List<string> warnings)
    {
        if (dto is null)
        {
            warnings.Add("settings file has no sound section; using defaults for all of it");
            return defaults;
        }

        return new SoundSettings(
            Volume(dto.MasterVolumePermille, "MasterVolumePermille", defaults.MasterVolumePermille, warnings),
            Volume(dto.MusicVolumePermille, "MusicVolumePermille", defaults.MusicVolumePermille, warnings),
            Flag(dto.MusicEnabled, "MusicEnabled", defaults.MusicEnabled, warnings),
            Volume(dto.EffectsVolumePermille, "EffectsVolumePermille", defaults.EffectsVolumePermille, warnings),
            Flag(dto.EffectsEnabled, "EffectsEnabled", defaults.EffectsEnabled, warnings));
    }

    private static WindowMode ReadMode(string? value, WindowMode fallback, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            warnings.Add($"settings named no window mode; using '{fallback}'");
            return fallback;
        }

        // IsDefined after TryParse: TryParse accepts an ordinal spelled as digits, so "17" would
        // otherwise succeed and produce a WindowMode no switch has a case for.
        if (!Enum.TryParse<WindowMode>(value, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            warnings.Add($"settings names unknown window mode '{value}'; using '{fallback}'");
            return fallback;
        }

        return mode;
    }

    private static int Volume(int? value, string name, int fallback, List<string> warnings) =>
        Bounded(value, name, MinVolumePermille, MaxVolumePermille, fallback, warnings);

    private static int Bounded(int? value, string name, int min, int max, int fallback, List<string> warnings)
    {
        if (value is not { } number)
        {
            warnings.Add($"settings named no {name}; using {fallback}");
            return fallback;
        }

        if (number >= min && number <= max)
        {
            return number;
        }

        var clamped = Math.Clamp(number, min, max);
        warnings.Add($"{name} {number} is out of range; clamped to {clamped}");
        return clamped;
    }

    private static bool Flag(bool? value, string name, bool fallback, List<string> warnings)
    {
        if (value is { } flag)
        {
            return flag;
        }

        warnings.Add($"settings named no {name}; using {fallback}");
        return fallback;
    }
}
