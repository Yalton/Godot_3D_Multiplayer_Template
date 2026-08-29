namespace MultiplayerTemplate.Network;

/// <summary>
/// Authorization rule for host-initiated removal. Pure and engine-free so it can be
/// unit tested; must not reference the Godot namespace.
/// </summary>
public static class KickPolicy
{
    public const long ServerPeerId = 1;

    public static bool CanKick(long senderPeerId, long targetPeerId, bool senderIsHost, bool targetIsKnown) =>
        senderIsHost &&
        targetIsKnown &&
        targetPeerId != senderPeerId &&
        targetPeerId != ServerPeerId;
}
