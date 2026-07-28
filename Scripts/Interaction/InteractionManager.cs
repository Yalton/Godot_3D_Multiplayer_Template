using Godot;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Interaction;

/// <summary>Stable server-authoritative RPC endpoint for world interactions.</summary>
public partial class InteractionManager : Node
{
    public static InteractionManager Instance { get; private set; } = null!;
    public const float Reach = 4.5f;
    private readonly RpcRateLimiter _rateLimiter = new();

    public override void _Ready() => Instance = this;

    public void Request(NodePath target)
    {
        if (Multiplayer.IsServer()) HandleRequest(Multiplayer.GetUniqueId(), target);
        else RpcId(1, nameof(RequestInteraction), target);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestInteraction(NodePath target)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestInteraction)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(RequestInteraction), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(RequestInteraction),
                SessionManager.SessionState.Playing)) return;
        if (!_rateLimiter.Allow(sender, nameof(RequestInteraction), 12, 1000))
        {
            RpcSecurity.Reject(nameof(RequestInteraction), sender, "rate limit exceeded");
            return;
        }
        HandleRequest(sender, target);
    }

    private void HandleRequest(long peerId, NodePath targetPath)
    {
        var player = FindPlayer(peerId);
        if (player == null) return;
        if (targetPath.IsEmpty)
        {
            foreach (var node in GetTree().GetNodesInGroup("network_physics"))
                if (node is NetworkPhysicsObject prop && prop.HeldByPeer == peerId) prop.ServerDrop();
            return;
        }
        var target = GetNodeOrNull<Node3D>(targetPath);
        if (target == null || !target.IsInGroup("interactable") ||
            !RpcSecurity.WithinDistance(player.EyePosition, target.GlobalPosition, Reach,
                nameof(RequestInteraction), peerId) ||
            !RpcSecurity.HasLineOfSight(player, player.EyePosition, target.GlobalPosition,
                [player.GetRid()], target, nameof(RequestInteraction), peerId)) return;
        if (target is NetworkPhysicsObject physics) physics.ServerInteract(peerId);
        else if (target is NetworkInteractable interactable) interactable.ServerInteract(peerId);
    }

    private NetworkPlayer? FindPlayer(long peerId)
    {
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == peerId) return player;
        return null;
    }
}
