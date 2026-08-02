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

    /// <summary>
    /// Laid over the backdrop image. The nebula is far brighter than any HUD colour here, so
    /// without this the dim text and hairline borders lose their contrast wherever it is bright.
    /// </summary>
    public static readonly Color BgScrim = Color.FromHtml("0A0D0FA6");

    /// <summary>
    /// Mixed over the blurred backdrop inside a panel. The alpha is a mix weight rather than an
    /// opacity — the frosted pane draws opaque — and it is the dial between a readable panel and
    /// a visible nebula: lower lets more of the image through.
    /// </summary>
    public static readonly Color BgGlass = Color.FromHtml("12181CA6");

    /// <summary>
    /// Hover and press fills for a glassed button. A glassed button has no fill of its own — the
    /// pane behind it is the surface — so these only have to read as a state change over it.
    /// </summary>
    public static readonly Color BgGlassHover = Color.FromHtml("1E2A3199");
    public static readonly Color BgGlassPressed = Color.FromHtml("1E2A31CC");
    public static readonly Color Border = Color.FromHtml("1E2A31");
    public static readonly Color TextPrimary = Color.FromHtml("8FA3AD");
    public static readonly Color TextDim = Color.FromHtml("4A6270");
    public static readonly Color TextFaint = Color.FromHtml("3D525C");
    public static readonly Color StateOk = Color.FromHtml("00E5C0");
    public static readonly Color StateWarn = Color.FromHtml("FFB000");
    public static readonly Color StateFault = Color.FromHtml("FF4D4D");

    /// <summary>
    /// How hard a transport line is working. Four of the five alias a state colour today, and are
    /// named separately anyway: an edge asking for <see cref="StateWarn"/> when it means "high
    /// load" is how the rule that nothing outside this file names a colour erodes.
    /// </summary>
    public static readonly Color FlowIdle = TextDim;

    /// <summary>The one genuinely new colour: moving, but well short of capacity.</summary>
    public static readonly Color FlowLow = Color.FromHtml("0E8C7A");
    public static readonly Color FlowNormal = StateOk;
    public static readonly Color FlowHigh = StateWarn;
    public static readonly Color FlowBlocked = StateFault;

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
