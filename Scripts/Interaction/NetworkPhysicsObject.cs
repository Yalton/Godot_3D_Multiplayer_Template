using Godot;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Interaction;

/// <summary>Host-simulated rigid body with snapshot interpolation and grab/drop interaction.</summary>
public partial class NetworkPhysicsObject : RigidBody3D
{
    [Export] public string Prompt { get; set; } = "Pick up";
    public long HeldByPeer { get; private set; }
    private Vector3 _targetPosition;
    private Quaternion _targetRotation = Quaternion.Identity;
    private Vector3 _targetVelocity;
    private double _snapshotAccumulator;

    public override void _Ready()
    {
        AddToGroup("interactable");
        AddToGroup("network_physics");
        _targetPosition = GlobalPosition;
        _targetRotation = GlobalBasis.GetRotationQuaternion();
        if (!Multiplayer.IsServer())
        {
            Freeze = true;
            FreezeMode = FreezeModeEnum.Kinematic;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (!Multiplayer.IsServer())
        {
            var weight = Mathf.Min((float)delta * 15f, 1f);
            GlobalPosition = GlobalPosition.Lerp(_targetPosition, weight);
            GlobalBasis = new Basis(GlobalBasis.GetRotationQuaternion().Slerp(_targetRotation, weight));
            return;
        }

        if (HeldByPeer != 0 && FindPlayer(HeldByPeer) is { } holder)
        {
            var desired = holder.HoldPosition;
            LinearVelocity = (desired - GlobalPosition) * 12f;
            AngularVelocity *= 0.8f;
        }
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator >= 1.0 / 20.0)
        {
            _snapshotAccumulator = 0;
            // Only peers with the match scene mounted have this node; a peer still loading
            // has no counterpart, and Godot logs "Node not found" if it is targeted anyway.
            foreach (var peerId in MatchSceneRpc.ReadyPeers(this))
                RpcId(peerId, nameof(ReceiveSnapshot), GlobalPosition, GlobalBasis.GetRotationQuaternion(), LinearVelocity);
        }
    }

    public void ServerInteract(long peerId)
    {
        if (!Multiplayer.IsServer()) return;
        if (HeldByPeer == peerId) ServerDrop();
        else if (HeldByPeer == 0)
        {
            foreach (var node in GetTree().GetNodesInGroup("network_physics"))
                if (node is NetworkPhysicsObject other && other.HeldByPeer == peerId) other.ServerDrop();
            SetHeld(peerId);
        }
    }

    public void ServerDrop() => SetHeld(0);

    /// <summary>Server-side: brings a late joiner up to date with the current state.</summary>
    public void SyncStateTo(long peerId)
    {
        if (!Multiplayer.IsServer()) return;
        RpcId(peerId, nameof(ReceiveHeld), HeldByPeer);
        RpcId(peerId, nameof(ReceiveSnapshot), GlobalPosition,
            GlobalBasis.GetRotationQuaternion(), LinearVelocity);
    }

    private void SetHeld(long peerId)
    {
        HeldByPeer = peerId;
        GravityScale = peerId == 0 ? 1f : 0f;
        // Only peers with the match scene mounted have this node; see ReceiveSnapshot above.
        foreach (var readyPeerId in MatchSceneRpc.ReadyPeers(this))
            RpcId(readyPeerId, nameof(ReceiveHeld), peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHeld(long peerId) => HeldByPeer = peerId;

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveSnapshot(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        _targetPosition = position;
        _targetRotation = rotation;
        _targetVelocity = velocity;
    }

    private NetworkPlayer? FindPlayer(long peerId)
    {
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == peerId) return player;
        return null;
    }
}
