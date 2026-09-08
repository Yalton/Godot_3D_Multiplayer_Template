using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceActivityDetectorTests
{
    [Fact]
    public void SilentFrameStaysInactiveBelowThreshold()
    {
        var vad = new VoiceActivityDetector(0.1f, 2);

        Assert.False(vad.IsActive(SilentFrame()));
    }

    [Fact]
    public void VadUsesHangoverAfterLoudFrame()
    {
        var vad = new VoiceActivityDetector(0.1f, 2);

        Assert.True(vad.IsActive(LoudFrame()));
        Assert.True(vad.IsActive(SilentFrame()));
        Assert.True(vad.IsActive(SilentFrame()));
        Assert.False(vad.IsActive(SilentFrame()));
    }

    private static short[] SilentFrame() => new short[320];

    private static short[] LoudFrame()
    {
        var frame = new short[320];
        Array.Fill(frame, (short)(short.MaxValue / 2));
        return frame;
    }
}
