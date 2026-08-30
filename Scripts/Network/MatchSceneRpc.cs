using Godot;

namespace MultiplayerTemplate.Network;

/// <summary>
/// Helper for match-scene-resident nodes (physics props, interactables) that broadcast RPCs
/// from the server. A peer still loading the match scene has no such node, so sending to it
/// produces "Node not found" errors on the client and dropped-packet warnings on the server.
/// </summary>
public static class MatchSceneRpc
{
    /// <summary>Connected peer ids (relative to `context.Multiplayer`) that have the match scene mounted.</summary>
    public static IEnumerable<long> ReadyPeers(Node context)
    {
        foreach (var peerId in context.Multiplayer.GetPeers())
            if (MatchSceneCoordinator.Instance.IsSceneReady(peerId))
                yield return peerId;
    }
}
