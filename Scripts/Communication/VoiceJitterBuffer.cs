namespace MultiplayerTemplate.Communication;

public sealed class VoiceJitterBuffer
{
    private const int TargetFrames = 3;
    private const int MaxFrames = 10;

    private readonly Dictionary<uint, byte[]> _payloads = new();
    private bool _started;
    private bool _hasNextSequence;
    private uint _nextSequence;

    public int Count => _payloads.Count;

    public void Enqueue(VoicePacket packet)
    {
        if (!VoiceWirePolicy.IsValid(packet) || _payloads.Count >= MaxFrames || _payloads.ContainsKey(packet.Sequence))
            return;

        if (_started && packet.Sequence != _nextSequence && !VoiceWirePolicy.IsNewerSequence(_nextSequence, packet.Sequence))
            return;

        if (!_hasNextSequence)
        {
            _nextSequence = packet.Sequence;
            _hasNextSequence = true;
        }

        _payloads.Add(packet.Sequence, packet.Payload);
    }

    public bool TryTake(out byte[]? payload, out bool packetLoss)
    {
        payload = null;
        packetLoss = false;

        if (!_started)
        {
            if (_payloads.Count < TargetFrames)
                return false;

            _nextSequence = OldestBufferedSequence();
            _started = true;
        }

        if (_payloads.Remove(_nextSequence, out payload))
        {
            _nextSequence = unchecked(_nextSequence + 1);
            return true;
        }

        if (_payloads.Count == 0)
            return false;

        _nextSequence = unchecked(_nextSequence + 1);
        packetLoss = true;
        return true;
    }

    public void Reset()
    {
        _payloads.Clear();
        _started = false;
        _hasNextSequence = false;
        _nextSequence = 0;
    }

    private uint OldestBufferedSequence()
    {
        var oldest = _nextSequence;
        foreach (var sequence in _payloads.Keys)
            if (VoiceWirePolicy.IsNewerSequence(sequence, oldest))
                oldest = sequence;

        return oldest;
    }
}
