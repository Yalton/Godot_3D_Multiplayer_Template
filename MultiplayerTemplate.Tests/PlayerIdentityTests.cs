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
}
