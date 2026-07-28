namespace Dimenship.Shell;

/// <summary>What the shell knows about a panel without constructing it.</summary>
public sealed record PanelDescriptor(PanelId Id, string Title, ZoneKind Zone);
