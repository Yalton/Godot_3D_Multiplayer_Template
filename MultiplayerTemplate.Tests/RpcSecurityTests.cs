using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class RpcRateLimiterTests
{
    [Fact]
    public void AllowsUpToLimitWithinWindow()
    {
        long now = 1000;
        var limiter = new RpcRateLimiter(() => now);

        Assert.True(limiter.Allow(7, "chat", 2, 1000));
        Assert.True(limiter.Allow(7, "chat", 2, 1000));
        Assert.False(limiter.Allow(7, "chat", 2, 1000));
    }

    [Fact]
    public void ResetsAfterWindow()
    {
        long now = 1000;
        var limiter = new RpcRateLimiter(() => now);
        Assert.True(limiter.Allow(7, "chat", 1, 1000));
        Assert.False(limiter.Allow(7, "chat", 1, 1000));

        now = 2000;

        Assert.True(limiter.Allow(7, "chat", 1, 1000));
    }

    [Fact]
    public void SeparatesPeersAndActions()
    {
        var limiter = new RpcRateLimiter(() => 1000);
        Assert.True(limiter.Allow(1, "chat", 1, 1000));

        Assert.True(limiter.Allow(2, "chat", 1, 1000));
        Assert.True(limiter.Allow(1, "interact", 1, 1000));
    }

    [Fact]
    public void ForgetClearsOnlyRequestedPeer()
    {
        var limiter = new RpcRateLimiter(() => 1000);
        limiter.Allow(1, "chat", 1, 1000);
        limiter.Allow(2, "chat", 1, 1000);

        limiter.Forget(1);

        Assert.True(limiter.Allow(1, "chat", 1, 1000));
        Assert.False(limiter.Allow(2, "chat", 1, 1000));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 0)]
    [InlineData(-1, 1000)]
    public void RejectsInvalidConfiguration(int maximum, long windowMs)
    {
        var limiter = new RpcRateLimiter(() => 1000);
        Assert.False(limiter.Allow(1, "chat", maximum, windowMs));
    }
}

public sealed class RpcCooldownTests
{
    [Fact]
    public void EnforcesAndExpiresCooldown()
    {
        long now = 1000;
        var cooldowns = new RpcCooldowns(() => now);

        Assert.True(cooldowns.TryConsume(3, "ability", 500));
        Assert.False(cooldowns.TryConsume(3, "ability", 500));
        now = 1500;
        Assert.True(cooldowns.TryConsume(3, "ability", 500));
    }

    [Fact]
    public void SeparatesPeersAndActions()
    {
        var cooldowns = new RpcCooldowns(() => 1000);
        Assert.True(cooldowns.TryConsume(1, "a", 500));
        Assert.True(cooldowns.TryConsume(2, "a", 500));
        Assert.True(cooldowns.TryConsume(1, "b", 500));
    }

    [Fact]
    public void RejectsNegativeCooldown()
    {
        Assert.False(new RpcCooldowns(() => 1000).TryConsume(1, "a", -1));
    }
}
