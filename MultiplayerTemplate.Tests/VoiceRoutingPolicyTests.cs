using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceRoutingPolicyTests
{
    [Fact]
    public void ProximityExcludesSenderAndPeerAt36Metres()
    {
        var peers = new Dictionary<long, VoicePoint>
        {
            [1] = new(0, 0, 0),
            [2] = new(34, 0, 0),
            [3] = new(36, 0, 0),
        };

        Assert.Equal(new long[] { 2 }, VoiceRoutingPolicy.Recipients(VoiceChannel.Proximity, 1, peers));
    }

    [Fact]
    public void GlobalIncludesEveryOtherPeer()
    {
        var peers = new Dictionary<long, VoicePoint>
        {
            [1] = new(0, 0, 0),
            [2] = new(34, 0, 0),
            [3] = new(36, 0, 0),
        };

        Assert.Equal(new long[] { 2, 3 }, VoiceRoutingPolicy.Recipients(VoiceChannel.Global, 1, peers));
    }
}
