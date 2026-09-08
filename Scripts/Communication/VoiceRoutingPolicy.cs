namespace MultiplayerTemplate.Communication;

public static class VoiceRoutingPolicy
{
    private const double ProximityRadiusMetres = 35d;
    private const double ProximityRadiusSquared = ProximityRadiusMetres * ProximityRadiusMetres;

    public static IReadOnlyList<long> Recipients(
        VoiceChannel channel,
        long sender,
        IReadOnlyDictionary<long, VoicePoint> peers)
    {
        return channel switch
        {
            VoiceChannel.Proximity => ProximityRecipients(sender, peers),
            VoiceChannel.Global => GlobalRecipients(sender, peers),
            _ => [],
        };
    }

    private static IReadOnlyList<long> GlobalRecipients(long sender, IReadOnlyDictionary<long, VoicePoint> peers)
    {
        var recipients = new List<long>(peers.Count);
        foreach (var peerId in peers.Keys)
        {
            if (peerId != sender)
                recipients.Add(peerId);
        }

        recipients.Sort();
        return recipients;
    }

    private static IReadOnlyList<long> ProximityRecipients(long sender, IReadOnlyDictionary<long, VoicePoint> peers)
    {
        if (!peers.TryGetValue(sender, out var senderPoint))
            return [];

        var recipients = new List<long>();
        foreach (var (peerId, peerPoint) in peers)
        {
            if (peerId == sender)
                continue;

            if (DistanceSquared(senderPoint, peerPoint) <= ProximityRadiusSquared)
                recipients.Add(peerId);
        }

        recipients.Sort();
        return recipients;
    }

    private static double DistanceSquared(VoicePoint left, VoicePoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
