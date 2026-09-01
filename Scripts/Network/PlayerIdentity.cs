using Godot;
using Godot.Collections;
using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Network;

public readonly record struct PlayerIdentity(
    long PeerId,
    string DisplayName,
    string Platform,
    long SteamId,
    bool IsHost,
    bool Ready,
    bool Afk = false,
    AvatarAppearance appearance = default)
{
    public AvatarAppearance appearance { get; init; } =
        appearance == default ? AvatarAppearance.Default : appearance;

    public AvatarAppearance Appearance
    {
        get => appearance;
        init => appearance = value;
    }

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
        data.GetValueOrDefault("afk", false).AsBool(),
        AppearanceProfileCodec.FromDictionary(new System.Collections.Generic.Dictionary<string, int>
        {
            [AppearanceProfileCodec.BodyColorKey] = ReadAppearanceId(data, AppearanceProfileCodec.BodyColorKey, AvatarAppearance.Default.BodyColorId),
            [AppearanceProfileCodec.HeadKey] = ReadAppearanceId(data, AppearanceProfileCodec.HeadKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.BackKey] = ReadAppearanceId(data, AppearanceProfileCodec.BackKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.HipsKey] = ReadAppearanceId(data, AppearanceProfileCodec.HipsKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.HandKey] = ReadAppearanceId(data, AppearanceProfileCodec.HandKey, CosmeticCatalogue.None)
        }));

    public Dictionary ToDictionary()
    {
        var values = new Dictionary
        {
            ["name"] = DisplayName,
            ["platform"] = Platform,
            ["steam_id"] = SteamId,
            ["is_host"] = IsHost,
            ["ready"] = Ready,
            ["afk"] = Afk
        };
        var appearance = AppearanceProfileCodec.ToDictionary(Appearance);
        values[AppearanceProfileCodec.BodyColorKey] = appearance[AppearanceProfileCodec.BodyColorKey];
        values[AppearanceProfileCodec.HeadKey] = appearance[AppearanceProfileCodec.HeadKey];
        values[AppearanceProfileCodec.BackKey] = appearance[AppearanceProfileCodec.BackKey];
        values[AppearanceProfileCodec.HipsKey] = appearance[AppearanceProfileCodec.HipsKey];
        values[AppearanceProfileCodec.HandKey] = appearance[AppearanceProfileCodec.HandKey];
        return values;
    }

    private static int ReadAppearanceId(Dictionary data, string key, int fallback) =>
        data.GetValueOrDefault(key, fallback).AsInt32();
}
