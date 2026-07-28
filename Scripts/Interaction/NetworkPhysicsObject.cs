using Godot;
using MultiplayerTemplate.Game;

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
        if (!Multiplayer.IsServer())
        {
            var weight = Mathf.Min((float)delta * 15f, 1f);
            GlobalPosition = GlobalPosition.Lerp(_targetPosition, weight);
            GlobalBasis = new Basis(GlobalBasis.GetRotationQuaternion().Slerp(_targetRotation, weight));
            return;
        }

        if (HeldByPeer != 0 && FindPlayer(HeldByPeer) is { } holder)
        {
            var head = holder.GetNode<Node3D>("Head");
            var desired = head.GlobalPosition - head.GlobalBasis.Z * 2.2f;
            LinearVelocity = (desired - GlobalPosition) * 12f;
            AngularVelocity *= 0.8f;
        }
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator >= 1.0 / 20.0)
        {
            _snapshotAccumulator = 0;
            Rpc(nameof(ReceiveSnapshot), GlobalPosition, GlobalBasis.GetRotationQuaternion(), LinearVelocity);
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

    private void SetHeld(long peerId)
    {
        HeldByPeer = peerId;
        GravityScale = peerId == 0 ? 1f : 0f;
        Rpc(nameof(ReceiveHeld), peerId);
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
