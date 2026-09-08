namespace MultiplayerTemplate.Communication;

public sealed class VoiceActivityDetector
{
    private readonly float _threshold;
    private readonly int _hangoverFrames;
    private int _remainingHangoverFrames;

    public VoiceActivityDetector(float threshold, int hangoverFrames)
    {
        _threshold = Math.Clamp(threshold, 0f, 1f);
        _hangoverFrames = Math.Max(0, hangoverFrames);
    }

    public bool IsActive(ReadOnlySpan<short> frame)
    {
        var rms = ComputeRms(frame);
        if (rms >= _threshold)
        {
            _remainingHangoverFrames = _hangoverFrames;
            return true;
        }

        if (_remainingHangoverFrames > 0)
        {
            _remainingHangoverFrames--;
            return true;
        }

        return false;
    }

    private static float ComputeRms(ReadOnlySpan<short> frame)
    {
        if (frame.Length == 0)
            return 0f;

        double sumSquares = 0;
        foreach (var sample in frame)
        {
            var normalized = sample / (double)short.MaxValue;
            sumSquares += normalized * normalized;
        }

        return (float)Math.Sqrt(sumSquares / frame.Length);
    }
}
