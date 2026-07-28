using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class NetworkTimeMathTests
{
    [Fact]
    public void ComputesNtpStyleOffset()
    {
        // Client 1000 -> 1200, server received at its local 5100.
        Assert.Equal(4000, NetworkTimeMath.OffsetUsec(1000, 1200, 5100));
    }

    [Fact]
    public void NegativeRoundTripIsClamped()
    {
        Assert.Equal(4100, NetworkTimeMath.OffsetUsec(1200, 1000, 5100));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000_000, 60)]
    [InlineData(1_500_000, 90)]
    public void ConvertsServerTimeToTick(long usec, ulong expected)
    {
        Assert.Equal(expected, NetworkTimeMath.TickAt(usec));
    }

    [Fact]
    public void RemainingTimeNeverGoesNegative()
    {
        Assert.Equal(1.5, NetworkTimeMath.RemainingSeconds(2_500_000, 1_000_000), 3);
        Assert.Equal(0, NetworkTimeMath.RemainingSeconds(1_000_000, 2_000_000));
    }

    [Theory]
    [InlineData(2_000_000, 1_000_000, 2, 0.5)]
    [InlineData(1_000_000, 2_000_000, 2, 1)]
    [InlineData(2_000_000, 1_000_000, 0, 1)]
    public void CalculatesClampedProgress(
        long deadline, long now, double duration, float expected)
    {
        Assert.Equal(expected, NetworkTimeMath.Progress(deadline, now, duration), 3);
    }
}
