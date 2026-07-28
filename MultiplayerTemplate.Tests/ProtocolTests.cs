using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void ProtocolVersionIsPositive() => Assert.True(NetworkBuildInfo.ProtocolVersion > 0);

    [Fact]
    public void TickRateIsStableAndPositive() => Assert.Equal(60, NetworkClock.TickRate);
}
