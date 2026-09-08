namespace MultiplayerTemplate.Communication;

public enum VoiceChannel
{
    Proximity = 0,
    Global = 1,
}

public readonly record struct VoicePoint(double X, double Y, double Z);

public readonly record struct VoicePacket(
    int FormatVersion,
    VoiceChannel Channel,
    uint Sequence,
    byte[] Payload);

public static class VoiceWirePolicy
{
    public const int FormatVersion = 1;
    public const int SampleRate = 16000;
    public const int FrameSamples = 320;
    public const int MaxPayloadBytes = 512;

    public static bool IsValid(VoicePacket packet) =>
        packet.FormatVersion == FormatVersion &&
        Enum.IsDefined(packet.Channel) &&
        packet.Payload is { Length: > 0 and <= MaxPayloadBytes };

    public static bool IsNewerSequence(uint previous, uint candidate)
    {
        var diff = candidate - previous;
        return diff != 0 && diff < 0x8000_0000;
    }
}

public sealed class VoiceByteLimiter
{
    private const long WindowMilliseconds = 1_000;
    private const int MaxFrames = 50;
    private const int MaxBytes = 30 * 1024;

    private sealed class Budget
    {
        public long WindowStartMilliseconds;
        public int Frames;
        public int Bytes;
    }

    private readonly Dictionary<long, Budget> _budgets = new();

    public bool TryConsume(long peerId, int frames, int bytes, long nowMilliseconds)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));

        var budget = GetBudget(peerId, nowMilliseconds);
        var nextFrames = (long)budget.Frames + frames;
        var nextBytes = (long)budget.Bytes + bytes;
        if (nextFrames > MaxFrames || nextBytes > MaxBytes)
            return false;

        budget.Frames = (int)nextFrames;
        budget.Bytes = (int)nextBytes;
        return true;
    }

    public void Forget(long peerId) => _budgets.Remove(peerId);

    private Budget GetBudget(long peerId, long nowMilliseconds)
    {
        if (!_budgets.TryGetValue(peerId, out var budget))
        {
            budget = new Budget { WindowStartMilliseconds = nowMilliseconds };
            _budgets[peerId] = budget;
            return budget;
        }

        if (nowMilliseconds - budget.WindowStartMilliseconds >= WindowMilliseconds)
        {
            budget.WindowStartMilliseconds = nowMilliseconds;
            budget.Frames = 0;
            budget.Bytes = 0;
        }

        return budget;
    }
}
