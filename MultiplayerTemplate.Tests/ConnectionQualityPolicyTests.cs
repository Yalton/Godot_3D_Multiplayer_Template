using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class ConnectionQualityPolicyTests
{
    [Fact]
    public void CalculatesMeanPingJitterAndLoss()
    {
        var quality = ConnectionQualityPolicy.Calculate(
            new[] { 20.0, 30.0, 25.0 }, new[] { true, true, false, true }, 0.1);

        Assert.Equal(25, quality.PingMs, 3);
        Assert.Equal(7.5, quality.JitterMs, 3);
        Assert.Equal(25, quality.PacketLossPercent, 3);
        Assert.Equal(ConnectionHealth.Degraded, quality.Health);
    }

    [Theory]
    [InlineData(30, 2, 0, 0.1, ConnectionHealth.Excellent)]
    [InlineData(120, 2, 0, 0.1, ConnectionHealth.Good)]
    [InlineData(250, 2, 0, 0.1, ConnectionHealth.Degraded)]
    [InlineData(30, 2, 0, 5.01, ConnectionHealth.Timeout)]
    public void ClassifiesThresholds(
        double ping, double jitterDelta, double loss, double stale, ConnectionHealth expected)
    {
        var outcomes = loss == 0 ? new[] { true } : new[] { false };
        var quality = ConnectionQualityPolicy.Calculate(
            new[] { ping, ping + jitterDelta }, outcomes, stale);
        Assert.Equal(expected, quality.Health);
    }

    [Fact]
    public void EmptySamplesAreSafe()
    {
        var quality = ConnectionQualityPolicy.Calculate(
            Array.Empty<double>(), Array.Empty<bool>(), 0);
        Assert.Equal(0, quality.PingMs);
        Assert.Equal(0, quality.JitterMs);
        Assert.Equal(0, quality.PacketLossPercent);
    }

    [Fact]
    public void SanitizationClampsUntrustedReports()
    {
        var quality = ConnectionQualityPolicy.Sanitize(-1, 9000, 140, 999);
        Assert.Equal(0, quality.PingMs);
        Assert.Equal(5000, quality.JitterMs);
        Assert.Equal(100, quality.PacketLossPercent);
        Assert.Equal(ConnectionHealth.Timeout, quality.Health);
    }
}
