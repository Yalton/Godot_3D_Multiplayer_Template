using Godot;
using Godot.Collections;

namespace MultiplayerTemplate.Network;

public readonly record struct PlayerIdentity(
    long PeerId,
    string DisplayName,
    string Platform,
    long SteamId,
    bool IsHost,
    bool Ready,
    bool Afk = false)
{
    public bool HasSteamIdentity => Platform == "Steam" && SteamId != 0;
    public string StableSocialKey => HasSteamIdentity
        ? $"steam_{SteamId}"
        : $"lan_{DisplayName.ToLowerInvariant().Replace(' ', '_').Replace('/', '_')}";

    public static PlayerIdentity FromDictionary(long peerId, Dictionary data) => new(
        peerId,
        data.GetValueOrDefault("name", $"Player {peerId}").AsString(),
        data.GetValueOrDefault("platform", "LAN").AsString(),
        data.GetValueOrDefault("steam_id", 0L).AsInt64(),
        data.GetValueOrDefault("is_host", peerId == 1).AsBool(),
        data.GetValueOrDefault("ready", false).AsBool(),
        data.GetValueOrDefault("afk", false).AsBool());

    public Dictionary ToDictionary() => new()
    {
        ["name"] = DisplayName,
        ["platform"] = Platform,
        ["steam_id"] = SteamId,
        ["is_host"] = IsHost,
        ["ready"] = Ready,
        ["afk"] = Afk
    };
}
