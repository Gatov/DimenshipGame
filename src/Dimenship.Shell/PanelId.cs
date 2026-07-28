namespace Dimenship.Shell;

/// <summary>Stable identifier for a panel. Persisted in layout files, so values must not be renamed casually.</summary>
public readonly record struct PanelId(string Value)
{
    public override string ToString() => Value;
}
