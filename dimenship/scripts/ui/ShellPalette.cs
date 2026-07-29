using Godot;

namespace Dimenship.Ui;

/// <summary>
/// The single source of truth for shell colours, spacing and type sizes. Nothing in the shell
/// may hard-code a colour: changing the visual direction must mean changing this file only.
/// </summary>
public static class ShellPalette
{
    public static readonly Color BgBase = Color.FromHtml("0A0D0F");
    public static readonly Color BgPanel = Color.FromHtml("12181C");
    public static readonly Color Border = Color.FromHtml("1E2A31");
    public static readonly Color TextPrimary = Color.FromHtml("8FA3AD");
    public static readonly Color TextDim = Color.FromHtml("4A6270");
    public static readonly Color TextFaint = Color.FromHtml("3D525C");
    public static readonly Color StateOk = Color.FromHtml("00E5C0");
    public static readonly Color StateWarn = Color.FromHtml("FFB000");
    public static readonly Color StateFault = Color.FromHtml("FF4D4D");

    public const int SpaceXs = 2;
    public const int SpaceSm = 4;
    public const int SpaceMd = 8;
    public const int SpaceLg = 12;
    public const int SpaceXl = 16;

    public const int FontMicro = 9;
    public const int FontBody = 11;
    public const int FontHeading = 13;
    public const int FontNumeric = 22;
}
