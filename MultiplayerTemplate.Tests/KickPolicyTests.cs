using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class KickPolicyTests
{
    [Fact]
    public void HostMayKickAKnownClient()
    {
        Assert.True(KickPolicy.CanKick(1, 42, true, true));
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
