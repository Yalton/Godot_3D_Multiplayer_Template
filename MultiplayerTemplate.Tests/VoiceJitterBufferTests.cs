using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceJitterBufferTests
{
    [Fact]
    public void PlaybackWaitsForThreeFrames()
    {
        var queue = new VoiceJitterBuffer();
        queue.Enqueue(Packet(10));
        queue.Enqueue(Packet(11));

        Assert.False(queue.TryTake(out _, out var packetLoss));
        Assert.False(packetLoss);

        queue.Enqueue(Packet(12));

        Assert.True(queue.TryTake(out var payload, out packetLoss));
        Assert.False(packetLoss);
        Assert.Equal([10], payload);
    }

    [Fact]
    public void GapRequestsPlcOnceBeforeNextPacket()
    {
        var queue = new VoiceJitterBuffer();
        queue.Enqueue(Packet(10));
        queue.Enqueue(Packet(12));
        queue.Enqueue(Packet(13));

        Assert.True(queue.TryTake(out var payload, out var packetLoss));
        Assert.False(packetLoss);
        Assert.Equal([10], payload);

        Assert.True(queue.TryTake(out payload, out packetLoss));
        Assert.True(packetLoss);
        Assert.Null(payload);

        Assert.True(queue.TryTake(out payload, out packetLoss));
        Assert.False(packetLoss);
        Assert.Equal([12], payload);
    }

    [Fact]
    public void QueueKeepsAtMostTenFrames()
    {
        var queue = new VoiceJitterBuffer();
        for (uint i = 0; i < 11; i++)
            queue.Enqueue(Packet(i));

        Assert.Equal(10, queue.Count);
    }

    [Fact]
    public void ResetDiscardsBufferedFramesAndPlaybackState()
    {
        var queue = new VoiceJitterBuffer();
        queue.Enqueue(Packet(10));
        queue.Enqueue(Packet(11));
        queue.Enqueue(Packet(12));
        Assert.True(queue.TryTake(out _, out _));

        queue.Reset();

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryTake(out _, out var packetLoss));
        Assert.False(packetLoss);
    }

    private static VoicePacket Packet(uint sequence) =>
        new(VoiceWirePolicy.FormatVersion, VoiceChannel.Proximity, sequence, [(byte)sequence]);
}
