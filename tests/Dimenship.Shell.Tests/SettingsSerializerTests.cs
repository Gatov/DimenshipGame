using NUnit.Framework;

namespace Dimenship.Shell.Tests;

public class SettingsSerializerTests
{
    private static readonly SettingsState Defaults = SettingsState.Defaults;

    [Test]
    public void RoundTrip_PreservesEveryField()
    {
        var original = new SettingsState(
            new GraphicsSettings(
                WindowMode.ExclusiveFullscreen,
                new ScreenSize(2560, 1440),
                UiScalePermille: 1250,
                Backdrop: false,
                FrostedPanels: false),
            new SoundSettings(
                MasterVolumePermille: 640,
                MusicVolumePermille: 0,
                MusicEnabled: false,
                EffectsVolumePermille: 1000,
                EffectsEnabled: true));

        var result = SettingsSerializer.Load(SettingsSerializer.ToJson(original), Defaults);

        Assert.That(result.State, Is.EqualTo(original));
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(result.UsedDefault, Is.False);
    }

    [Test]
    public void ToJson_WritesTheWindowModeByName()
    {
        var json = SettingsSerializer.ToJson(Defaults with
        {
            Graphics = Defaults.Graphics with { Mode = WindowMode.BorderlessFullscreen },
        });

        Assert.That(json, Does.Contain("BorderlessFullscreen"), "reordering the enum must not re-point saved files");
    }

    [Test]
    public void Load_NullJson_UsesDefaultsWithoutComplaining()
    {
        var result = SettingsSerializer.Load(null, Defaults);

        Assert.That(result.State, Is.EqualTo(Defaults));
        Assert.That(result.UsedDefault, Is.True);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Load_UnparseableJson_UsesDefaultsAndWarns()
    {
        var result = SettingsSerializer.Load("{ this is not json", Defaults);

        Assert.That(result.State, Is.EqualTo(Defaults));
        Assert.That(result.UsedDefault, Is.True);
        Assert.That(result.Warnings, Is.Not.Empty);
    }

    [Test]
    public void Load_UnknownWindowMode_FallsBackForThatFieldOnly()
    {
        const string json = """
            {
              "Graphics": {
                "Mode": "Holographic",
                "Width": 1600, "Height": 900,
                "UiScalePermille": 1100,
                "Backdrop": true, "FrostedPanels": false
              },
              "Sound": {
                "MasterVolumePermille": 500, "MusicVolumePermille": 500, "MusicEnabled": true,
                "EffectsVolumePermille": 500, "EffectsEnabled": true
              }
            }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(result.State.Graphics.Mode, Is.EqualTo(Defaults.Graphics.Mode));
        Assert.That(result.State.Graphics.Resolution, Is.EqualTo(new ScreenSize(1600, 900)), "other fields must survive");
        Assert.That(result.State.Graphics.UiScalePermille, Is.EqualTo(1100), "other fields must survive");
        Assert.That(result.State.Graphics.FrostedPanels, Is.False, "other fields must survive");
        Assert.That(result.Warnings.Count(w => w.Contains("Holographic")), Is.EqualTo(1));
        Assert.That(result.UsedDefault, Is.False);
    }

    [Test]
    public void Load_WindowModeSpelledAsAnOrdinal_IsRejected()
    {
        const string json = """
            { "Graphics": { "Mode": "17" }, "Sound": {} }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(result.State.Graphics.Mode, Is.EqualTo(Defaults.Graphics.Mode));
        Assert.That(result.Warnings.Count(w => w.Contains("17")), Is.EqualTo(1));
    }

    [Test]
    public void Load_AbsurdVolume_IsClamped()
    {
        const string json = """
            {
              "Sound": {
                "MasterVolumePermille": 9000, "MusicVolumePermille": -400, "MusicEnabled": true,
                "EffectsVolumePermille": 300, "EffectsEnabled": false
              }
            }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(result.State.Sound.MasterVolumePermille, Is.EqualTo(SettingsSerializer.MaxVolumePermille));
        Assert.That(result.State.Sound.MusicVolumePermille, Is.EqualTo(SettingsSerializer.MinVolumePermille));
        Assert.That(result.State.Sound.EffectsVolumePermille, Is.EqualTo(300), "an in-range level must survive");
        Assert.That(result.Warnings.Count(w => w.Contains("out of range")), Is.EqualTo(2));
    }

    [Test]
    public void Load_UiScaleBelowTheFloor_IsClamped()
    {
        const string json = """
            { "Graphics": { "Mode": "Windowed", "Width": 1920, "Height": 1080,
              "UiScalePermille": 10, "Backdrop": true, "FrostedPanels": true } }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(
            result.State.Graphics.UiScalePermille,
            Is.EqualTo(SettingsSerializer.MinUiScalePermille),
            "a scale that could shrink the settings menu out of legibility must not be reachable");
    }

    [Test]
    public void Load_AbsurdResolution_IsClamped()
    {
        const string json = """
            { "Graphics": { "Mode": "Windowed", "Width": 4, "Height": 999999,
              "UiScalePermille": 1000, "Backdrop": true, "FrostedPanels": true } }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(
            result.State.Graphics.Resolution,
            Is.EqualTo(new ScreenSize(SettingsSerializer.MinDimension, SettingsSerializer.MaxDimension)));
        Assert.That(result.Warnings.Count(w => w.Contains("out of range")), Is.EqualTo(2));
    }

    [Test]
    public void Load_MissingSection_FallsBackOnceRatherThanPerField()
    {
        const string json = """
            { "Graphics": { "Mode": "Windowed", "Width": 1280, "Height": 720,
              "UiScalePermille": 1000, "Backdrop": true, "FrostedPanels": true } }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(result.State.Sound, Is.EqualTo(Defaults.Sound));
        Assert.That(result.State.Graphics.Resolution, Is.EqualTo(new ScreenSize(1280, 720)));
        Assert.That(result.Warnings, Has.Count.EqualTo(1), "an absent group is one absence, not five");
    }

    [Test]
    public void Load_MissingVolume_FallsBackAndWarnsRatherThanMuting()
    {
        const string json = """
            { "Sound": { "MusicVolumePermille": 500, "MusicEnabled": true,
              "EffectsVolumePermille": 500, "EffectsEnabled": true } }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(
            result.State.Sound.MasterVolumePermille,
            Is.EqualTo(Defaults.Sound.MasterVolumePermille),
            "a missing level must not deserialize to silence");
        Assert.That(result.Warnings.Count(w => w.Contains("MasterVolumePermille")), Is.EqualTo(1));
    }

    [Test]
    public void Load_UnknownField_IsIgnoredRatherThanFatal()
    {
        const string json = """
            { "Graphics": { "Mode": "Windowed", "Width": 1920, "Height": 1080,
              "UiScalePermille": 1000, "Backdrop": true, "FrostedPanels": true,
              "Bloom": true },
              "Sound": { "MasterVolumePermille": 800, "MusicVolumePermille": 800, "MusicEnabled": true,
              "EffectsVolumePermille": 800, "EffectsEnabled": true } }
            """;

        var result = SettingsSerializer.Load(json, Defaults);

        Assert.That(result.State, Is.EqualTo(Defaults));
        Assert.That(
            result.UsedDefault,
            Is.False,
            "a settings file from a newer build must still open on an older one");
    }
}
