using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// One editable value in a block, with the words either side of it. Every slot is a dropdown or a
/// bounded number field and there is no free text anywhere in the editor, which is what makes a
/// syntactically invalid program impossible to construct — the player cannot type a target that
/// does not exist or a comparison the language has no member for.
/// <para>
/// Built as a function rather than a node subclass: a slot has no state of its own beyond the
/// <see cref="SlotValue"/> it writes into, and a class per slot kind would be five classes that
/// each hold one control.
/// </para>
/// </summary>
public static class SlotControl
{
    /// <summary>Wide enough that a six-figure quantity does not clip its own spinner.</summary>
    private const int NumberWidth = 96;

    /// <summary>
    /// A slot and its surrounding words. <paramref name="onChanged"/> fires after the value has
    /// been written, so the caller can rebuild and record an undo entry from the new state.
    /// </summary>
    public static Control Build(SlotSpec spec, SlotValue value, Action onChanged)
    {
        // Transparent to the mouse, so a press on the words between two slots falls through to the
        // block and starts a drag. Only the slot's own control takes a press.
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);

        if (spec.Lead.Length > 0)
        {
            row.AddChild(Word(spec.Lead));
        }

        row.AddChild(spec.Kind == SlotKind.Number
            ? Number(spec, value, onChanged)
            : Choice(spec, value, onChanged));

        if (spec.Trail.Length > 0)
        {
            row.AddChild(Word(spec.Trail));
        }

        return row;
    }

    /// <summary>
    /// Connective text between slots. Dim and not uppercased: it is neither a label nor a value but
    /// the sentence the block reads as, and shouting it would make the block harder to scan, not
    /// easier.
    /// </summary>
    private static Label Word(string text)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", ShellPalette.TextDim);
        label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        return label;
    }

    private static Control Choice(SlotSpec spec, SlotValue value, Action onChanged)
    {
        var options = ProgramVocabulary.OptionsFor(spec.Kind);
        var button = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        button.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);

        Populate(button, options, value.Id);

        button.ItemSelected += index =>
        {
            var chosen = (int)index;
            if (chosen < 0 || chosen >= options.Count)
            {
                return;
            }

            value.Id = options[chosen].Id;
            onChanged();
        };

        return button;
    }

    /// <summary>
    /// Fills the dropdown and selects the stored value. A stored id the world no longer offers is
    /// added as a disabled entry and selected, rather than quietly snapping to the first option:
    /// a program naming a facility that has gone is precisely the thing the player has to see.
    /// </summary>
    private static void Populate(
        OptionButton button, IReadOnlyList<VocabularyOption> options, string stored)
    {
        var selected = -1;

        for (var i = 0; i < options.Count; i++)
        {
            button.AddItem(options[i].Label, i);

            if (options[i].Id == stored)
            {
                selected = i;
            }
        }

        if (selected >= 0)
        {
            button.Selected = selected;
            return;
        }

        button.AddItem($"[{stored}]", options.Count);
        button.SetItemDisabled(button.ItemCount - 1, true);
        button.Selected = button.ItemCount - 1;
    }

    /// <summary>
    /// A bounded number. The field clamps at the slot's own bounds, so a value outside the range
    /// its author validated cannot be produced from the editor at all.
    /// </summary>
    private static Control Number(SlotSpec spec, SlotValue value, Action onChanged)
    {
        var box = new SpinBox
        {
            MinValue = spec.Min,
            MaxValue = spec.Max,
            Step = 1,
            Value = Math.Clamp(value.Number, spec.Min, spec.Max),
            CustomMinimumSize = new Vector2(NumberWidth, 0),
            FocusMode = Control.FocusModeEnum.All,

            // Committed on focus loss as well as on Enter. A number the player typed and then
            // clicked away from has been decided; discarding it would be the editor disagreeing.
            UpdateOnTextChanged = false,
        };

        box.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        box.GetLineEdit().AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);

        box.ValueChanged += amount =>
        {
            var clamped = (long)Math.Clamp(amount, spec.Min, spec.Max);
            if (clamped == value.Number)
            {
                return;
            }

            value.Number = clamped;
            onChanged();
        };

        return box;
    }
}
