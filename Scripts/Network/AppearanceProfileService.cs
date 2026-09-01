using Godot;
using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Network;

/// <summary>Persists the local player's normalized appearance intent.</summary>
public partial class AppearanceProfileService : Node
{
    public static AppearanceProfileService Instance { get; private set; } = null!;
    public AvatarAppearance RequestedAppearance { get; private set; } = AvatarAppearance.Default;

    private const string DefaultProfilePath = "user://appearance_profile.cfg";
    private const string Section = "appearance";
    private readonly ConfigFile _config = new();
    private string _profilePath = DefaultProfilePath;

    public override void _Ready()
    {
        Instance = this;
        LoadProfile(DefaultProfilePath);
    }

    /// <summary>Loads one profile path; exposed so engine smoke can recreate the real ConfigFile boundary.</summary>
    public void LoadProfile(string profilePath)
    {
        _profilePath = profilePath;
        if (_config.Load(_profilePath) != Error.Ok) _config.Clear();
        var values = new Dictionary<string, int>
        {
            [AppearanceProfileCodec.BodyColorKey] = Read(AppearanceProfileCodec.BodyColorKey, AvatarAppearance.Default.BodyColorId),
            [AppearanceProfileCodec.HeadKey] = Read(AppearanceProfileCodec.HeadKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.BackKey] = Read(AppearanceProfileCodec.BackKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.HipsKey] = Read(AppearanceProfileCodec.HipsKey, CosmeticCatalogue.None),
            [AppearanceProfileCodec.HandKey] = Read(AppearanceProfileCodec.HandKey, CosmeticCatalogue.None)
        };
        RequestedAppearance = AppearanceProfileCodec.FromDictionary(values);
        Save(RequestedAppearance);
    }

    public void SetRequestedAppearance(AvatarAppearance appearance)
    {
        RequestedAppearance = CosmeticCatalogue.Normalize(appearance);
        Save(RequestedAppearance);
    }

    private int Read(string key, int fallback) => _config.GetValue(Section, key, fallback).AsInt32();

    private void Save(AvatarAppearance appearance)
    {
        foreach (var pair in AppearanceProfileCodec.ToDictionary(appearance)) _config.SetValue(Section, pair.Key, pair.Value);
        _config.Save(_profilePath);
    }
}
