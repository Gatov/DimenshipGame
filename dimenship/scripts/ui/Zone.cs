using System;
using System.Linq;
using Dimenship.Core.Simulation;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// A host for exactly one panel, with a header carrying the panel picker and a collapse control.
/// Panels cannot be dragged between zones — that was a deliberate scope decision.
/// </summary>
public sealed partial class Zone : VBoxContainer
{
    private readonly ZoneKind _kind;
    private readonly PanelRegistry _registry;
    private readonly ShellContext _context;
    private readonly bool _showPicker;

    private Label _title = null!;
    private MenuButton _picker = null!;
    private MarginContainer _host = null!;
    private PanelBase? _current;
    private WorldSnapshot? _lastSnapshot;

    public Zone(ZoneKind kind, PanelRegistry registry, ShellContext context, bool showPicker = true)
    {
        _kind = kind;
        _registry = registry;
        _context = context;
        _showPicker = showPicker;
    }

    public event Action<PanelId>? PanelChosen;

    public PanelId? CurrentId => _current?.Id;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", ShellPalette.SpaceMd);
        AddChild(header);

        _title = new Label { VerticalAlignment = VerticalAlignment.Center };
        _title.AddThemeColorOverride("font_color", ShellPalette.StateWarn);
        _title.AddThemeFontSizeOverride("font_size", ShellPalette.FontMicro);
        header.AddChild(_title);

        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _picker = new MenuButton { Text = "\u25be", Visible = _showPicker };
        _picker.GetPopup().IdPressed += OnPickerIdPressed;
        header.AddChild(_picker);

        _host = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_host);

        RebuildPicker();
    }

    /// <summary>Mounts the named panel, replacing whatever was there.</summary>
    public void Show(PanelId id)
    {
        if (_current is not null)
        {
            _current.OnUnmount();
            _host.RemoveChild(_current);
            _current.QueueFree();
            _current = null;
        }

        var panel = _registry.Create(id)
                    ?? new PlaceholderPanel(
                        id,
                        "Unknown panel",
                        $"No panel is registered under '{id}'. The layout named it, but nothing builds it.",
                        _kind,
                        ShellPalette.StateFault);

        _current = panel;
        _host.AddChild(panel);
        panel.OnMount(_context);
        _title.Text = panel.Title.ToUpperInvariant();

        // A newly mounted panel would otherwise wait for the next _Process tick to see any data,
        // and _Process only delivers when the snapshot reference changes — which never happens
        // while the simulation is paused. Re-deliver whatever we last saw immediately so a panel
        // switch while paused does not leave the panel blank indefinitely.
        if (_lastSnapshot is not null)
        {
            panel.OnSnapshot(_lastSnapshot);
        }
    }

    public void Deliver(WorldSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        _current?.OnSnapshot(snapshot);
    }

    private void RebuildPicker()
    {
        var popup = _picker.GetPopup();
        popup.Clear();

        var options = _registry.OfKind(_kind).OrderBy(d => d.Title).ToList();
        for (var i = 0; i < options.Count; i++)
        {
            popup.AddItem(options[i].Title, i);
            popup.SetItemMetadata(i, options[i].Id.Value);
        }
    }

    private void OnPickerIdPressed(long id)
    {
        var popup = _picker.GetPopup();
        var index = popup.GetItemIndex((int)id);
        if (index < 0)
        {
            return;
        }

        var value = popup.GetItemMetadata(index).AsString();
        if (!string.IsNullOrEmpty(value))
        {
            PanelChosen?.Invoke(new PanelId(value));
        }
    }
}
