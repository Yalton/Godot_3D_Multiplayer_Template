using MultiplayerTemplate.Game;

namespace MultiplayerTemplate.Tests;

public sealed class AfkPolicyTests
{
    [Fact]
    public void BecomesAfkAtTheThreshold()
    {
        Assert.False(AfkPolicy.Evaluate(89.9, 90, false));
        Assert.True(AfkPolicy.Evaluate(90, 90, false));
    }

    [Fact]
    public void StaysAfkUntilFreshInputResetsTheTimer()
    {
        Assert.True(AfkPolicy.Evaluate(120, 90, true));
        Assert.True(AfkPolicy.Evaluate(1.0, 90, true));
        Assert.False(AfkPolicy.Evaluate(0.0, 90, true));
    }

    [Fact]
    public void NonPositiveThresholdDisablesAfk()
    {
        Assert.False(AfkPolicy.Evaluate(9999, 0, false));
        Assert.False(AfkPolicy.Evaluate(9999, -1, true));
    }

    [Fact]
    public void KickOnlyWhenAKickThresholdIsConfigured()
    {
        Assert.False(AfkPolicy.ShouldKick(9999, 0));
        Assert.False(AfkPolicy.ShouldKick(299, 300));
        Assert.True(AfkPolicy.ShouldKick(300, 300));
    }
}
