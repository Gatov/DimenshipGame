using System;
using System.Collections.Generic;
using System.Linq;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The settings menu: Gameplay, Graphics and Sound, over whatever scene opened it.
/// <para>
/// A <see cref="Control"/> laid over the running scene rather than a real <see cref="Window"/>.
/// A Window owns its own viewport, and the frosted-glass shader samples the backdrop against
/// <c>SCREEN_UV</c> — inside a second viewport it would sample nothing, and the menu would be the
/// one surface in the game that does not match the game. It carries its own theme because the
/// start screen has none, so the menu looks the same from both places it opens.
/// </para>
/// <para>
/// There is no OK and no Cancel. Every change applies and persists as it is made, the way the
/// window layout already does — a menu that asks a player to confirm a volume slider they have
/// already heard take effect is asking about something already decided.
/// </para>
/// </summary>
public sealed partial class SettingsOverlay : Control
{
    /// <summary>Wide enough that a label, its note and its control share one line comfortably.</summary>
    private const int PaneWidth = 620;

    /// <summary>Holds the tab bodies to one height, so switching tabs does not resize the pane.</summary>
    private const int BodyHeight = 260;

    private const int RowLabelWidth = 170;
    private const int SliderWidth = 180;
    private const int ControlWidth = 190;
    private const int ReadoutWidth = 48;

    /// <summary>Interface scale moves in 5% steps: finer is fiddly, coarser skips useful sizes.</summary>
    private const int ScaleStep = 50;

    /// <summary>Volume moves in whole percent, which is what the readout beside it shows.</summary>
    private const int VolumeStep = 10;

    /// <summary>The permille divisor, so the bare 1000 never appears in a calculation.</summary>
    private const int Permille = 1000;

    /// <summary>
    /// Built once per process rather than per opening. The theme allocates the slider handle
    /// textures, and a menu the player opens and closes repeatedly should not allocate three
    /// images each time.
    /// </summary>
    private static Theme? _theme;

    private VBoxContainer _body = null!;
    private HBoxContainer _tabs = null!;
    private SettingsTab _tab = SettingsTab.Graphics;

    /// <summary>
    /// The order is the conventional one, and the tab the menu opens on is deliberately not the
    /// first: Gameplay has nothing in it yet, and landing on an empty tab reads as a broken menu.
    /// </summary>
    public enum SettingsTab
    {
        Gameplay,
        Graphics,
        Sound,
    }

    /// <summary>Raised once, when the menu closes. The host restores whatever it suspended.</summary>
    public event Action? Closed;

    /// <summary>Opens the menu over <paramref name="host"/>, which must be the scene's root Control.</summary>
    public static SettingsOverlay Open(Node host)
    {
        var overlay = new SettingsOverlay { Name = "SettingsOverlay" };
        host.AddChild(overlay);
        return overlay;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // Stop, not Pass: the menu is modal, and a click that fell through it would select a node
        // on a graph the player cannot see.
        MouseFilter = MouseFilterEnum.Stop;
        Theme = _theme ??= ShellTheme.Build();

        var scrim = new ColorRect { Color = ShellPalette.BgScrim, MouseFilter = MouseFilterEnum.Stop };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var pane = new PanelContainer { CustomMinimumSize = new Vector2(PaneWidth, 0) };
        pane.AddThemeStyleboxOverride("panel", ShellTheme.Pane());
        centre.AddChild(pane);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        pane.AddChild(column);

        var heading = new Label { Text = "SETTINGS" };
        heading.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        column.AddChild(heading);

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        column.AddChild(_tabs);

        column.AddChild(ShellTheme.Divider());

        _body = new VBoxContainer { CustomMinimumSize = new Vector2(0, BodyHeight) };
        _body.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        column.AddChild(_body);

        column.AddChild(ShellTheme.Divider());
        column.AddChild(BuildFooter());

        RebuildTabs();
        RebuildBody();
    }

    /// <summary>
    /// Escape closes the menu. Handled in <c>_Input</c> and marked handled, so it beats
    /// <c>ShellRoot._UnhandledInput</c>, where Escape already means "release GUI focus" — one key
    /// cannot mean two things while a modal surface is up.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>
    /// A backstop for the one path that changes a value without ending in a commit: dragging a
    /// slider and closing the menu with the mouse still down.
    /// </summary>
    public override void _ExitTree() => Settings.Persist();

    private void Close()
    {
        var closed = Closed;
        Closed = null;
        closed?.Invoke();
        QueueFree();
    }

    private Control BuildFooter()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var restore = new Button { Text = "RESTORE DEFAULTS" };
        restore.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        restore.Pressed += () => Commit(SettingsState.Defaults);
        row.AddChild(restore);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var back = new Button { Text = "BACK" };
        back.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        back.Pressed += Close;
        row.AddChild(back);

        // Deferred because the row is not in the tree yet, and a Control outside the tree has no
        // viewport to take focus in.
        back.CallDeferred(Control.MethodName.GrabFocus);
        return row;
    }

    private void RebuildTabs()
    {
        Clear(_tabs);

        foreach (var tab in Enum.GetValues<SettingsTab>())
        {
            _tabs.AddChild(TabButton(tab));
        }
    }

    /// <summary>
    /// The tab strip from the programming view, with one difference: an inactive tab here is
    /// enabled, because these tabs go somewhere. Disabled stays reserved for what does not exist.
    /// </summary>
    private Button TabButton(SettingsTab tab)
    {
        var button = new Button { Text = tab.ToString().ToUpperInvariant() };

        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);

        if (tab == _tab)
        {
            button.AddThemeStyleboxOverride(
                "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: true));
            button.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        }
        else
        {
            button.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        }

        button.Pressed += () =>
        {
            if (_tab == tab)
            {
                return;
            }

            _tab = tab;
            // Deferred: this frees the button whose own signal is being emitted.
            Callable.From(RebuildTabs).CallDeferred();
            Callable.From(RebuildBody).CallDeferred();
        };

        return button;
    }

    private void RebuildBody()
    {
        Clear(_body);

        switch (_tab)
        {
            case SettingsTab.Gameplay:
                BuildGameplay();
                break;
            case SettingsTab.Graphics:
                BuildGraphics();
                break;
            case SettingsTab.Sound:
                BuildSound();
                break;
        }
    }

    /// <summary>
    /// A stub, said out loud, in the register <see cref="PlaceholderPanel"/> uses for the focus
    /// views that do not exist yet. The alternative — a difficulty dropdown and an autosave
    /// interval persisted to a file nothing reads — would look finished and be a lie.
    /// </summary>
    private void BuildGameplay()
    {
        var centre = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(centre);

        var column = new VBoxContainer();
        centre.AddChild(column);

        var heading = new Label { Text = "GAMEPLAY", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        heading.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        column.AddChild(heading);

        var body = new Label
        {
            Text = "Difficulty, autosave cadence and auto-pause on a critical alert belong here.\n"
                 + "None is offered yet: each names a system the simulation does not have, and a "
                 + "switch with nothing behind it is worse than an empty tab.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        column.AddChild(body);
    }

    private void BuildGraphics()
    {
        var graphics = Settings.Current.Graphics;
        var owned = SettingsApplier.WindowIsPlayerControlled;
        var windowed = graphics.Mode == WindowMode.Windowed;

        var modes = Enum.GetValues<WindowMode>();
        var mode = Dropdown(
            modes.Select(Describe),
            Array.IndexOf(modes, graphics.Mode),
            index => Commit(graphics with { Mode = modes[index] }));
        mode.Disabled = !owned;
        _body.AddChild(Row("WINDOW MODE", mode, owned ? null : "THE PLATFORM OWNS THE WINDOW"));

        // The screen's full size, not its usable rect: a 1920x1080 display whose taskbar leaves
        // 1920x1040 usable must still offer 1920x1080, which is the size a player on it expects.
        var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        var offered = ScreenSizes.Offered(new ScreenSize(screen.X, screen.Y), graphics.Resolution).ToList();
        var resolution = Dropdown(
            offered.Select(size => size.ToString()),
            offered.IndexOf(graphics.Resolution),
            index => Commit(graphics with { Resolution = offered[index] }));
        resolution.Disabled = !owned || !windowed;
        _body.AddChild(Row(
            "RESOLUTION",
            resolution,
            windowed ? null : "THE DISPLAY OWNS THE SIZE IN FULLSCREEN"));

        _body.AddChild(Row(
            "INTERFACE SCALE",
            Slider(
                SettingsSerializer.MinUiScalePermille,
                SettingsSerializer.MaxUiScalePermille,
                ScaleStep,
                graphics.UiScalePermille,
                editable: true,
                live: false,
                permille => Settings.Current with
                {
                    Graphics = Settings.Current.Graphics with { UiScalePermille = permille },
                })));

        _body.AddChild(Row(
            "NEBULA BACKDROP",
            Toggle(graphics.Backdrop, on => Commit(graphics with { Backdrop = on }))));

        // Shown off while the backdrop is off, without forgetting the choice underneath: turning
        // the backdrop back on restores the frost the player actually chose.
        var frost = Toggle(
            graphics.Backdrop && graphics.FrostedPanels,
            on => Commit(graphics with { FrostedPanels = on }));
        frost.Disabled = !graphics.Backdrop;
        _body.AddChild(Row(
            "FROSTED PANELS",
            frost,
            graphics.Backdrop ? null : "NOTHING TO BLUR WITHOUT THE BACKDROP"));
    }

    private void BuildSound()
    {
        var sound = Settings.Current.Sound;

        _body.AddChild(Row(
            "MASTER VOLUME",
            Volume(sound.MasterVolumePermille, true, (s, v) => s with { MasterVolumePermille = v })));

        _body.AddChild(Row(
            "MUSIC",
            Toggle(sound.MusicEnabled, on => Commit(sound with { MusicEnabled = on }))));

        _body.AddChild(Row(
            "MUSIC VOLUME",
            Volume(
                sound.MusicVolumePermille,
                sound.MusicEnabled,
                (s, v) => s with { MusicVolumePermille = v })));

        _body.AddChild(Row(
            "SOUND EFFECTS",
            Toggle(sound.EffectsEnabled, on => Commit(sound with { EffectsEnabled = on }))));

        _body.AddChild(Row(
            "EFFECTS VOLUME",
            Volume(
                sound.EffectsVolumePermille,
                sound.EffectsEnabled,
                (s, v) => s with { EffectsVolumePermille = v })));

        _body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        // Said plainly rather than left for the player to discover by turning everything up.
        // Music is real and audible; the effects bus is still waiting for something to play into
        // it, and a slider that moves nothing is worth admitting to.
        var note = new Label
        {
            Text = "Music plays. Sound effects have nothing to play yet; that slider sets the mixer for when they do.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        note.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _body.AddChild(note);
    }

    private void Commit(GraphicsSettings graphics) =>
        Commit(Settings.Current with { Graphics = graphics });

    private void Commit(SoundSettings sound) =>
        Commit(Settings.Current with { Sound = sound });

    private void Commit(SettingsState state)
    {
        Settings.Change(state, GetTree());
        // Deferred: a commit usually arrives from a control's own signal, and rebuilding the body
        // frees that control while it is still emitting.
        Callable.From(RebuildBody).CallDeferred();
    }

    /// <summary>
    /// A label, an optional reason the control is the way it is, and the control itself. The
    /// reason is a word beside the control rather than a greyed control the player has to guess
    /// at — the rule the programming view's disabled ACTIVATE button already follows.
    /// </summary>
    private static Control Row(string label, Control control, string? note = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var name = new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(RowLabelWidth, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        name.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(name);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        if (note is not null)
        {
            var reason = new Label { Text = note, VerticalAlignment = VerticalAlignment.Center };
            reason.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            row.AddChild(reason);
        }

        row.AddChild(control);
        return row;
    }

    /// <summary>
    /// A two-state button rather than a <c>CheckButton</c>. The engine's check button is a rounded
    /// switch drawn from its own textures — the one shape in this console that would carry a colour
    /// the palette never chose — and a button says which state it is in in a word, which is what
    /// the rule against state carried by colour alone asks for.
    /// </summary>
    private static Button Toggle(bool on, Action<bool> changed)
    {
        var button = new Button
        {
            Text = on ? "ON" : "OFF",
            ToggleMode = true,
            ButtonPressed = on,
            CustomMinimumSize = new Vector2(ControlWidth, 0),
        };
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        button.Toggled += pressed => changed(pressed);
        return button;
    }

    private static OptionButton Dropdown(IEnumerable<string> items, int selected, Action<int> changed)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(ControlWidth, 0) };
        option.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);

        foreach (var item in items)
        {
            option.AddItem(item);
        }

        // Assigned before the handler is connected: setting Selected raises no ItemSelected today,
        // and connecting afterwards means it cannot start doing so behind our backs.
        option.Selected = selected;
        option.ItemSelected += index => changed((int)index);
        return option;
    }

    /// <summary>A volume slider, which is heard as it is dragged.</summary>
    private Control Volume(int permille, bool editable, Func<SoundSettings, int, SoundSettings> revise) =>
        Slider(
            SettingsSerializer.MinVolumePermille,
            SettingsSerializer.MaxVolumePermille,
            VolumeStep,
            permille,
            editable,
            live: true,
            value => Settings.Current with { Sound = revise(Settings.Current.Sound, value) });

    /// <summary>
    /// A slider and its readout, packed as one control so a row holds them in one slot.
    /// <paramref name="revise"/> takes the new permille and returns the whole state it belongs to,
    /// so a slider never has to know which group it lives in.
    /// <para>
    /// The write happens once the drag ends, for the reason the split container gives at
    /// <c>ShellRoot.BuildTree</c>: a disk write per mouse-motion frame is a disk write per frame.
    /// A slider moved by the keyboard raises no drag, so that path commits on the spot.
    /// </para>
    /// <para>
    /// <paramref name="live"/> decides whether the value is also *applied* on the way. A volume is,
    /// because the point of dragging it is to hear it. The interface scale is not: applying it
    /// continuously resizes the settings menu — and with it this slider — out from under the
    /// cursor still dragging it. Its readout updates all the same, so the control is never mute.
    /// </para>
    /// </summary>
    private Control Slider(
        int min,
        int max,
        int step,
        int value,
        bool editable,
        bool live,
        Func<int, SettingsState> revise)
    {
        var readout = new Label
        {
            Text = Percent(value),
            CustomMinimumSize = new Vector2(ReadoutWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        readout.AddThemeColorOverride(
            "font_color", editable ? ShellPalette.TextPrimary : ShellPalette.TextFaint);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            Editable = editable,
            CustomMinimumSize = new Vector2(SliderWidth, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };

        // Read back rather than trusted: Range snaps an assigned value to the step, so a
        // hand-edited file holding 1010 leaves the handle at 1000, and the readout must agree with
        // where the handle actually is.
        var chosen = (int)slider.Value;
        readout.Text = Percent(chosen);

        var dragging = false;

        slider.DragStarted += () => dragging = true;
        slider.DragEnded += _ =>
        {
            dragging = false;
            Settings.Change(revise(chosen), GetTree());
        };
        slider.ValueChanged += raw =>
        {
            chosen = (int)raw;
            readout.Text = Percent(chosen);

            if (live)
            {
                Settings.Preview(revise(chosen), GetTree());
            }

            // No drag in progress means the keyboard moved it, and there is no drag-end to wait
            // for. Committing here is also what writes the value a live drag has already applied.
            if (!dragging)
            {
                Settings.Change(revise(chosen), GetTree());
            }
        };

        var pair = new HBoxContainer();
        pair.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        pair.AddChild(slider);
        pair.AddChild(readout);
        return pair;
    }

    private static string Percent(int permille) => $"{permille / (Permille / 100)}%";

    private static string Describe(WindowMode mode) => mode switch
    {
        WindowMode.Windowed => "Windowed",
        WindowMode.BorderlessFullscreen => "Borderless fullscreen",
        WindowMode.ExclusiveFullscreen => "Exclusive fullscreen",
        _ => mode.ToString(),
    };

    /// <summary>
    /// Removes before freeing rather than freeing alone: <c>QueueFree</c> is deferred, so a child
    /// left in place would still be laid out beside the ones replacing it for a frame.
    /// </summary>
    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
