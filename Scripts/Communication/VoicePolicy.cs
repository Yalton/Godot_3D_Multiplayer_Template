namespace MultiplayerTemplate.Communication;

public enum VoiceTransmissionMode
{
    PushToTalk = 0,
    VoiceActivity = 1,
}

/// <summary>
/// Per-peer listening gain rules. Pure and engine-free so it can be unit tested;
/// must not reference the Godot namespace.
/// </summary>
public static class VoiceVolumePolicy
{
    public const float Min = 0f;
    public const float Max = 2f;
    public const float Default = 1f;
    private const float SilenceDecibels = -80f;

    public static float Clamp(float linear) => Math.Clamp(linear, Min, Max);

    public static float ToDecibels(float linear)
    {
        var clamped = Clamp(linear);
        return clamped <= 0.0001f ? SilenceDecibels : 20f * MathF.Log10(clamped);
    }
}

/// <summary>
/// Receive-side observation of who is talking, derived from voice packet arrival.
/// Deliberately local: this flips several times a second and has no business on the
/// replicated roster.
/// </summary>
public sealed class SpeakingTracker
{
    public const double DecaySeconds = 0.25;
    private readonly Dictionary<long, double> _lastPacketSeconds = new();

    public void NotePacket(long peerId, double now) => _lastPacketSeconds[peerId] = now;

    public bool IsSpeaking(long peerId, double now) =>
        _lastPacketSeconds.TryGetValue(peerId, out var last) && now - last <= DecaySeconds;

    public void Remove(long peerId) => _lastPacketSeconds.Remove(peerId);

    public void Clear() => _lastPacketSeconds.Clear();
}
