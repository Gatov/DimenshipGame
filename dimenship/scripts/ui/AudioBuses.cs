using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Names the mixer buses the Sound settings drive, and guarantees they exist.
/// <para>
/// Nothing plays into them yet — the game is silent, and this is the first audio code in the
/// repository. They are created now because a volume setting has to have somewhere to land: a
/// slider that writes to a bus which does not exist is a slider that quietly does nothing, and
/// the alternative (a Sound tab that persists numbers nothing reads) is the fake the rest of this
/// menu avoids.
/// </para>
/// <para>
/// The layout ships as <c>res://default_bus_layout.tres</c>, which Godot loads on startup.
/// <see cref="Ensure"/> exists for when it does not: same stance as <see cref="ShellBackdrop"/>,
/// a missing asset degrades to a working default rather than to a crash.
/// </para>
/// </summary>
public static class AudioBuses
{
    /// <summary>Bus zero. Godot always has it; it cannot be renamed away.</summary>
    public const string Master = "Master";

    public const string Music = "Music";

    /// <summary>
    /// Named SFX rather than Effects because that is what every mixer calls it, and the bus name
    /// is what a sound designer will see in the editor. The setting behind it is spelled out in
    /// full, because a settings menu is not a mixer.
    /// </summary>
    public const string Effects = "SFX";

    public static void Ensure()
    {
        EnsureBus(Music);
        EnsureBus(Effects);
    }

    /// <summary>The bus index, or -1 if there is no such bus. Callers must not assume success.</summary>
    public static int IndexOf(string name) => AudioServer.GetBusIndex(name);

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        GD.PushWarning($"Audio bus '{name}' is missing; creating it. Is res://default_bus_layout.tres intact?");

        // Appended at the end rather than at a chosen index: inserting would renumber the buses
        // beneath it, and bus indices are what every send in the layout is written in terms of.
        var index = AudioServer.BusCount;
        AudioServer.AddBus();
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, Master);
    }
}
