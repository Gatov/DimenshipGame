using System.Collections.Generic;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The loadout composer: a template library on the left, the frame drawn with a box per socket in
/// the middle, and a picker that opens beside a box, listing the fittings its socket accepts and
/// previewing each on the strip before it is chosen.
/// <para>
/// <b>This is a concept mock.</b> It composes loadout templates and nothing builds them. There is
/// no robot, no socket storage, no refit, no production order and no persistence — a template
/// edited here changes nothing about the vessel, and closing the game forgets it. It exists to
/// answer one question before the system underneath it is designed: whether filling sockets against
/// a live rollup is a pleasant way to spend the time between missions.
/// </para>
/// <para>
/// One thing here is real. The build cost is compared against
/// <see cref="WorldSnapshot.Resources"/>, so the <i>held</i> column is the vessel's actual stock
/// and moves as it produces and spends. Everything else — frames, sockets, fittings, prices — is
/// invented, and <see cref="LoadoutCatalog"/> says so.
/// </para>
/// <para>
/// The panel identifier stays <c>robotics</c> and so does the title: the identifier is written into
/// saved layout files, and the title is what <see cref="ShellRoot"/> orders the focus views by for
/// the <c>Ctrl+1..9</c> accelerators. Renaming either would move something a player has already
/// learned in exchange for nothing.
/// </para>
/// <para>
/// See <c>docs/superpowers/specs/2026-08-21-loadout-composer-mock-design.md</c>, including its
/// <i>Not built</i> list.
/// </para>
/// </summary>
public sealed partial class LoadoutsFocus : PanelBase
{
    /// <summary>
    /// Bounded, and per template. Undo is discarded when the selection changes for the reason
    /// <see cref="ProgramsFocus"/> gives: a player who edits three templates and expects Ctrl+Z to
    /// walk backwards across all three is expecting a document model this editor does not have.
    /// </summary>
    private const int UndoLimit = 64;

    private readonly List<LoadoutDraft> _templates = LoadoutLibrary.Create();
    private readonly List<LoadoutDraft> _undo = new();
    private readonly List<LoadoutDraft> _redo = new();

    private TemplateList _library = null!;
    private LoadoutStage _stage = null!;
    private RollupGrid _rollup = null!;
    private CostBox _cost = null!;
    private Label _name = null!;
    private Label _verdict = null!;
    private Label _frameName = null!;
    private Button _changeFrame = null!;

    /// <summary>The open picker or frame chooser, if any. At most one is ever open.</summary>
    private StagePopover? _popover;

    /// <summary>
    /// A copy of the template with the previewed candidate fitted, or null. Never the template
    /// itself: a preview that could reach the template could reach the undo stack.
    /// </summary>
    private LoadoutDraft? _preview;

    /// <summary>
    /// Whether the details drawer is open. Session-local by being static: the shell frees a focus
    /// view on every switch, and a drawer that closed itself each time the player looked away would
    /// be a preference nobody set. Deliberately not in <c>user://layout.json</c>, which describes
    /// zones and never a focus view's interior.
    /// </summary>
    private static bool _detailsOpen;

    private readonly VesselStock _stock = new();
    private LoadoutStrip _strip = null!;
    private PanelContainer _drawer = null!;

    /// <summary>The template as it stood before the edit in progress. What an undo entry is made of.</summary>
    private LoadoutDraft? _baseline;
    private int _selected;

    /// <summary>
    /// Which socket is selected: the box drawn with the selection border, the one <c>Delete</c>
    /// clears, and the one a click or <c>Enter</c> opens the picker for. Kept rather than derived,
    /// so all three read one value.
    /// </summary>
    private int _socket;

    public override PanelId Id => ShellRoot.RoboticsId;

    public override string Title => "Robotics";

    private LoadoutDraft? Current =>
        _selected >= 0 && _selected < _templates.Count ? _templates[_selected] : null;

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(column);

        var split = new HSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(split);

        _library = new TemplateList
        {
            Chosen = Select,
            NewRequested = AddTemplate,
        };
        split.AddChild(_library);

        split.AddChild(Composer());

        Select(0);
        _strip.SetDetails(_detailsOpen);
    }

    /// <summary>
    /// Only the vessel's material stock is taken, for the two cost readouts. Nothing else here is
    /// live: a template commands nothing, so there is nothing about the vessel for it to be out of
    /// date with.
    /// </summary>
    public override void OnSnapshot(WorldSnapshot snapshot)
    {
        if (_stock.Update(snapshot))
        {
            RefreshReadouts();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            // Before the shell sees it: ShellRoot._UnhandledInput takes Escape as "release focus",
            // and unhandled key input reaches this view first.
            case Key.Escape when _popover is not null:
                ClosePopover();
                _stage.FocusBox(_socket);
                AcceptEvent();
                break;

            case Key.Delete when !key.CtrlPressed:
                Remove(_socket);
                AcceptEvent();
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

    private Control Composer()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        column.AddChild(Header());
        column.AddChild(ShellTheme.Divider());

        // The stage and the drawer share one area so the drawer can lie over the stage's bottom
        // edge. A drawer that pushed the stage up would resize it, and a resized stage moves every
        // box — opening the details would rearrange the thing being examined.
        var area = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(area);

        _stage = new LoadoutStage
        {
            SocketChosen = OpenPicker,
            SocketFocused = SelectSocket,
        };
        area.AddChild(_stage);
        _stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _drawer = new PanelContainer { Visible = _detailsOpen, MouseFilter = MouseFilterEnum.Stop };
        _drawer.AddThemeStyleboxOverride("panel", ShellTheme.Box());
        area.AddChild(_drawer);
        _drawer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _drawer.GrowVertical = GrowDirection.Begin;

        var details = new HBoxContainer();
        details.AddThemeConstantOverride("separation", ShellPalette.SpaceLg);
        _drawer.AddChild(details);

        _rollup = new RollupGrid { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddChild(_rollup);

        _cost = new CostBox(_stock) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddChild(_cost);

        _strip = new LoadoutStrip(_stock)
        {
            DetailsToggled = open =>
            {
                _detailsOpen = open;
                _drawer.Visible = open;
            },
        };
        column.AddChild(_strip);

        return column;
    }

    private Control Header()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);

        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
        row.AddChild(titles);

        _name = new Label { TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _name.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
        _name.AddThemeFontSizeOverride("font_size", ShellPalette.FontHeading);
        titles.AddChild(_name);

        _frameName = new Label();
        _frameName.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        _frameName.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        titles.AddChild(_frameName);

        _changeFrame = new Button { Text = "CHANGE FRAME", FocusMode = FocusModeEnum.None };
        ShellTheme.ApplyGlass(_changeFrame);
        _changeFrame.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _changeFrame.Pressed += OpenChooser;
        row.AddChild(_changeFrame);

        var chip = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", ShellTheme.Chip(active: false));
        _verdict = new Label();
        _verdict.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        chip.AddChild(_verdict);
        row.AddChild(chip);

        // Said in words beside the control rather than left as a greyed button to guess at, the way
        // the programming view states its disabled ACTIVATE. There is no build task to queue and no
        // construction unit to queue it on. The ticket's sketch leaves these three out; they stay,
        // because the mock's honesty is the one thing about it that is not presentation.
        var reason = new Label
        {
            Text = "CONCEPT — NOTHING IS BUILT FROM THIS",
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        reason.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        reason.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(reason);

        var queue = new Button
        {
            Text = "QUEUE BUILD",
            Disabled = true,
            FocusMode = FocusModeEnum.None,
        };
        queue.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        row.AddChild(queue);

        return row;
    }

    private void Select(int index)
    {
        ClosePopover();

        _selected = index;
        _socket = 0;
        _undo.Clear();
        _redo.Clear();
        _baseline = Current?.Clone();
        _preview = null;

        Rebuild();
    }

    private void AddTemplate()
    {
        _templates.Add(LoadoutLibrary.Empty(_templates.Count + 1));
        Select(_templates.Count - 1);
    }

    /// <summary>
    /// Keeps whatever the new frame has a socket of the same id and kind for, and drops the rest —
    /// see <see cref="LoadoutDraft.CarryOver"/>. A frame swap that emptied every socket would punish
    /// the player for looking.
    /// </summary>
    private void SwapFrame(FrameDef frame)
    {
        ClosePopover();

        if (Current is not { } template || template.FrameId == frame.Id)
        {
            return;
        }

        template.Refit(LoadoutCatalog.Frame(template.FrameId), frame);
        _socket = 0;
        RecordEdit();
    }

    private void Remove(int socket)
    {
        ClosePopover();

        if (Current is not { } template
            || socket < 0
            || socket >= template.Fitted.Count
            || template.Fitted[socket] is null)
        {
            return;
        }

        template.Fitted[socket] = null;
        _socket = socket;
        RecordEdit();
    }

    private void SelectSocket(int socket)
    {
        if (_socket == socket)
        {
            return;
        }

        // Keyboard focus (Tab) can reach a different box while a popover for another socket is
        // still open; closing it here is what keeps the popover's captured socket in step with
        // the box that reads selected.
        ClosePopover();

        _socket = socket;
        Rebuild();
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

        Rebuild();
    }

    /// <summary>
    /// Undo and redo are the same move in opposite directions: take the top of one stack, push what
    /// is on screen onto the other, and swap the draft the library holds.
    /// </summary>
    private void Step(List<LoadoutDraft> from, List<LoadoutDraft> to)
    {
        ClosePopover();

        if (from.Count == 0 || Current is null)
        {
            return;
        }

        var restored = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Current.Clone());

        _templates[_selected] = restored;
        _baseline = restored.Clone();

        Rebuild();
    }

    private void Rebuild()
    {
        _library.Refresh(_templates, _selected);

        if (Current is not { } template)
        {
            _name.Text = "—";
            _verdict.Text = "—";
            return;
        }

        var rollup = LoadoutRollup.Of(template);

        _stage.Show(rollup.Frame, template, _socket);

        _name.Text = template.Name;
        _frameName.Text = rollup.Frame.Label;
        _verdict.Text = VerdictText.Of(rollup.Verdict);
        _verdict.AddThemeColorOverride("font_color", VerdictText.Colour(rollup.Verdict));

        RefreshReadouts();
    }

    /// <summary>The strip and the drawer, recomputed together so they can never disagree.</summary>
    private void RefreshReadouts()
    {
        if (Current is not { } template)
        {
            return;
        }

        var rollup = LoadoutRollup.Of(template);
        var preview = _preview is null ? null : LoadoutRollup.Of(_preview);

        _strip.Refresh(rollup, preview);
        _rollup.Refresh(rollup);
        _cost.Refresh(rollup.Cost);
    }

    /// <summary>
    /// Opens the picker for a socket, beside its box. Opening one closes any other: two popovers
    /// would be two previews competing for one strip.
    /// </summary>
    private void OpenPicker(int socket)
    {
        if (Current is not { } template)
        {
            return;
        }

        var frame = LoadoutCatalog.Frame(template.FrameId);

        if (socket < 0 || socket >= frame.Sockets.Count)
        {
            return;
        }

        SelectSocket(socket);
        ClosePopover();

        var picker = new FittingPicker(frame.Sockets[socket], template.Fitted[socket])
        {
            Chosen = fitting => Choose(socket, fitting),
            Previewed = fitting => Preview(socket, fitting),
            PreviewCleared = ClearPreview,
        };

        Track(picker);
        _stage.ShowPopoverBeside(picker, socket);
        picker.FocusFirst();
    }

    private void OpenChooser()
    {
        if (Current is not { } template)
        {
            return;
        }

        ClosePopover();

        var chooser = new FrameChooser(LoadoutCatalog.Frame(template.FrameId), template.Fitted)
        {
            Chosen = SwapFrame,
        };

        Track(chooser);
        _stage.ShowPopoverAt(chooser, _changeFrame.GlobalPosition.X - _stage.GlobalPosition.X);
        chooser.FocusFirst();
    }

    private void Track(StagePopover popover)
    {
        popover.Closed = () =>
        {
            if (_popover == popover)
            {
                _popover = null;
            }

            ClearPreview();
        };

        _popover = popover;
    }

    private void ClosePopover() => _popover?.Close();

    /// <summary>
    /// A choice from the picker. Choosing what is already fitted closes the picker without an undo
    /// entry, because an undo step that changes nothing is one the player has to press twice.
    /// </summary>
    private void Choose(int socket, string? fitting)
    {
        if (Current is not { } template || socket >= template.Fitted.Count)
        {
            return;
        }

        var changed = template.Fitted[socket] != fitting;

        template.Fitted[socket] = fitting;
        _socket = socket;
        ClosePopover();

        if (changed)
        {
            RecordEdit();
        }

        _stage.FocusBox(socket);
    }

    private void Preview(int socket, string? fitting)
    {
        if (Current is not { } template || socket >= template.Fitted.Count)
        {
            return;
        }

        if (template.Fitted[socket] == fitting)
        {
            ClearPreview();
            return;
        }

        var copy = template.Clone();
        copy.Fitted[socket] = fitting;
        _preview = copy;
        RefreshReadouts();
    }

    private void ClearPreview()
    {
        if (_preview is null)
        {
            return;
        }

        _preview = null;
        RefreshReadouts();
    }
}
