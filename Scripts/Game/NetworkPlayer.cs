using Godot;
using SettingsKit;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Interaction;
using MultiplayerTemplate.UI;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Game;

public partial class NetworkPlayer : CharacterBody3D
{
    [Export] public float Speed { get; set; } = 6f;
    [Export] public float JumpVelocity { get; set; } = 5.5f;
    private const float SnapshotRate = 20f;
    private const float ReconcileThreshold = 0.12f;
    private const float HardSnapThreshold = 2.5f;
    private const float PitchLimit = 1.45f;

    public long PeerId { get; private set; } = 1;
    private float _yaw;
    private float _pitch;
    private double _snapshotAccumulator;
    private Vector3 _targetPosition;
    private Vector3 _targetVelocity;
    private float _targetYaw;
    private float _targetPitch;
    private Vector2 _serverMoveInput;
    private bool _serverJump;
    private float _serverYaw;
    private float _serverPitch;
    private uint _nextInputSequence;
    private uint _lastReceivedSequence;
    private uint _unackedJumpSequence;
    private (Vector3 Position, Vector3 Velocity, float Yaw, float Error)? _pendingCorrection;
    private Camera3D _camera = null!;
    private Node3D _head = null!;
    private Label3D _nameLabel = null!;
    private Label _interactionPrompt = null!;
    private readonly List<PredictedInput> _pendingInputs = new();

    private readonly record struct PredictedInput(
        uint Sequence, Vector2 Move, bool Jump, float Yaw, float Delta);

    private bool IsLocalPlayer =>
        Multiplayer.HasMultiplayerPeer() && Multiplayer.GetUniqueId() == PeerId;

    public void Setup(long id, Node3D head, Camera3D camera, Label3D nameLabel)
    {
        PeerId = id;
        Name = id.ToString();
        _head = head;
        _camera = camera;
        _nameLabel = nameLabel;
        // The server owns every player node. PeerId identifies who may submit input.
        SetMultiplayerAuthority(1);
    }

    public Vector3 HoldPosition => _head.GlobalPosition - _head.GlobalBasis.Z * 2.2f;
    public Vector3 EyePosition => _camera.GlobalPosition;

    public override void _Ready()
    {
        AddToGroup("network_players");
        _nameLabel.Text = Network.SessionManager.Instance.DisplayName(PeerId);
        _camera.Current = IsLocalPlayer;
        if (IsLocalPlayer) LocalInputContext.Instance.RefreshMouseMode();
        _targetPosition = GlobalPosition;
        if (IsLocalPlayer) BuildInteractionHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsLocalPlayer && @event.IsActionPressed("interact") &&
            !LocalInputContext.Instance.GameplaySuppressed)
        {
            InteractionManager.Instance.Request(InteractionTarget());
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsLocalPlayer || LocalInputContext.Instance.GameplaySuppressed ||
            Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is not InputEventMouseMotion motion) return;
        var sensitivity = SettingsManager.Instance.GetMouseSensitivity() / 15000f;
        _yaw -= motion.Relative.X * sensitivity;
        _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -PitchLimit, PitchLimit);
        _head.Rotation = new Vector3(_pitch, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsLocalPlayer) UpdateInteractionPrompt();
        var step = (float)delta;
        if (Multiplayer.IsServer())
        {
            if (PeerId == Multiplayer.GetUniqueId())
            {
                var local = ReadLocalInput();
                _serverMoveInput = local.Move;
                _serverJump = local.Jump;
                _serverYaw = _yaw;
                _serverPitch = _pitch;
                _lastReceivedSequence = ++_nextInputSequence;
            }
            ApplyMovement(_serverMoveInput, _serverJump, _serverYaw, step);
            // Server-side head pitch: HoldPosition and remote aim read it from here.
            _head.Rotation = new Vector3(_serverPitch, 0, 0);
            _serverJump = false;
            SendSnapshots(delta);
        }
        else if (IsLocalPlayer)
        {
            ApplyPendingCorrection();
            var local = ReadLocalInput();
            var sequence = ++_nextInputSequence;
            _pendingInputs.Add(new PredictedInput(sequence, local.Move, local.Jump, _yaw, step));
            if (_pendingInputs.Count > 120) _pendingInputs.RemoveAt(0);
            ApplyMovement(local.Move, local.Jump, _yaw, step);
            // A jump is a one-frame edge on an unreliable channel: keep resending the
            // flag until the server acknowledges a later input, so one lost packet
            // cannot eat the jump and force a hard reconcile snap. Redundant trues are
            // harmless server-side — the body is airborne after the first one lands.
            if (local.Jump) _unackedJumpSequence = sequence;
            RpcId(1, nameof(SubmitInput),
                local.Move, local.Jump || _unackedJumpSequence != 0, _yaw, _pitch, sequence);
        }
        else
        {
            var weight = Mathf.Min(step * 14f, 1f);
            GlobalPosition = GlobalPosition.Lerp(_targetPosition, weight);
            Rotation = new Vector3(
                Rotation.X,
                Mathf.LerpAngle(Rotation.Y, _targetYaw, weight),
                Rotation.Z);
            _head.Rotation = new Vector3(
                Mathf.LerpAngle(_head.Rotation.X, _targetPitch, weight), 0, 0);
            Velocity = _targetVelocity;
        }
    }

    private (Vector2 Move, bool Jump) ReadLocalInput() =>
        LocalInputContext.Instance.GameplaySuppressed
            ? (Vector2.Zero, false)
            : (Input.GetVector("move_left", "move_right", "move_forward", "move_back"),
                Input.IsActionJustPressed("jump"));

    private NodePath InteractionTarget()
    {
        var from = _camera.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from - _camera.GlobalBasis.Z * InteractionManager.Reach);
        query.Exclude = [GetRid()];
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.TryGetValue("collider", out var collider) && collider.AsGodotObject() is Node3D node &&
            node.IsInGroup("interactable"))
            return node.GetPath();
        return new NodePath();
    }

    private void BuildInteractionHud()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        _interactionPrompt = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _interactionPrompt.SetAnchorsPreset(Control.LayoutPreset.Center);
        _interactionPrompt.Position = new Vector2(-160, 70);
        _interactionPrompt.Size = new Vector2(320, 40);
        layer.AddChild(_interactionPrompt);
        var crosshair = new Label { Text = "+", MouseFilter = Control.MouseFilterEnum.Ignore };
        crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        crosshair.Position = new Vector2(-5, -12);
        layer.AddChild(crosshair);
    }

    private void UpdateInteractionPrompt()
    {
        if (_interactionPrompt == null) return;
        var path = InteractionTarget();
        if (path.IsEmpty)
        {
            _interactionPrompt.Text = IsHoldingObject() ? "[E] Drop held object" : "";
            return;
        }
        var target = GetNodeOrNull<Node>(path);
        var prompt = target switch
        {
            NetworkPhysicsObject physics => physics.HeldByPeer == PeerId ? "Drop" : physics.Prompt,
            NetworkInteractable interactable => interactable.Prompt,
            _ => "Interact"
        };
        _interactionPrompt.Text = $"[E] {prompt}";
    }

    private bool IsHoldingObject()
    {
        foreach (var node in GetTree().GetNodesInGroup("network_physics"))
            if (node is NetworkPhysicsObject physics && physics.HeldByPeer == PeerId) return true;
        return false;
    }

    private void ApplyMovement(Vector2 input, bool jump, float yaw, float delta)
    {
        Rotation = new Vector3(Rotation.X, yaw, Rotation.Z);
        var velocity = Velocity;
        if (!IsOnFloor()) velocity += GetGravity() * delta;
        if (jump && IsOnFloor()) velocity.Y = JumpVelocity;
        var direction = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0, Speed * 8f * delta);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed * 8f * delta);
        }
        Velocity = velocity;
        MoveAndSlide();
    }

    private void SendSnapshots(double delta)
    {
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator < 1.0 / SnapshotRate) return;
        _snapshotAccumulator = 0;
        Rpc(nameof(ReceiveSnapshot), GlobalPosition, Velocity, Rotation.Y, _serverPitch,
            _lastReceivedSequence);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void SubmitInput(Vector2 move, bool jump, float yaw, float pitch, uint sequence)
    {
        if (!RpcSecurity.RequireServer(this, nameof(SubmitInput))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!RpcSecurity.RequireSender(sender, PeerId, nameof(SubmitInput)) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(SubmitInput),
                SessionManager.SessionState.Playing)) return;
        if (!move.IsFinite() || !float.IsFinite(yaw) || !float.IsFinite(pitch))
        {
            RpcSecurity.Reject(nameof(SubmitInput), sender, "non-finite input values");
            return;
        }
        if (sequence <= _lastReceivedSequence) return;
        _lastReceivedSequence = sequence;
        _serverMoveInput = move.LimitLength(1f);
        _serverJump |= jump;
        _serverYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        _serverPitch = Mathf.Clamp(pitch, -PitchLimit, PitchLimit);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveSnapshot(
        Vector3 position, Vector3 velocity, float yaw, float pitch, uint acknowledgedInput)
    {
        if (IsLocalPlayer)
        {
            _pendingInputs.RemoveAll(input => input.Sequence <= acknowledgedInput);
            if (_unackedJumpSequence != 0 && acknowledgedInput >= _unackedJumpSequence)
                _unackedJumpSequence = 0;
            var error = GlobalPosition.DistanceTo(position);
            if (error > ReconcileThreshold) _pendingCorrection = (position, velocity, yaw, error);
            return;
        }
        _targetPosition = position;
        _targetVelocity = velocity;
        _targetYaw = yaw;
        _targetPitch = pitch;
    }

    // Rewind to the acknowledged server result, then replay inputs the server has not
    // processed yet. Large errors snap; small ones blend after replay. Must run inside a
    // physics frame: MoveAndSlide derives its step from the current frame type, so
    // replaying during RPC delivery (an idle frame) would advance each replayed input by
    // the render delta instead of the recorded physics delta.
    private void ApplyPendingCorrection()
    {
        if (_pendingCorrection is not { } correction) return;
        _pendingCorrection = null;
        var predictedBeforeRewind = GlobalPosition;
        GlobalPosition = correction.Position;
        Velocity = correction.Velocity;
        Rotation = new Vector3(Rotation.X, correction.Yaw, Rotation.Z);
        foreach (var input in _pendingInputs)
            ApplyMovement(input.Move, input.Jump, input.Yaw, input.Delta);
        if (correction.Error < HardSnapThreshold)
            GlobalPosition = predictedBeforeRewind.Lerp(GlobalPosition, 0.35f);
    }
}
