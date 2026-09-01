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
}
