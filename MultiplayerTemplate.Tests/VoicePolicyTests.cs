using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceVolumePolicyTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    [InlineData(9f, 2f)]
    public void ClampsToSupportedRange(float input, float expected)
    {
        Assert.Equal(expected, VoiceVolumePolicy.Clamp(input), 3);
    }

    [Fact]
    public void UnityGainIsZeroDecibels()
    {
        Assert.Equal(0f, VoiceVolumePolicy.ToDecibels(1f), 3);
    }

    [Fact]
    public void HalfGainIsAboutMinusSixDecibels()
    {
        Assert.Equal(-6.02f, VoiceVolumePolicy.ToDecibels(0.5f), 2);
    }

    [Fact]
    public void SilenceIsFloored()
    {
        Assert.Equal(-80f, VoiceVolumePolicy.ToDecibels(0f), 3);
    }
}

public sealed class SpeakingTrackerTests
{
    [Fact]
    public void PeerIsSpeakingUntilTheDecayWindowElapses()
    {
        var tracker = new SpeakingTracker();
        tracker.NotePacket(7, 10.0);
        Assert.True(tracker.IsSpeaking(7, 10.0));
        Assert.True(tracker.IsSpeaking(7, 10.25));
        Assert.False(tracker.IsSpeaking(7, 10.26));
    }

    [Fact]
    public void UnknownPeerIsNotSpeaking()
    {
        Assert.False(new SpeakingTracker().IsSpeaking(7, 0.0));
    }

    [Fact]
    public void RemovedPeerIsNotSpeaking()
    {
        var tracker = new SpeakingTracker();
        tracker.NotePacket(7, 10.0);
        tracker.Remove(7);
        Assert.False(tracker.IsSpeaking(7, 10.0));
    }
}
