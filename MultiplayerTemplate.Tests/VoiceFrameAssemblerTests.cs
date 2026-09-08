using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceFrameAssemblerTests
{
    [Fact]
    public void AssemblerEmitsOnlyCompleteFrames()
    {
        var frames = new VoiceFrameAssembler();

        Assert.Empty(frames.Push(new short[319]));
        Assert.Single(frames.Push(new short[1]));
    }

    [Fact]
    public void ResetDropsBufferedSamples()
    {
        var frames = new VoiceFrameAssembler();

        Assert.Empty(frames.Push(new short[319]));
        frames.Reset();

        Assert.Empty(frames.Push(new short[1]));
    }
}
