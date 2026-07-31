namespace MultiplayerTemplate.Communication;

/// <summary>
/// Linear-interpolation mono resampler that carries fractional read position across
/// buffers, so a continuous capture stream resamples without seams. The previous
/// decimate-by-3 approach silently assumed a 48 kHz mix rate; Godot defaults to
/// 44.1 kHz, which produced ~9% pitch-shifted audio and playback underruns.
/// No anti-aliasing filter is applied — acceptable for speech, not for music.
/// </summary>
public sealed class VoiceResampler
{
    private readonly double _step;
    private double _phase;
    private float _previous;

    public VoiceResampler(double sourceRate, double targetRate)
    {
        if (sourceRate <= 0 || targetRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate),
                "Sample rates must be positive.");
        _step = sourceRate / targetRate;
    }

    /// <summary>Source samples consumed per output sample.</summary>
    public double Step => _step;

    /// <summary>Clears carried state; call when capture starts or restarts.</summary>
    public void Reset()
    {
        _phase = 0;
        _previous = 0;
    }

    /// <summary>
    /// Resamples one mono buffer to 16-bit PCM samples. Output length varies by ±1
    /// sample between calls as the fractional phase carries over.
    /// </summary>
    public short[] Process(float[] mono)
    {
        if (mono.Length == 0) return [];
        var output = new List<short>((int)(mono.Length / _step) + 2);
        // Virtual stream: index -1 is the last sample of the previous buffer.
        while (_phase < mono.Length - 1)
        {
            var index = (int)Math.Floor(_phase);
            var fraction = (float)(_phase - index);
            var from = index < 0 ? _previous : mono[index];
            var to = mono[index + 1];
            var value = Math.Clamp(from + (to - from) * fraction, -1f, 1f);
            output.Add((short)(value * short.MaxValue));
            _phase += _step;
        }
        _phase -= mono.Length;
        _previous = mono[^1];
        return output.ToArray();
    }
}
