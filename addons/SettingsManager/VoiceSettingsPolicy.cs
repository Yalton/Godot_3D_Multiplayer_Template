using System;

namespace SettingsKit;

/// <summary>
/// Local voice-preference bounds and defaults. Engine-free so saved-value
/// normalization remains testable outside Godot and this addon stays portable.
/// </summary>
public static class VoiceSettingsPolicy
{
    public const string DefaultInputDevice = "Default";
    public const float DefaultGain = 1f;
    public const float DefaultVadThreshold = 0.1f;
    public const float DefaultVolume = 100f;
    public const int DefaultTransmissionMode = 0;
    public const int DefaultChannel = 0;

    public static float ClampGain(float gain) => float.IsFinite(gain) ? Math.Clamp(gain, 0f, 2f) : DefaultGain;
    public static float ClampVadThreshold(float threshold) =>
        float.IsFinite(threshold) ? Math.Clamp(threshold, 0f, 1f) : DefaultVadThreshold;
    public static float ClampVolume(float volume) =>
        float.IsFinite(volume) ? Math.Clamp(volume, 0f, 100f) : DefaultVolume;
    public static int NormalizeTransmissionMode(int mode) => mode is 0 or 1 ? mode : DefaultTransmissionMode;
    public static int NormalizeChannel(int channel) => channel is 0 or 1 ? channel : DefaultChannel;
}
