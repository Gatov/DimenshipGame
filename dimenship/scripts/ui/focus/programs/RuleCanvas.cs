using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The rule ladder: every rule of the selected program, drawn as an indent ladder with a gutter
/// number per row, and rebuilt from the draft after every edit.
/// <para>
/// The tree is flattened into a flat list of rows, each carrying its own depth, rather than being
/// drawn as nested containers. A gutter that must stay at a constant x while its rows indent cannot
/// be a column of a nested layout, and the vertical rule marking a branch body is then one 2px rect
/// per row at a known x — which joins up across consecutive rows for free, provided the ladder's
/// row separation is zero and the rows carry their own breathing space in their padding.
/// </para>
/// <para>
/// The spec has this measured by <c>BlockLayout</c> in <c>Dimenship.Shell</c> and hit-tested by
/// <c>DropTarget</c>. That is the right answer for a custom-drawn canvas; this mock lets Godot's
/// containers do the arithmetic instead, and each block resolves its own drop.
/// </para>
/// </summary>
public sealed partial class RuleCanvas : ScrollContainer
{
    /// <summary>One indent level, per the spec's <c>IndentWidth</c>.</summary>
    private const int IndentWidth = 20;

    /// <summary>The gutter, wide enough for a three-digit block number.</summary>
    private const int GutterWidth = 28;

    /// <summary>The vertical rule marking a branch body. Drawn at full opacity, never dimmed —
    /// it is the only thing carrying nesting depth visually.</summary>
    private const int GuideWidth = 2;

    private VBoxContainer _ladder = null!;
    private ProgramDraft? _program;
    private StatementDraft? _selection;
    private List<StatementDraft>? _selectionOwner;
    private int _number;

    /// <summary>Raised after the draft has been mutated, so the owner can record undo and rebuild.</summary>
    public Action? Edited { get; set; }

    /// <summary>The statement the player has selected, if any. Null when nothing is selected.</summary>
    public StatementDraft? Selection => _selection;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _ladder = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        // Zero, so a branch's vertical rule is continuous down its body. The rows carry their own
        // separation in their block padding instead.
        _ladder.AddThemeConstantOverride("separation", 0);
        AddChild(_ladder);
    }

    /// <summary>
    /// Draws a different program. Named for what it does rather than <c>Show</c>, which is
    /// <see cref="CanvasItem"/>'s own and means something else entirely.
    /// </summary>
    public void SetProgram(ProgramDraft? program)
    {
        _program = program;
        _selection = null;
        _selectionOwner = null;
        Rebuild();
    }

    public void Rebuild()
    {
        foreach (var child in _ladder.GetChildren())
        {
            _ladder.RemoveChild(child);
            child.QueueFree();
        }

        if (_program is null)
        {
            _ladder.AddChild(Notice("NO PROGRAM SELECTED"));
            return;
        }

        _number = 1;
        _selectionOwner = null;

        for (var i = 0; i < _program.Rules.Count; i++)
        {
            EmitRule(_program.Rules[i], i);
        }

        _ladder.AddChild(AddRuleFooter());
    }

    /// <summary>Removes the selected block and everything under it. Returns false when nothing is selected.</summary>
    public bool DeleteSelection()
    {
        if (_selection is null || _selectionOwner is null)
        {
            return false;
        }

        _selectionOwner.Remove(_selection);
        _selection = null;
        _selectionOwner = null;
        return true;
    }

    private void EmitRule(RuleDraft rule, int index)
    {
        // Written back through the slot values rather than bound: RuleDraft carries a plain int and
        // a plain long, and giving the model SlotValue-typed fields to suit one editor control
        // would be the view reaching into the model's shape.
        var priority = new SlotValue(rule.Priority);
        var cooldown = new SlotValue(rule.CooldownTicks);

        var header = new BlockView(
            BlockKind.Control,
            $"RULE {index + 1}",
            new[]
            {
                new SlotSpec(SlotKind.Number, Lead: "priority", Max: 100),
                new SlotSpec(SlotKind.Number, Lead: "cooldown", Trail: "ticks", Max: 100_000),
            },
            new[] { priority, cooldown })
        {
            OnEdited = () =>
            {
                rule.Priority = (int)priority.Number;
                rule.CooldownTicks = cooldown.Number;
                Edited?.Invoke();
            },
        };

        _ladder.AddChild(Row(header, depth: 0, guides: null, numbered: true, trailing: RemoveRule(rule)));

        EmitRuleCondition(rule);

        var then = new BlockView(BlockKind.Control, "THEN", Array.Empty<SlotSpec>(), Array.Empty<SlotValue>());
        _ladder.AddChild(Row(then, depth: 1, guides: null, numbered: false));

        EmitBody(rule.Then, depth: 2, guides: new List<Guide> { new(1, ShellPalette.BlockControl) });
    }

    /// <summary>
    /// A rule's gate. A null condition is an unconditional standing instruction — the spec's own
    /// wording — so it is drawn as exactly that and as a place to drop a condition, rather than as
    /// a missing block.
    /// </summary>
    private void EmitRuleCondition(RuleDraft rule)
    {
        var when = rule.When;

        var block = when is null
            ? new BlockView(
                BlockKind.Condition, "EVERY TICK", Array.Empty<SlotSpec>(), Array.Empty<SlotValue>(),
                note: "drop a condition here to gate this rule")
            : new BlockView(
                BlockKind.Condition,
                ProgramVocabulary.Spec(when.Kind).Keyword,
                ProgramVocabulary.Spec(when.Kind).Slots,
                when.Slots);

        block.OnEdited = () => Edited?.Invoke();
        block.OnCondition = condition =>
        {
            rule.When = condition;
            Edited?.Invoke();
        };

        var trailing = when is null
            ? null
            : SmallButton("CLEAR", () =>
            {
                rule.When = null;
                Edited?.Invoke();
            });

        _ladder.AddChild(Row(block, depth: 1, guides: null, numbered: true, trailing: trailing));
    }

    private void EmitBody(List<StatementDraft> body, int depth, List<Guide> guides)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case CommandDraft command:
                    EmitCommand(command, body, depth, guides);
                    break;

                case BranchDraft branch:
                    EmitBranch(branch, body, depth, guides);
                    break;
            }
        }

        var strip = new BlockDropStrip(body, empty: body.Count == 0) { OnDropped = Drop };
        _ladder.AddChild(Row(strip, depth, guides, numbered: false));
    }

    private void EmitCommand(
        CommandDraft command, List<StatementDraft> owner, int depth, List<Guide> guides)
    {
        var spec = ProgramVocabulary.Spec(command.Kind);

        var block = new BlockView(BlockKind.Action, spec.Keyword, spec.Slots, command.Slots)
        {
            Statement = command,
            Body = owner,
            OnDropped = Drop,
            OnEdited = () => Edited?.Invoke(),
            OnSelected = Select,
        };

        Mark(block, command, owner);
        _ladder.AddChild(Row(block, depth, guides, numbered: true));
    }

    /// <summary>
    /// A whole <c>IF</c> / <c>ELSE IF</c> … / <c>ELSE</c> ladder. Only the leading <c>IF</c> is a
    /// drag source, and it carries the whole branch: dragging an <c>ELSE IF</c> and having the
    /// arms above it come too would be the canvas moving something the player did not grab.
    /// </summary>
    private void EmitBranch(
        BranchDraft branch, List<StatementDraft> owner, int depth, List<Guide> guides)
    {
        for (var i = 0; i < branch.Arms.Count; i++)
        {
            var arm = branch.Arms[i];
            var lead = i == 0;
            var spec = ProgramVocabulary.Spec(arm.When.Kind);
            var kind = lead ? BlockKind.Control : BlockKind.Branch;

            var header = new BlockView(kind, lead ? "IF" : "ELSE IF", spec.Slots, arm.When.Slots)
            {
                OnEdited = () => Edited?.Invoke(),
                OnCondition = condition =>
                {
                    arm.When = condition;
                    Edited?.Invoke();
                },
                OnSelected = Select,
            };

            if (lead)
            {
                header.Statement = branch;
                header.Body = owner;
                Mark(header, branch, owner);
            }

            _ladder.AddChild(Row(header, depth, guides, numbered: true, trailing: RemoveArm(branch, i)));
            EmitBody(arm.Then, depth + 1, With(guides, depth, Colour(kind)));
        }

        var fallback = branch.Otherwise;
        if (fallback is not null)
        {
            var otherwise = new BlockView(
                BlockKind.Branch, "ELSE", Array.Empty<SlotSpec>(), Array.Empty<SlotValue>(),
                note: "when no arm above matched")
            {
                OnSelected = Select,
            };

            _ladder.AddChild(Row(
                otherwise, depth, guides, numbered: true, trailing: SmallButton("REMOVE", () =>
                {
                    branch.Otherwise = null;
                    Edited?.Invoke();
                })));

            EmitBody(fallback, depth + 1, With(guides, depth, ShellPalette.BlockBranch));
        }

        _ladder.AddChild(Row(ArmFooter(branch), depth, guides, numbered: false));
    }

    /// <summary>
    /// One row: the gutter number, the indent and its vertical rules, the block, and whatever
    /// button the row carries on its right.
    /// </summary>
    private Control Row(
        Control content,
        int depth,
        List<Guide>? guides,
        bool numbered,
        Control? trailing = null)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 0);

        var gutter = new Label
        {
            Text = numbered ? _number++.ToString() : string.Empty,
            CustomMinimumSize = new Vector2(GutterWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        gutter.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
        gutter.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        row.AddChild(gutter);

        row.AddChild(new Indent(depth, guides));

        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(content);

        if (trailing is not null)
        {
            row.AddChild(trailing);
        }

        return row;
    }

    /// <summary>
    /// Moves or inserts a block. The removal happens before the insert and the index is corrected
    /// for it, so dragging a block downward inside its own list lands where the indicator was
    /// rather than one place short of it.
    /// </summary>
    private void Drop(BlockDragData payload, List<StatementDraft> target, int index)
    {
        var statement = payload.ToStatement();

        if (payload.Body is not null && payload.Statement is not null)
        {
            var from = payload.Body.IndexOf(payload.Statement);
            if (from >= 0)
            {
                payload.Body.RemoveAt(from);

                if (ReferenceEquals(payload.Body, target) && from < index)
                {
                    index--;
                }
            }
        }

        target.Insert(Math.Clamp(index, 0, target.Count), statement);
        _selection = statement;
        Edited?.Invoke();
    }

    private void Select(BlockView view)
    {
        _selection = view.Statement;
        _selectionOwner = view.Body;

        foreach (var child in _ladder.GetChildren())
        {
            if (child is not HBoxContainer row)
            {
                continue;
            }

            foreach (var cell in row.GetChildren())
            {
                if (cell is BlockView block)
                {
                    block.SetSelected(block.Statement is not null &&
                                      ReferenceEquals(block.Statement, _selection));
                }
            }
        }
    }

    private void Mark(BlockView block, StatementDraft statement, List<StatementDraft> owner)
    {
        if (!ReferenceEquals(statement, _selection))
        {
            return;
        }

        _selectionOwner = owner;
        block.SetSelected(true);
    }

    private Control ArmFooter(BranchDraft branch)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);

        row.AddChild(SmallButton("+ ELSE IF", () =>
        {
            branch.Arms.Add(new BranchArmDraft(
                ProgramVocabulary.NewCondition(ConditionKind.StorageFillPercent)));
            Edited?.Invoke();
        }));

        if (branch.Otherwise is null)
        {
            row.AddChild(SmallButton("+ ELSE", () =>
            {
                branch.Otherwise = new List<StatementDraft>();
                Edited?.Invoke();
            }));
        }

        return row;
    }

    private Control AddRuleFooter()
    {
        var button = new Button
        {
            Text = "+ ADD NEW RULE",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        button.Pressed += () =>
        {
            _program?.Rules.Add(new RuleDraft());
            Edited?.Invoke();
        };

        return button;
    }

    private Control RemoveRule(RuleDraft rule) => SmallButton("REMOVE RULE", () =>
    {
        _program?.Rules.Remove(rule);
        Edited?.Invoke();
    });

    /// <summary>
    /// The leading arm is never removable on its own: a branch without its <c>IF</c> is an
    /// <c>ELSE</c> with nothing to be the alternative to, which the language has no meaning for.
    /// Removing the whole branch is what selecting the <c>IF</c> and pressing Delete is for.
    /// </summary>
    private Control? RemoveArm(BranchDraft branch, int index) => index == 0
        ? null
        : SmallButton("REMOVE", () =>
        {
            branch.Arms.RemoveAt(index);
            Edited?.Invoke();
        });

    private static Button SmallButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        ShellTheme.ApplyGlass(button);
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        button.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        button.Pressed += pressed;
        return button;
    }

    private static Label Notice(string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        return label;
    }

    private static Color Colour(BlockKind kind) =>
        kind == BlockKind.Control ? ShellPalette.BlockControl : ShellPalette.BlockBranch;

    private static List<Guide> With(List<Guide>? guides, int column, Color colour)
    {
        var next = guides is null ? new List<Guide>() : new List<Guide>(guides);
        next.Add(new Guide(column, colour));
        return next;
    }

    /// <summary>A vertical rule to draw down this row, at the indent column it belongs to.</summary>
    private readonly record struct Guide(int Column, Color Colour);

    /// <summary>
    /// The blank space to the left of a block, and the branch rules running through it. It draws
    /// its own guides rather than each branch drawing a tall rect behind its body, because a tall
    /// rect would have to be positioned against a layout that has not run yet.
    /// </summary>
    private sealed partial class Indent : Control
    {
        private readonly List<Guide> _guides;

        public Indent(int depth, List<Guide>? guides)
        {
            _guides = guides is null ? new List<Guide>() : new List<Guide>(guides);
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new Vector2(depth * IndentWidth, 0);
        }

        public override void _Draw()
        {
            foreach (var guide in _guides)
            {
                DrawRect(
                    new Rect2(guide.Column * IndentWidth, 0, GuideWidth, Size.Y), guide.Colour);
            }
        }
    }
}
