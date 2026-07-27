namespace Dimenship.Core;

/// <summary>Static identity of the game, shared by the engine layer and tests.</summary>
public static class GameInfo
{
    public const string Title = "Dimenship";
    public const string Version = "0.1.0";

    public static string DisplayVersion => $"v{Version}";
}
