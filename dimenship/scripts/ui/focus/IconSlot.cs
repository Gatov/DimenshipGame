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

    public IconSlot(string domain, string name, int size, Color tint)
    {
        CustomMinimumSize = new Vector2(size, size);
        StretchMode = StretchModeEnum.KeepAspectCentered;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        MouseFilter = MouseFilterEnum.Ignore;
        Modulate = tint;

        // Loaded rather than preloaded, and null-checked rather than trusted: a missing icon is an
        // empty slot by specification, and ResourceLoader is the only load that says so quietly.
        var path = $"{Root}/{domain}/{name}.svg";
        if (ResourceLoader.Exists(path))
        {
            Texture = ResourceLoader.Load<Texture2D>(path);
        }
        else
        {
            GD.PushWarning($"Icon '{path}' is missing; its slot is drawn empty.");
        }
    }

    /// <summary>Retints an existing slot, for an icon whose colour tracks a live state.</summary>
    public void SetTint(Color tint) => Modulate = tint;
}
