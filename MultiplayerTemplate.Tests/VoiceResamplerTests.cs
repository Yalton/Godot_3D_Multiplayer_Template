using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceResamplerTests
{
    [Theory]
    [InlineData(0, 16000)]
    [InlineData(48000, 0)]
    [InlineData(-44100, 16000)]
    public void RejectsNonPositiveRates(double source, double target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceResampler(source, target));

    [Fact]
    public void EmptyBufferProducesNoOutput()
    {
        var resampler = new VoiceResampler(48000, 16000);
        Assert.Empty(resampler.Process([]));
    }

    [Fact]
    public void IntegerRatioProducesExactCounts()
    {
        var resampler = new VoiceResampler(48000, 16000);
        var buffer = new float[960];
        Assert.Equal(320, resampler.Process(buffer).Length);
        Assert.Equal(320, resampler.Process(buffer).Length);
    }

    [Fact]
    public void FractionalRatioConvergesToExpectedRate()
    {
        // Godot's default mix rate: 44100 -> 16000 is a non-integer ratio, the exact
        // case the old decimate-by-3 implementation got wrong.
        var resampler = new VoiceResampler(44100, 16000);
        var total = 0;
        for (var i = 0; i < 100; i++) total += resampler.Process(new float[960]).Length;
        var expected = 100 * 960 * 16000.0 / 44100.0;
        Assert.InRange(total, (int)expected - 1, (int)expected + 1);
    }

    [Fact]
    public void FractionalRateBuffersStillEmitExactVoiceFrames()
    {
        var source = new VoiceResampler(44100, 16000);
        var frames = new VoiceFrameAssembler();
        var output = frames.Push(source.Process(new float[960]));
        Assert.Single(output);
        Assert.All(output, frame =>
            Assert.Equal(VoiceWirePolicy.FrameSamples, frame.Length));
    }

    [Fact]
    public void ConstantSignalStaysConstantAcrossBufferBoundaries()
    {
        var resampler = new VoiceResampler(44100, 16000);
        var buffer = new float[480];
        Array.Fill(buffer, 0.5f);
        var expected = (short)(0.5f * short.MaxValue);
        for (var i = 0; i < 5; i++)
            Assert.All(resampler.Process(buffer), sample => Assert.Equal(expected, sample));
    }

    [Fact]
    public void OutOfRangeInputIsClamped()
    {
        var resampler = new VoiceResampler(48000, 16000);
        var loud = new float[96];
        Array.Fill(loud, 2f);
        Assert.All(resampler.Process(loud), sample => Assert.Equal(short.MaxValue, sample));
    }

    [Fact]
    public void ResetClearsCarriedPhase()
    {
        var resampler = new VoiceResampler(44100, 16000);
        resampler.Process(new float[961]);
        resampler.Reset();
        // After a reset the first buffer behaves like a fresh stream.
        var fresh = new VoiceResampler(44100, 16000);
        Assert.Equal(fresh.Process(new float[960]).Length, resampler.Process(new float[960]).Length);
    }

    [Fact]
    public void UpsamplingInterpolatesBetweenSamples()
    {
        var resampler = new VoiceResampler(8000, 16000);
        var ramp = new float[] { 0f, 0.5f, 1f };
        var output = resampler.Process(ramp);
        // step 0.5: phases 0, 0.5, 1.0, 1.5 -> 0, 0.25, 0.5, 0.75
        Assert.Equal(4, output.Length);
        Assert.Equal(0, output[0]);
        Assert.Equal((short)(0.25f * short.MaxValue), output[1]);
        Assert.Equal((short)(0.5f * short.MaxValue), output[2]);
        Assert.Equal((short)(0.75f * short.MaxValue), output[3]);
    }
}
