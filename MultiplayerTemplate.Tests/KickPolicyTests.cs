using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class KickPolicyTests
{
    [Theory]
    [InlineData(1, 42, true, true)]
    [InlineData(1, 2, true, true)]
    [InlineData(1, 100, true, true)]
    [InlineData(7, 42, true, true)]
    [InlineData(7, 999, true, true)]
    public void HostMayKickAKnownClient(
        long senderPeerId, long targetPeerId, bool senderIsHost, bool targetIsKnown)
    {
        Assert.True(KickPolicy.CanKick(senderPeerId, targetPeerId, senderIsHost, targetIsKnown));
    }

    [Fact]
    public void NonHostMayNotKick()
    {
        Assert.False(KickPolicy.CanKick(42, 43, false, true));
    }

    [Fact]
    public void UnknownTargetIsRejected()
    {
        Assert.False(KickPolicy.CanKick(1, 42, true, false));
    }

    [Fact]
    public void HostMayNotKickItself()
    {
        Assert.False(KickPolicy.CanKick(1, 1, true, true));
    }

    [Fact]
    public void HostMayNotKickItselfWhenItIsNotTheServerPeer()
    {
        Assert.False(KickPolicy.CanKick(5, 5, true, true));
    }

    [Fact]
    public void ServerPeerIsNeverAValidTarget()
    {
        Assert.False(KickPolicy.CanKick(99, 1, true, true));
    }
}
