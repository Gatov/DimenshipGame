using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The programming view: a library on the left, the rule ladder in the middle, the block palette on
/// the right.
/// <para>
/// <b>This is a concept mock.</b> It authors programs and nothing executes them. There is no
/// runtime, no validator, no telemetry and no persistence — a program edited here changes nothing
/// about the vessel, and closing the game forgets it. It exists to answer one question before the
/// system underneath it is built: whether authoring a rule by dragging blocks into an indent
/// ladder is pleasant to use.
/// </para>
/// <para>
/// The panel identifier stays <c>doctrine</c> although the view is titled Programs: the identifier
/// is written into saved layout files, and renaming it would silently reset every player's layout
/// to gain nothing. The title change does move this view's accelerator, because
/// <see cref="ShellRoot"/> orders the focus views by title.
/// </para>
/// </summary>
public sealed partial class ProgramsFocus : PanelBase
{
    /// <summary>
    /// Bounded, per the spec. Undo is per program and is discarded when the selection changes: a
    /// player who edits three programs and expects Ctrl+Z to walk backwards across all three is
    /// expecting a document model this editor does not have.
    /// </summary>
    private const int UndoLimit = 64;

    private readonly List<ProgramDraft> _programs = ProgramLibrary.Create();
    private readonly List<ProgramDraft> _undo = new();
    private readonly List<ProgramDraft> _redo = new();

    private ProgramList _library = null!;
    private RuleCanvas _canvas = null!;
    private Label _name = null!;
    private Label _version = null!;

    /// <summary>The draft as it stood before the edit in progress. What an undo entry is made of.</summary>
    private ProgramDraft? _baseline;
    private int _selected;

    public override PanelId Id => ShellRoot.DoctrineId;

    public override string Title => "Programs";

    private ProgramDraft? Current =>
        _selected >= 0 && _selected < _programs.Count ? _programs[_selected] : null;

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(column);

        // Two nested splitters rather than three fixed columns, so the player can trade palette
        // width for canvas width. The offsets are session-local: LayoutState describes zones, not
        // the interior of a focus view, and widening it to carry one view's private geometry is how
        // a layout format stops being about layout.
        var outer = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(outer);

        _library = new ProgramList
        {
            Chosen = Select,
            NewRequested = AddProgram,
        };
        outer.AddChild(_library);

        var inner = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        outer.AddChild(inner);

        inner.AddChild(Editor());
        inner.AddChild(new BlockPalette());

        Select(0);
    }

    /// <summary>
    /// Nothing. The mock reads no live state: a program here commands nothing, so there is nothing
    /// about the vessel for it to be out of date with. The real view reads the snapshot to offer
    /// live targets and to gate activation on TimeFlow.
    /// </summary>
    public override void OnSnapshot(WorldSnapshot snapshot)
    {
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Delete when !key.CtrlPressed:
                if (_canvas.DeleteSelection())
                {
                    RecordEdit();
                    AcceptEvent();
                }

                break;

            case Key.Z when key.CtrlPressed:
                Step(_undo, _redo);
                AcceptEvent();
                break;

            case Key.Y when key.CtrlPressed:
                Step(_redo, _undo);
                AcceptEvent();
                break;
        }
    }

    private Control Editor()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        column.AddChild(Header());
        column.AddChild(Tabs());
        column.AddChild(ShellTheme.Divider());

        _canvas = new RuleCanvas { Edited = RecordEdit };
        column.AddChild(_canvas);

        return column;
    }

    private Control Header()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var caption = new Label { Text = "PROGRAM:" };
        caption.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        caption.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(caption);

        _name = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _name.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        row.AddChild(_name);

        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: false));
        _version = new Label();
        _version.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        _version.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        chip.AddChild(_version);
        row.AddChild(chip);

        // Stated in words beside the control, not left as a greyed button the player has to guess
        // at. Activation is disabled because there is nothing to activate into — the runtime this
        // mock exists to justify has not been built.
        var reason = new Label { Text = "CONCEPT — NO RUNTIME TO ACTIVATE INTO" };
        reason.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(reason);

        var activate = new Button
        {
            Text = "ACTIVATE",
            Disabled = true,
            FocusMode = FocusModeEnum.None,
        };
        activate.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(activate);

        return row;
    }

    /// <summary>
    /// <c>RULES</c> and the five tabs whose subsystems do not exist, rendered disabled rather than
    /// hidden so their positions stay where they will be when those subsystems ship.
    /// </summary>
    private static Control Tabs()
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        strip.AddChild(Tab("RULES", active: true));

        foreach (var title in new[] { "PARAMETERS", "NOTES", "VARIABLES", "PERMISSIONS", "FUNCTIONS" })
        {
            strip.AddChild(Tab(title, active: false));
        }

        return strip;
    }

    private static Button Tab(string text, bool active)
    {
        var tab = new Button
        {
            Text = text,
            Disabled = !active,
            FocusMode = active ? FocusModeEnum.All : FocusModeEnum.None,
        };

        ShellTheme.ApplyGlass(tab);
        tab.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        tab.AddThemeColorOverride("font_disabled_color", ShellPalette.TextFaint);

        if (active)
        {
            tab.AddThemeStyleboxOverride(
                "normal", ShellTheme.Block(ShellPalette.BgGlass, highlighted: true));
            tab.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        }

        return tab;
    }

    private void Select(int index)
    {
        _selected = index;
        _undo.Clear();
        _redo.Clear();
        _baseline = Current?.Clone();

        _canvas.SetProgram(Current);
        RefreshHeader();
        _library.Refresh(_programs, _selected);
    }

    private void AddProgram()
    {
        _programs.Add(ProgramLibrary.Empty(_programs.Count + 1));
        Select(_programs.Count - 1);
    }

    /// <summary>
    /// Records the state as it was before this edit, then redraws. The baseline is what makes an
    /// undo entry possible at all: the draft is mutated in place by whatever control changed it, so
    /// by the time this is called the only copy of the previous state is the one kept here.
    /// </summary>
    private void RecordEdit()
    {
        if (Current is null)
        {
            return;
        }

        if (_baseline is not null)
        {
            _undo.Add(_baseline);

            if (_undo.Count > UndoLimit)
            {
                _undo.RemoveAt(0);
            }
        }

        _redo.Clear();
        _baseline = Current.Clone();

        _canvas.Rebuild();
        RefreshHeader();
        _library.Refresh(_programs, _selected);
    }

    /// <summary>
    /// Undo and redo are the same move in opposite directions: take the top of one stack, push what
    /// is on screen onto the other, and swap the draft the library holds.
    /// </summary>
    private void Step(List<ProgramDraft> from, List<ProgramDraft> to)
    {
        if (from.Count == 0 || Current is null)
        {
            return;
        }

        var restored = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Current.Clone());

        _programs[_selected] = restored;
        _baseline = restored.Clone();

        _canvas.SetProgram(restored);
        RefreshHeader();
        _library.Refresh(_programs, _selected);
    }

    private void RefreshHeader()
    {
        _name.Text = Current?.Name ?? "—";
        _version.Text = Current?.Version ?? "—";
    }
}
