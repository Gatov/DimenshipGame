using System.Collections.Generic;
using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>Reads and writes the layout file. All degraded-input decisions belong to LayoutSerializer.</summary>
public static class LayoutStore
{
    public const string Path = "user://layout.json";
    public const string QuarantinePath = "user://layout.json.bad";

    public static LayoutLoadResult Load(
        IReadOnlyDictionary<PanelId, PanelDescriptor> known,
        LayoutState defaults)
    {
        if (!FileAccess.FileExists(Path))
        {
            return LayoutSerializer.Load(null, known, defaults);
        }

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushWarning($"Could not open {Path}: {FileAccess.GetOpenError()}");
            return LayoutSerializer.Load(null, known, defaults);
        }

        var result = LayoutSerializer.Load(file.GetAsText(), known, defaults);

        if (result.UsedDefault)
        {
            // Kept, not deleted: an unreadable layout is a bug report, and deleting it destroys
            // the only evidence of what went wrong.
            DirAccess.RenameAbsolute(
                ProjectSettings.GlobalizePath(Path),
                ProjectSettings.GlobalizePath(QuarantinePath));
        }

        foreach (var warning in result.Warnings)
        {
            GD.PushWarning($"layout: {warning}");
        }

        return result;
    }

    public static void Save(LayoutState state)
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Could not write {Path}: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(LayoutSerializer.ToJson(state));
    }
}
