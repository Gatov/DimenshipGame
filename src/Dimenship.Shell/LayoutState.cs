namespace Dimenship.Shell;

/// <summary>
/// The player's arrangement of the shell. Split offsets are pixel offsets as understood by
/// Godot's SplitContainer, not ratios: the engine already clamps them against child minimum
/// sizes, so storing pixels avoids reimplementing that logic against a moving viewport.
/// </summary>
public sealed record LayoutState(
    PanelId ActiveFocus,
    PanelId InspectorPanel,
    PanelId ConsolePanel,
    int InspectorSplitOffset,
    int ConsoleSplitOffset,
    bool InspectorCollapsed,
    bool ConsoleCollapsed);
