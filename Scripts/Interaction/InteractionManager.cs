using Godot;
using MultiplayerTemplate.Game;

namespace MultiplayerTemplate.Interaction;

/// <summary>Stable server-authoritative RPC endpoint for world interactions.</summary>
public partial class InteractionManager : Node
{
    public static InteractionManager Instance { get; private set; } = null!;
    public const float Reach = 4.5f;

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
        if (Multiplayer.IsServer()) HandleRequest(Multiplayer.GetRemoteSenderId(), target);
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
            player.GlobalPosition.DistanceTo(target.GlobalPosition) > Reach) return;
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
