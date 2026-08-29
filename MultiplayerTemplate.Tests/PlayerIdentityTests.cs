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
    public void AfkSurvivesADictionaryRoundTrip()
    {
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, true);
        var restored = PlayerIdentity.FromDictionary(7, identity.ToDictionary());
        Assert.True(restored.Afk);
        Assert.True(restored.Ready);
        Assert.Equal("Ann", restored.DisplayName);
    }

    [Fact]
    public void PayloadWithoutAnAfkKeyDefaultsToPresent()
    {
        var legacy = new Godot.Collections.Dictionary
        {
            ["name"] = "Bo",
            ["platform"] = "LAN",
            ["steam_id"] = 0L,
            ["is_host"] = false,
            ["ready"] = false
        };
        Assert.False(PlayerIdentity.FromDictionary(9, legacy).Afk);
    }
}
