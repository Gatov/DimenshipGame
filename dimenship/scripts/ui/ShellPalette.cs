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
    /// The active tab, the focused control, the informational status dot and a normally-loaded
    /// edge: "this is the thing that is working, and nothing is wrong with it".
    /// <para>
    /// <see cref="StateWarn"/> used to double as this. It stops here, because a selection
    /// highlight drawn in the warning colour teaches the player that selection means warning.
    /// </para>
    /// </summary>
    public static readonly Color Accent = Color.FromHtml("58A6D9");

    /// <summary>
    /// A brighter tier than <see cref="TextPrimary"/>, for card and panel titles and the large
    /// numeric readouts. A title at <see cref="TextPrimary"/> over <see cref="BgGlass"/>
    /// disappears into its own rows.
    /// </summary>
    public static readonly Color TextTitle = Color.FromHtml("D6E4EC");

    /// <summary>
    /// How hard a transport line is working, reading grey → green → blue → orange → red: idle,
    /// plenty of headroom, working, near capacity, stopped. Every one aliases another token and
    /// they are named separately anyway — an edge asking for <see cref="StateWarn"/> when it means
    /// "high load" is how the rule that nothing outside this file names a colour erodes.
    /// </summary>
    public static readonly Color FlowIdle = TextDim;
    public static readonly Color FlowLow = StateOk;
    public static readonly Color FlowNormal = Accent;
    public static readonly Color FlowHigh = StateWarn;
    public static readonly Color FlowBlocked = StateFault;

    /// <summary>
    /// A rule block's category in the programming view: control, branch, condition, action.
    /// Category and nothing finer — the same action drawn in two hues would be teaching the player
    /// a distinction the language does not have.
    /// <para>
    /// The colour is redundant by construction: every block also wears its category as a word in
    /// its keyword cap, which is what the rule against state carried by colour alone requires.
    /// </para>
    /// <para>
    /// These three values are read off the concept image with nothing to anchor them, and are
    /// expected to be tuned against the real backdrop, as <see cref="Accent"/> was.
    /// </para>
    /// </summary>
    public static readonly Color BlockControl = Color.FromHtml("1E4A66");

    /// <summary>The alternative path: <c>ELSE IF</c> and <c>ELSE</c>.</summary>
    public static readonly Color BlockBranch = Color.FromHtml("6B4A16");

    /// <summary>Every command. See <see cref="BlockControl"/> for the rule these four follow.</summary>
    public static readonly Color BlockAction = Color.FromHtml("2A4A2E");

    /// <summary>
    /// A condition's slot strip. It aliases <see cref="Border"/> and is named separately anyway —
    /// a block asking for <see cref="Border"/> when it means "this is a condition" is how the rule
    /// that nothing outside this file names a colour erodes.
    /// </summary>
    public static readonly Color BlockCondition = Border;

    /// <summary>Bar troughs, and bar fills at 6px height or above.</summary>
    public const int RadiusSm = 2;

    /// <summary>Buttons, tabs, chips, badges and item rows.</summary>
    public const int RadiusMd = 4;

    /// <summary>Panes, boxes, node cards and the legend.</summary>
    public const int RadiusLg = 8;

    public const int SpaceXs = 2;
    public const int SpaceSm = 4;
    public const int SpaceMd = 8;
    public const int SpaceLg = 12;
    public const int SpaceXl = 16;

    /// <summary>
    /// Pane padding and the gaps between top-bar groups. A separate step rather than a stretched
    /// <see cref="SpaceXl"/>: a scale whose largest value covers both a row gap and a pane inset
    /// has stopped meaning anything.
    /// </summary>
    public const int Space2Xl = 24;

    public const int FontMicro = 9;
    public const int FontBody = 11;
    public const int FontHeading = 13;
    public const int FontNumeric = 22;

    /// <summary>The operational-time readout in the top bar. One instance, deliberately.</summary>
    public const int FontDisplay = 26;
}
