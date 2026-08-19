using Godot;

namespace Dimenship.Ui;

/// <summary>
/// A fixed-size slot holding one flat vector icon, tinted from <see cref="ShellPalette"/>.
/// <para>
/// The size is the slot's, never the artwork's: a slot reserves its space whether or not anything
/// is in it, so a missing file leaves a gap of the right shape instead of collapsing the layout
/// around it or drawing a broken-texture placeholder. Icons carry no colour of their own — one
/// that did would be a colour literal outside the palette — so every one of them is tinted here.
/// </para>
/// </summary>
public sealed partial class IconSlot : TextureRect
{
    /// <summary>A graph node card and the inspector header.</summary>
    public const int CardSize = 40;

    /// <summary>An item row, a resource table row, an alert severity.</summary>
    public const int RowSize = 16;

    /// <summary>
    /// Where the icons live, by domain: <c>facility</c>, <c>item</c>, <c>status</c>, <c>control</c>.
    /// One place, so a renamed folder is one edit rather than a hunt through every card.
    /// </summary>
    private const string Root = "res://assets/icons";

    /// <summary>
    /// The path currently shown. Slots that track live state are told their icon on every
    /// snapshot, and reloading the same file sixty times a second — or re-pushing the same
    /// missing-file warning that often — would be the cost of not remembering.
    /// </summary>
    private string? _shown;

    public IconSlot(string domain, string name, int size, Color tint)
        : this(size, tint) => SetIcon(domain, name);

    /// <summary>
    /// An empty slot, for a site that only learns which icon it wants once a snapshot arrives.
    /// It still reserves its space, so a row does not reflow when its first icon appears.
    /// </summary>
    public IconSlot(int size, Color tint)
    {
        CustomMinimumSize = new Vector2(size, size);
        StretchMode = StretchModeEnum.KeepAspectCentered;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        MouseFilter = MouseFilterEnum.Ignore;
        Modulate = tint;

        // Shrunk on both axes, or the slot is exactly as tall as whatever row it lands in and the
        // artwork scales up to fill it: the importer renders these at svg/scale=2.0, so there is
        // plenty of texture to stretch and a 16px row icon comes out 40px tall in a tall row.
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    /// <summary>
    /// Loads an icon for a site that is not a slot — a <see cref="Button"/>'s own icon, which the
    /// button draws and tints itself. Null when the file is missing, which every caller renders as
    /// no icon rather than as a broken texture.
    /// </summary>
    public static Texture2D? Load(string domain, string name) => Load($"{Root}/{domain}/{name}.svg");

    /// <summary>Swaps the icon shown, for a slot whose subject changes between snapshots.</summary>
    public void SetIcon(string domain, string name)
    {
        var path = $"{Root}/{domain}/{name}.svg";
        if (_shown == path)
        {
            return;
        }

        _shown = path;
        Texture = Load(path);
    }

    /// <summary>Empties the slot without collapsing it, for a row that has nothing to show.</summary>
    public void Clear()
    {
        _shown = null;
        Texture = null;
    }

    /// <summary>Retints an existing slot, for an icon whose colour tracks a live state.</summary>
    public void SetTint(Color tint) => Modulate = tint;

    // Loaded rather than preloaded, and null-checked rather than trusted: a missing icon is an
    // empty slot by specification, and ResourceLoader is the only load that says so quietly.
    private static Texture2D? Load(string path)
    {
        if (ResourceLoader.Exists(path))
        {
            return ResourceLoader.Load<Texture2D>(path);
        }

        GD.PushWarning($"Icon '{path}' is missing; its slot is drawn empty.");
        return null;
    }
}
