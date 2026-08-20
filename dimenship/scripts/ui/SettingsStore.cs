using Dimenship.Shell;
using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Reads and writes the settings file. All degraded-input decisions belong to SettingsSerializer.
/// This is <see cref="LayoutStore"/> for a different file, deliberately: the two are the only
/// <c>user://</c> files the game has, and a second set of I/O conventions for the second one would
/// be a second set of bugs.
/// </summary>
public static class SettingsStore
{
    public const string Path = "user://settings.json";
    public const string QuarantinePath = "user://settings.json.bad";

    public static SettingsLoadResult Load(SettingsState defaults)
    {
        if (!FileAccess.FileExists(Path))
        {
            return SettingsSerializer.Load(null, defaults);
        }

        string text;
        using (var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read))
        {
            if (file is null)
            {
                GD.PushWarning($"Could not open {Path}: {FileAccess.GetOpenError()}");
                return SettingsSerializer.Load(null, defaults);
            }

            text = file.GetAsText();
        }
        // The read handle is closed before any rename is attempted: Godot's FileAccess opens
        // without FILE_SHARE_DELETE, so on Windows a rename against a still-open handle fails
        // with a sharing violation.

        var result = SettingsSerializer.Load(text, defaults);

        if (result.UsedDefault)
        {
            // Kept, not deleted: this is the one file a player is likely to have edited by hand,
            // and deleting it destroys the evidence of what they got wrong.
            var renameError = DirAccess.RenameAbsolute(
                ProjectSettings.GlobalizePath(Path),
                ProjectSettings.GlobalizePath(QuarantinePath));
            if (renameError != Error.Ok)
            {
                GD.PushWarning($"Could not quarantine {Path} to {QuarantinePath}: {renameError}");
            }
        }

        foreach (var warning in result.Warnings)
        {
            GD.PushWarning($"settings: {warning}");
        }

        return result;
    }

    public static void Save(SettingsState state)
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Could not write {Path}: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(SettingsSerializer.ToJson(state));
    }
}
