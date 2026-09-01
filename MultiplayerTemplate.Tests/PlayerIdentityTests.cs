using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class PlayerIdentityTests
{
    [Fact]
    public void SteamIdentityUsesStableSteamKey()
    {
        var identity = new PlayerIdentity(2, "Name", "Steam", 7656119, false, false);
        Assert.True(identity.HasSteamIdentity);
        Assert.Equal("steam_7656119", identity.StableSocialKey);
    }

    [Fact]
    public void LanIdentityNormalizesCommonSeparators()
    {
        var identity = new PlayerIdentity(2, "Player One/Test", "LAN", 0, false, false);
        Assert.False(identity.HasSteamIdentity);
        Assert.Equal("lan_player_one_test", identity.StableSocialKey);
    }

    [Theory]
    [InlineData("LAN", 123)]
    [InlineData("Steam", 0)]
    public void SteamIdentityRequiresPlatformAndId(string platform, long steamId)
    {
        var identity = new PlayerIdentity(2, "Name", platform, steamId, false, false);
        Assert.False(identity.HasSteamIdentity);
    }

    [Fact]
    public void AfkDefaultsToPresent()
    {
        Assert.False(new PlayerIdentity(7, "Ann", "LAN", 0, false, true).Afk);
    }

    [Fact]
    public void AfkIsCarriedOnTheRecord()
    {
        Assert.True(new PlayerIdentity(7, "Ann", "LAN", 0, false, true, true).Afk);
    }

    [Fact]
    public void OmittedAppearanceUsesCanonicalDefault()
    {
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true);
        var (_, _, _, _, _, _, _, deconstructedAppearance) = identity;

        Assert.Equal(AvatarAppearance.Default, identity.Appearance);
        Assert.Equal(AvatarAppearance.Default, deconstructedAppearance);
    }

    [Fact]
    public void ExplicitAppearanceIsRetained()
    {
        var appearance = new AvatarAppearance(CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.BackItemId, CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId);

        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, false, appearance);

        Assert.Equal(appearance, identity.Appearance);
    }

    [Fact]
    public void RetainsPositionalRecordCompatibilityForAppearance()
    {
        var constructor = typeof(PlayerIdentity).GetConstructors()
            .Single(candidate => candidate.GetParameters().Length == 8);
        var parameters = constructor.GetParameters();

        Assert.NotNull(typeof(PlayerIdentity).GetMethod("Deconstruct"));
        Assert.True(typeof(PlayerIdentity).GetProperty(nameof(PlayerIdentity.Appearance))!.CanWrite);
        Assert.True(parameters[6].IsOptional);
        Assert.True(parameters[7].IsOptional);
    }

    [Fact]
    public void SupportsNamedAppearanceWithExpressionsAndDeconstruction()
    {
        var appearance = new AvatarAppearance(CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.BackItemId, CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId);
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, appearance: appearance);
        var updated = identity with { Ready = false };
        var (_, _, _, _, _, ready, _, restoredAppearance) = updated;

        Assert.False(ready);
        Assert.Equal(appearance, restoredAppearance);
    }

}
