using Concentus;
using Concentus.Enums;

namespace MultiplayerTemplate.Communication;

public interface IVoiceCodec
{
    byte[] Encode(short[] frame);

    short[] Decode(byte[]? payload, bool packetLoss);
}

public sealed class OpusVoiceCodec : IVoiceCodec
{
    private readonly IOpusEncoder _encoder;
    private readonly IOpusDecoder _decoder;

    public OpusVoiceCodec()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        _encoder = OpusCodecFactory.CreateEncoder(
            VoiceWirePolicy.SampleRate,
            1,
            OpusApplication.OPUS_APPLICATION_VOIP,
            null);
        _decoder = OpusCodecFactory.CreateDecoder(VoiceWirePolicy.SampleRate, 1, null);
    }

    public byte[] Encode(short[] frame)
    {
        if (frame is null || frame.Length != VoiceWirePolicy.FrameSamples)
            return [];

        try
        {
            var encoded = new byte[1276];
            var encodedLength = _encoder.Encode(frame, VoiceWirePolicy.FrameSamples, encoded, encoded.Length);
            if (encodedLength is <= 0 or > VoiceWirePolicy.MaxPayloadBytes)
                return [];

            return encoded[..encodedLength];
        }
        catch
        {
            return [];
        }
    }

    public short[] Decode(byte[]? payload, bool packetLoss)
    {
        if (!packetLoss && (payload is null || payload.Length is 0 or > VoiceWirePolicy.MaxPayloadBytes))
            return [];

        try
        {
            var decoded = new short[VoiceWirePolicy.FrameSamples];
            var encoded = packetLoss ? ReadOnlySpan<byte>.Empty : payload.AsSpan();
            var decodedSamples = _decoder.Decode(encoded, decoded, VoiceWirePolicy.FrameSamples, false);
            return decodedSamples == VoiceWirePolicy.FrameSamples ? decoded : [];
        }
        catch
        {
            return [];
        }
    }
}
