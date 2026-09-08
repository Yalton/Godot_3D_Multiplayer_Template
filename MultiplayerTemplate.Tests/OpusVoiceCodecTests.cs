using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class OpusVoiceCodecTests
{
    [Fact]
    public void OpusRoundTripReturnsOne320SampleFrame()
    {
        var codec = new OpusVoiceCodec();

        var encoded = codec.Encode(SineFrame());

        Assert.NotEmpty(encoded);
        Assert.InRange(encoded.Length, 1, VoiceWirePolicy.MaxPayloadBytes);
        Assert.Equal(VoiceWirePolicy.FrameSamples, codec.Decode(encoded, false).Length);
    }

    [Fact]
    public void EncodeRejectsFramesThatDoNotMatchTheWireSize()
    {
        var codec = new OpusVoiceCodec();

        Assert.Empty(codec.Encode(new short[VoiceWirePolicy.FrameSamples - 1]));
    }

    [Fact]
    public void DecodeRejectsPayloadsAboveTheWireLimit()
    {
        var codec = new OpusVoiceCodec();

        Assert.Empty(codec.Decode(new byte[VoiceWirePolicy.MaxPayloadBytes + 1], false));
    }

    [Fact]
    public void DecodeTreatsAnEmptyNonLossPayloadAsAFailedFrame()
    {
        var codec = new OpusVoiceCodec();

        Assert.Empty(codec.Decode([], false));
    }

    [Fact]
    public void DecodeTreatsACorruptPayloadAsAFailedFrame()
    {
        var codec = new OpusVoiceCodec();

        Assert.Empty(codec.Decode([0xff, 0xff], false));
    }

    [Fact]
    public void DecodeUsesPacketLossConcealmentForAMissingPacket()
    {
        var codec = new OpusVoiceCodec();

        Assert.Equal(VoiceWirePolicy.FrameSamples, codec.Decode(null, true).Length);
    }

    private static short[] SineFrame()
    {
        var frame = new short[VoiceWirePolicy.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
            frame[i] = (short)(short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / VoiceWirePolicy.SampleRate));

        return frame;
    }
}
