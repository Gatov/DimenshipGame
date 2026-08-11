using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The editor's left column: the library, a way to add to it, and what is known about whichever
/// program is selected.
/// </summary>
public sealed partial class ProgramList : VBoxContainer
{
    private VBoxContainer _rows = null!;
    private VBoxContainer _info = null!;

    /// <summary>Raised with the index of the program the player picked.</summary>
    public Action<int>? Chosen { get; set; }

    public Action? NewRequested { get; set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        CustomMinimumSize = new Vector2(260, 0);

        AddChild(ProgramBox.Create("Programs List", out _rows));

        var add = new Button { Text = "+ NEW PROGRAM" };
        ShellTheme.ApplyGlass(add);
        add.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        add.Pressed += () => NewRequested?.Invoke();
        AddChild(add);

        AddChild(ProgramBox.Create("Selected Program Info", out _info));
    }

    public void Refresh(IReadOnlyList<ProgramDraft> programs, int selected)
    {
        Clear(_rows);
        Clear(_info);

        for (var i = 0; i < programs.Count; i++)
        {
            var index = i;
            var row = new Entry(programs[i], i == selected)
            {
                Pressed = () => Chosen?.Invoke(index),
            };
            _rows.AddChild(row);
        }

        if (selected < 0 || selected >= programs.Count)
        {
            return;
        }

        var program = programs[selected];

        var description = new Label
        {
            Text = program.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        description.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
        description.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        _info.AddChild(description);

        _info.AddChild(ShellTheme.Divider());
        _info.AddChild(ProgramBox.Row("Scope", program.Scope));
        _info.AddChild(ProgramBox.Row("Complexity", program.Tier));

        // The numerals are present, deliberately: a count rendered as a row of stars is a value
        // encoded in shape alone, and the tier's maximum is the half of it that matters.
        _info.AddChild(ProgramBox.Row(
            "Rules", $"{program.Rules.Count}/{program.RuleLimit}"));
        _info.AddChild(ProgramBox.Row("Actions", Count(program).ToString()));

        // A word in a state colour, never the colour alone.
        _info.AddChild(ProgramBox.Row(
            "Reliability",
            program.Reliability,
            program.Reliability == "Verified" ? ShellPalette.StateOk : ShellPalette.StateWarn));
    }

    /// <summary>Every command in the program, however deeply nested.</summary>
    private static int Count(ProgramDraft program) =>
        program.Rules.Sum(rule => Count(rule.Then));

    private static int Count(IEnumerable<StatementDraft> body) =>
        body.Sum(statement => statement switch
        {
            CommandDraft => 1,
            BranchDraft branch =>
                branch.Arms.Sum(arm => Count(arm.Then)) +
                (branch.Otherwise is null ? 0 : Count(branch.Otherwise)),
            _ => 0,
        });

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// One library row: the program's name, its scope beneath, and its version on the right.
    /// <para>
    /// A <see cref="PanelContainer"/> rather than a <see cref="Button"/>, following
    /// <see cref="NodeCard"/>: a Button lays out no children at all, so its label would be the only
    /// thing it could ever hold. The five control states are drawn here instead.
    /// </para>
    /// <para>
    /// The active row keeps its highlight permanently rather than only on hover, so which program
    /// is being edited can be read without moving the pointer.
    /// </para>
    /// </summary>
    private sealed partial class Entry : PanelContainer
    {
        private readonly ProgramDraft _program;
        private readonly bool _active;

        private bool _hovered;
        private bool _focused;

        public Entry(ProgramDraft program, bool active)
        {
            _program = program;
            _active = active;
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
        }

        public Action? Pressed { get; set; }

        public override void _Ready()
        {
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", 0);
            AddChild(column);

            var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            top.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            column.AddChild(top);

            top.AddChild(Text(
                _program.Name, ShellPalette.TextTitle, ShellPalette.FontBody, expand: true));
            top.AddChild(Text(
                _program.Version, ShellPalette.TextDim, ShellPalette.FontMicro, expand: false));

            column.AddChild(Text(
                _program.Scope.ToUpperInvariant(), ShellPalette.TextDim, ShellPalette.FontMicro,
                expand: false));

            MouseEntered += () => Set(ref _hovered, true);
            MouseExited += () => Set(ref _hovered, false);
            FocusEntered += () => Set(ref _focused, true);
            FocusExited += () => Set(ref _focused, false);

            Apply();
        }

        public override void _GuiInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                    GrabFocus();
                    Pressed?.Invoke();
                    AcceptEvent();
                    break;

                case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }:
                    Pressed?.Invoke();
                    AcceptEvent();
                    break;
            }
        }

        private static Label Text(string text, Color colour, int size, bool expand)
        {
            var label = new Label
            {
                Text = text,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };

            if (expand)
            {
                label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }

            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeFontSizeOverride("font_size", size);
            return label;
        }

        private void Set(ref bool state, bool value)
        {
            if (state == value)
            {
                return;
            }

            state = value;
            Apply();
        }

        private void Apply() => AddThemeStyleboxOverride("panel", ShellTheme.Block(
            _active || _hovered ? ShellPalette.BgGlassHover : ShellPalette.BgPanel,
            highlighted: _active || _focused));
    }
}
