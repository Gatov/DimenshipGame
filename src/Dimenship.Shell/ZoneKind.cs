namespace Dimenship.Shell;

/// <summary>Which kind of zone a panel is allowed to occupy.</summary>
public enum ZoneKind
{
    /// <summary>The centre zone. Exactly one focus view is mounted at a time.</summary>
    Focus,

    /// <summary>The inspector or console zone.</summary>
    Panel,
}
