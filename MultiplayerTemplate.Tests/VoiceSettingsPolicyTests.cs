using MultiplayerTemplate.Communication;
using SettingsKit;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceSettingsPolicyTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.4f, 0.4f)]
    [InlineData(2f, 1f)]
    public void VadThresholdIsClamped(float input, float expected)
    {
        Assert.Equal(expected, VoiceSettingsPolicy.ClampVadThreshold(input), 3);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(3f, 2f)]
    public void GainIsClamped(float input, float expected)
    {
        Assert.Equal(expected, VoiceSettingsPolicy.ClampGain(input), 3);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(50f, 50f)]
    [InlineData(200f, 100f)]
    public void OverallVolumeIsClamped(float input, float expected)
    {
        Assert.Equal(expected, VoiceSettingsPolicy.ClampVolume(input), 3);
    }

    [Fact]
    public void DefaultsArePttAndProximity()
    {
        Assert.Equal((int)VoiceTransmissionMode.PushToTalk, (int)VoiceSettingsPolicy.DefaultTransmissionMode);
        Assert.Equal((int)VoiceChannel.Proximity, (int)VoiceSettingsPolicy.DefaultChannel);
    }

    [Fact]
    public void NonFinitePreferencesFallBackToDefaults()
    {
        Assert.Equal(VoiceSettingsPolicy.DefaultGain, VoiceSettingsPolicy.ClampGain(float.NaN));
        Assert.Equal(VoiceSettingsPolicy.DefaultVadThreshold, VoiceSettingsPolicy.ClampVadThreshold(float.PositiveInfinity));
        Assert.Equal(VoiceSettingsPolicy.DefaultVolume, VoiceSettingsPolicy.ClampVolume(float.NegativeInfinity));
    }
}
