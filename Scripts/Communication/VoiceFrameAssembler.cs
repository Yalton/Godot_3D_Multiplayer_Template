namespace MultiplayerTemplate.Communication;

public sealed class VoiceFrameAssembler
{
    private readonly List<short> _buffer = new();

    public IReadOnlyList<short[]> Push(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0)
            return [];

        var output = new List<short[]>();
        for (var i = 0; i < samples.Length; i++)
        {
            _buffer.Add(samples[i]);
            if (_buffer.Count == VoiceWirePolicy.FrameSamples)
            {
                output.Add(_buffer.ToArray());
                _buffer.Clear();
            }
        }

        return output;
    }

    public void Reset() => _buffer.Clear();
}
