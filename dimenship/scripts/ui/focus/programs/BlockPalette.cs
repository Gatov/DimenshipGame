using System;
using System.Collections.Generic;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The editor's right column: every block the language has, filtered and searchable, and each row a
/// drag source.
/// <para>
/// It lives inside the focus zone rather than as a panel in the Inspector zone, because the editor
/// is unusable without it — a palette the player can replace with the Energy Budget panel is a
/// palette that will be missing exactly when it is needed.
/// </para>
/// </summary>
public sealed partial class BlockPalette : VBoxContainer
{
    private readonly List<Button> _tabs = new();

    private LineEdit _search = null!;
    private OptionButton _domain = null!;
    private VBoxContainer _rows = null!;
    private bool _actions;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        CustomMinimumSize = new Vector2(280, 0);

        AddChild(Tabs());

        _search = new LineEdit { PlaceholderText = "Search blocks" };
        _search.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        _search.TextChanged += _ => Refresh();
        AddChild(_search);

        _domain = new OptionButton();
        _domain.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
        for (var i = 0; i < ProgramVocabulary.Domains.Count; i++)
        {
            _domain.AddItem(ProgramVocabulary.Domains[i].ToUpperInvariant(), i);
        }

        _domain.Selected = 0;
        _domain.ItemSelected += _ => Refresh();
        AddChild(_domain);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", ShellPalette.SpaceSm);
        scroll.AddChild(_rows);

        Refresh();
    }

    /// <summary>
    /// <c>CONDITIONS</c> and <c>ACTIONS</c>, and <c>FUNCTIONS</c> rendered disabled rather than
    /// hidden. User-defined reusable groups are a real language feature with real determinism and
    /// validation consequences, and a tab strip that changes length as subsystems ship moves every
    /// tab the player has learned the position of.
    /// </summary>
    private Control Tabs()
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);

        strip.AddChild(Tab("CONDITIONS", enabled: true, actions: false));
        strip.AddChild(Tab("ACTIONS", enabled: true, actions: true));
        strip.AddChild(Tab("FUNCTIONS", enabled: false, actions: false));

        return strip;
    }

    private Button Tab(string text, bool enabled, bool actions)
    {
        var tab = new Button
        {
            Text = text,
            Disabled = !enabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = enabled ? FocusModeEnum.All : FocusModeEnum.None,
        };

        ShellTheme.ApplyGlass(tab);
        tab.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        tab.AddThemeColorOverride("font_disabled_color", ShellPalette.TextFaint);

        if (!enabled)
        {
            return tab;
        }

        tab.SetMeta("actions", actions);
        tab.Pressed += () =>
        {
            _actions = actions;
            Refresh();
        };

        _tabs.Add(tab);
        return tab;
    }

    private void Refresh()
    {
        foreach (var tab in _tabs)
        {
            var mine = tab.GetMeta("actions").AsBool() == _actions;
            tab.AddThemeStyleboxOverride("normal", ShellTheme.Block(
                mine ? ShellPalette.BgGlassHover : Colors.Transparent, highlighted: mine));
            tab.AddThemeColorOverride(
                "font_color", mine ? ShellPalette.TextTitle : ShellPalette.TextDim);
        }

        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        var domain = ProgramVocabulary.Domains[Math.Max(_domain.Selected, 0)];
        var needle = _search.Text.Trim();

        // Filtered before anything is constructed. An Entry is a Node, and a Node built and then
        // discarded by a Where clause is never added to the tree and so is never freed.
        if (_actions)
        {
            foreach (var kind in ProgramVocabulary.ActionKinds)
            {
                var spec = ProgramVocabulary.Spec(kind);
                if (Matches(spec, domain, needle))
                {
                    _rows.AddChild(new Entry(spec, BlockKind.Action) { Action = kind });
                }
            }

            return;
        }

        foreach (var kind in ProgramVocabulary.ConditionKinds)
        {
            var spec = ProgramVocabulary.Spec(kind);
            if (Matches(spec, domain, needle))
            {
                _rows.AddChild(new Entry(spec, BlockKind.Condition) { Condition = kind });
            }
        }
    }

    private static bool Matches(BlockSpec spec, string domain, string needle) =>
        ProgramVocabulary.InDomain(spec, domain) &&
        (needle.Length == 0 ||
         spec.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
         spec.Keyword.Contains(needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One template row, and the source of every drag that adds a block. It is not itself
    /// droppable and holds no draft — dragging it off the palette is what mints one.
    /// </summary>
    private sealed partial class Entry : PanelContainer
    {
        private readonly BlockSpec _spec;
        private readonly BlockKind _kind;

        private bool _hovered;
        private bool _focused;

        public Entry(BlockSpec spec, BlockKind kind)
        {
            _spec = spec;
            _kind = kind;
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
        }

        public ConditionKind? Condition { get; init; }

        public ActionKind? Action { get; init; }

        public override void _Ready()
        {
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", ShellPalette.SpaceXs);
            AddChild(column);

            var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            top.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
            column.AddChild(top);

            var cap = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            cap.AddThemeStyleboxOverride("panel", ShellTheme.Block(Fill(), highlighted: false));
            top.AddChild(cap);

            var keyword = new Label
            {
                Text = _spec.Keyword,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            keyword.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            keyword.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            cap.AddChild(keyword);

            var domain = new Label
            {
                Text = _spec.Domain.ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            domain.AddThemeColorOverride("font_color", ShellPalette.TextFaint);
            domain.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            top.AddChild(domain);

            var summary = new Label
            {
                Text = _spec.Summary,
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            summary.AddThemeColorOverride("font_color", ShellPalette.TextPrimary);
            summary.AddThemeFontSizeOverride("font_size", ShellPalette.FontBody);
            column.AddChild(summary);

            MouseEntered += () => Set(ref _hovered, true);
            MouseExited += () => Set(ref _hovered, false);
            FocusEntered += () => Set(ref _focused, true);
            FocusExited += () => Set(ref _focused, false);

            Apply();
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            var payload = new BlockDragData
            {
                Condition = Condition,
                Action = Action,
                Label = _spec.Keyword,
            };

            var preview = new PanelContainer { Modulate = new Color(1, 1, 1, 0.8f) };
            preview.AddThemeStyleboxOverride(
                "panel", ShellTheme.Block(Fill(), highlighted: true));

            var label = new Label { Text = _spec.Keyword };
            label.AddThemeColorOverride("font_color", ShellPalette.TextTitle);
            label.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
            preview.AddChild(label);

            SetDragPreview(preview);
            return payload;
        }

        private Color Fill() =>
            _kind == BlockKind.Action ? ShellPalette.BlockAction : ShellPalette.BlockCondition;

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
            _hovered ? ShellPalette.BgGlassHover : ShellPalette.BgPanel,
            highlighted: _focused));
    }
}
