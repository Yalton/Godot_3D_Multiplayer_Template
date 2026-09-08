using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceWireTests
{
    [Fact]
    public void WirePolicyUsesFixed16Khz320SampleContract()
    {
        Assert.Equal(1, VoiceWirePolicy.FormatVersion);
        Assert.Equal(16000, VoiceWirePolicy.SampleRate);
        Assert.Equal(320, VoiceWirePolicy.FrameSamples);
        Assert.Equal(512, VoiceWirePolicy.MaxPayloadBytes);
    }

    [Fact]
    public void ValidPacketPassesWireValidation()
    {
        var packet = new VoicePacket(1, VoiceChannel.Global, 42, [1, 2, 3]);

        Assert.True(VoiceWirePolicy.IsValid(packet));
    }

    [Fact]
    public void RejectsMalformedPackets()
    {
        Assert.False(VoiceWirePolicy.IsValid(new VoicePacket(0, VoiceChannel.Global, 1, [1])));
        Assert.False(VoiceWirePolicy.IsValid(new VoicePacket(1, (VoiceChannel)123, 1, [1])));
        Assert.False(VoiceWirePolicy.IsValid(new VoicePacket(1, VoiceChannel.Global, 1, [])));
        Assert.False(VoiceWirePolicy.IsValid(new VoicePacket(1, VoiceChannel.Global, 1, new byte[513])));
    }

    [Fact]
    public void SequenceFreshnessWrapsAcrossUintMaxValue()
    {
        Assert.True(VoiceWirePolicy.IsNewerSequence(uint.MaxValue, 0));
        Assert.False(VoiceWirePolicy.IsNewerSequence(0, uint.MaxValue));
    }

    [Fact]
    public void FrameBudgetAcceptsFiftiethFrameAndRejectsTheNext()
    {
        var limiter = new VoiceByteLimiter();

        Assert.True(limiter.TryConsume(7, 50, 1, 0));
        Assert.False(limiter.TryConsume(7, 1, 1, 0));
    }

    [Fact]
    public void FrameBudgetRejectsOverflowSizedRequest()
    {
        var limiter = new VoiceByteLimiter();

        Assert.True(limiter.TryConsume(7, 1, 1, 0));
        Assert.False(limiter.TryConsume(7, int.MaxValue, 0, 0));
    }

    [Fact]
    public void ByteBudgetAcceptsThirtyKiBAndRejectsOneMoreByte()
    {
        var limiter = new VoiceByteLimiter();

        Assert.True(limiter.TryConsume(7, 1, 30 * 1024, 0));
        Assert.False(limiter.TryConsume(7, 1, 1, 0));
    }

    [Fact]
    public void WindowResetsExactlyAtOneSecond()
    {
        var limiter = new VoiceByteLimiter();

        Assert.True(limiter.TryConsume(7, 50, 30 * 1024, 0));
        Assert.False(limiter.TryConsume(7, 1, 1, 999));
        Assert.True(limiter.TryConsume(7, 1, 1, 1_000));
    }

    [Fact]
    public void ForgetClearsLimiterBudgetForPeer()
    {
        var limiter = new VoiceByteLimiter();

        Assert.True(limiter.TryConsume(7, 50, 30 * 1024, 0));
        Assert.False(limiter.TryConsume(7, 1, 1, 0));

        limiter.Forget(7);

        Assert.True(limiter.TryConsume(7, 1, 1, 0));
    }
}
