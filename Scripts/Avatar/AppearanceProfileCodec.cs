namespace MultiplayerTemplate.Avatar;

/// <summary>Converts appearance IDs to and from the primitive roster/profile shape.</summary>
public static class AppearanceProfileCodec
{
    public const string BodyColorKey = "appearance_body_color";
    public const string HeadKey = "appearance_head";
    public const string BackKey = "appearance_back";
    public const string HipsKey = "appearance_hips";
    public const string HandKey = "appearance_hand";

    public static Dictionary<string, int> ToDictionary(AvatarAppearance appearance)
    {
        var normalized = CosmeticCatalogue.Normalize(appearance);
        return new Dictionary<string, int>
        {
            [BodyColorKey] = normalized.BodyColorId,
            [HeadKey] = normalized.HeadId,
            [BackKey] = normalized.BackId,
            [HipsKey] = normalized.HipsId,
            [HandKey] = normalized.HandId
        };
    }

    public static AvatarAppearance FromDictionary(IReadOnlyDictionary<string, int> values) => CosmeticCatalogue.Normalize(new AvatarAppearance(
        ReadId(values, BodyColorKey, AvatarAppearance.Default.BodyColorId),
        ReadId(values, HeadKey, CosmeticCatalogue.None),
        ReadId(values, BackKey, CosmeticCatalogue.None),
        ReadId(values, HipsKey, CosmeticCatalogue.None),
        ReadId(values, HandKey, CosmeticCatalogue.None)));

    private static int ReadId(IReadOnlyDictionary<string, int> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) ? value : fallback;
    }
}
