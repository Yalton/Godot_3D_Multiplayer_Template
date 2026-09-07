using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class AppearanceProfileCodecTests
{
    [Fact]
    public void MalformedProfileIdsNormalizeToDefaults()
    {
        var values = new Dictionary<string, int>
        {
            ["appearance_body_color"] = 999,
            ["appearance_head"] = 999,
            ["appearance_back"] = -2,
            ["appearance_hips"] = 0,
            ["appearance_hand"] = 999
        };

        Assert.Equal(AvatarAppearance.Default, AppearanceProfileCodec.FromDictionary(values));
    }

    [Fact]
    public void FullAppearanceDictionaryRoundTrips()
    {
        var appearance = new AvatarAppearance(CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.BackItemId, CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId);

        var restored = AppearanceProfileCodec.FromDictionary(AppearanceProfileCodec.ToDictionary(appearance));

        Assert.Equal(appearance, restored);
    }

    [Fact]
    public void FullAppearanceDictionaryContainsEveryRosterKey()
    {
        var values = AppearanceProfileCodec.ToDictionary(new AvatarAppearance(
            CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId, CosmeticCatalogue.BackItemId,
            CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId));

        Assert.Equal(5, values.Count);
        Assert.Equal(CosmeticCatalogue.GreenBodyId, values[AppearanceProfileCodec.BodyColorKey]);
        Assert.Equal(CosmeticCatalogue.HeadItemId, values[AppearanceProfileCodec.HeadKey]);
        Assert.Equal(CosmeticCatalogue.BackItemId, values[AppearanceProfileCodec.BackKey]);
        Assert.Equal(CosmeticCatalogue.HipsItemId, values[AppearanceProfileCodec.HipsKey]);
        Assert.Equal(CosmeticCatalogue.HandItemId, values[AppearanceProfileCodec.HandKey]);
    }

    [Fact]
    public void PartialAppearanceDictionaryUsesDefaultsForOmittedSlots()
    {
        var appearance = AppearanceProfileCodec.FromDictionary(new Dictionary<string, int>
        {
            [AppearanceProfileCodec.HeadKey] = CosmeticCatalogue.HeadItemId
        });

        Assert.Equal(new AvatarAppearance(AvatarAppearance.Default.BodyColorId, CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.None, CosmeticCatalogue.None, CosmeticCatalogue.None), appearance);
    }
}
