using Godot;

namespace MultiplayerTemplate.Network;

public static class NetworkBuildInfo
{
    // Increment whenever an RPC signature, replicated payload, or connection flow changes.
    public const int ProtocolVersion = 9;

    public static string GameVersion =>
        ProjectSettings.GetSetting("application/config/version", "0.1.0").AsString();

    public static string BuildId => $"{GameVersion}-protocol.{ProtocolVersion}";
}
