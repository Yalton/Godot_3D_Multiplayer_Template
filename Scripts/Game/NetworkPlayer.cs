using Godot;
using SettingsKit;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Interaction;
using MultiplayerTemplate.UI;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Game;

public partial class NetworkPlayer : CharacterBody3D
{
    private const float Speed = 6f;
    private const float JumpVelocity = 5.5f;
    private const float SnapshotRate = 20f;
    private const float ReconcileThreshold = 0.12f;
    private const float HardSnapThreshold = 2.5f;

    public long PeerId { get; private set; } = 1;
    private float _yaw;
    private float _pitch;
    private double _snapshotAccumulator;
    private Vector3 _targetPosition;
    private Vector3 _targetVelocity;
    private float _targetYaw;
    private Vector2 _serverMoveInput;
    private bool _serverJump;
    private float _serverYaw;
    private uint _nextInputSequence;
    private uint _lastReceivedSequence;
    private AvatarRig? _avatar;
    private float _airborneSeconds;
    private bool _wasGrounded = true;
    private bool _targetGrounded = true;
    private PlayerCamera _view = null!;
    private Label3D _nameLabel = null!;
    private Label _interactionPrompt = null!;
    private readonly List<PredictedInput> _pendingInputs = new();

    private readonly record struct PredictedInput(
        uint Sequence, Vector2 Move, bool Jump, float Yaw, float Delta);

    private bool IsLocalPlayer =>
        Multiplayer.HasMultiplayerPeer() && Multiplayer.GetUniqueId() == PeerId;

    public void Setup(long id, PlayerCamera view, Label3D nameLabel)
    {
        PeerId = id;
        Name = id.ToString();
        _view = view;
        _nameLabel = nameLabel;
        // The server owns every player node. PeerId identifies who may submit input.
        SetMultiplayerAuthority(1);
    }

    public Vector3 HoldPosition => _view.GlobalPosition - _view.GlobalBasis.Z * 2.2f;
    public Vector3 EyePosition => _view.EyePosition;

    public AvatarRig? Avatar => _avatar;

    public void AttachAvatar(AvatarRig? rig)
    {
        if (rig == null) return;
        _avatar = rig;
        AddChild(rig);
    }

    public override void _Ready()
    {
        AddToGroup("network_players");
        _nameLabel.Text = Network.SessionManager.Instance.DisplayName(PeerId);
        _view.Initialize(IsLocalPlayer, _avatar);
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
        if (IsLocalPlayer && @event.IsActionPressed("toggle_camera") &&
            !LocalInputContext.Instance.GameplaySuppressed)
        {
            _view.ToggleMode();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsLocalPlayer || LocalInputContext.Instance.GameplaySuppressed ||
            Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is not InputEventMouseMotion motion) return;
        var sensitivity = SettingsManager.Instance.GetMouseSensitivity() / 15000f;
        _yaw -= motion.Relative.X * sensitivity;
        _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -1.45f, 1.45f);
        _view.ApplyPitch(_pitch);
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
                _lastReceivedSequence = ++_nextInputSequence;
            }
            ApplyMovement(_serverMoveInput, _serverJump, _serverYaw, step);
            _serverJump = false;
            SendSnapshots(delta);
        }
        else if (IsLocalPlayer)
        {
            var local = ReadLocalInput();
            var sequence = ++_nextInputSequence;
            _pendingInputs.Add(new PredictedInput(sequence, local.Move, local.Jump, _yaw, step));
            if (_pendingInputs.Count > 120) _pendingInputs.RemoveAt(0);
            ApplyMovement(local.Move, local.Jump, _yaw, step);
            RpcId(1, nameof(SubmitInput), local.Move, local.Jump, _yaw, sequence);
        }
        else
        {
            GlobalPosition = GlobalPosition.Lerp(_targetPosition, Mathf.Min(step * 14f, 1f));
            Rotation = new Vector3(
                Rotation.X,
                Mathf.LerpAngle(Rotation.Y, _targetYaw, Mathf.Min(step * 14f, 1f)),
                Rotation.Z);
            Velocity = _targetVelocity;
        }
        UpdateAvatarAnimation(step);
    }

    private (Vector2 Move, bool Jump) ReadLocalInput() =>
        LocalInputContext.Instance.GameplaySuppressed
            ? (Vector2.Zero, false)
            : (Input.GetVector("move_left", "move_right", "move_forward", "move_back"),
                Input.IsActionJustPressed("jump"));

    private NodePath InteractionTarget()
    {
        var from = _view.AimOrigin;
        var query = PhysicsRayQueryParameters3D.Create(
            from, from + _view.AimForward * InteractionManager.Reach);
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

    private void UpdateAvatarAnimation(float delta)
    {
        if (_avatar == null) return;
        // Remote peers never run MoveAndSlide, so their IsOnFloor() is meaningless;
        // the server tells them whether the character is grounded.
        var grounded = Multiplayer.IsServer() || IsLocalPlayer ? IsOnFloor() : _targetGrounded;
        var airborne = grounded ? _airborneSeconds : _airborneSeconds + delta;
        var planarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        _avatar.SetLocomotion(
            LocomotionSolver.Solve(planarSpeed, grounded, Velocity.Y, airborne, _wasGrounded));
        _airborneSeconds = grounded ? 0f : airborne;
        _wasGrounded = grounded;
    }

    private void SendSnapshots(double delta)
    {
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator < 1.0 / SnapshotRate) return;
        _snapshotAccumulator = 0;
        Rpc(nameof(ReceiveSnapshot), GlobalPosition, Velocity, Rotation.Y, IsOnFloor(), _lastReceivedSequence);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void SubmitInput(Vector2 move, bool jump, float yaw, uint sequence)
    {
        if (!RpcSecurity.RequireServer(this, nameof(SubmitInput))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!RpcSecurity.RequireSender(sender, PeerId, nameof(SubmitInput)) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(SubmitInput),
                SessionManager.SessionState.Playing)) return;
        if (sequence <= _lastReceivedSequence) return;
        _lastReceivedSequence = sequence;
        _serverMoveInput = move.LimitLength(1f);
        _serverJump |= jump;
        _serverYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveSnapshot(
        Vector3 position, Vector3 velocity, float yaw, bool grounded, uint acknowledgedInput)
    {
        if (IsLocalPlayer)
        {
            _pendingInputs.RemoveAll(input => input.Sequence <= acknowledgedInput);
            var error = GlobalPosition.DistanceTo(position);
            if (error > ReconcileThreshold)
            {
                // Rewind to the acknowledged server result, then replay inputs the server has
                // not processed yet. Large errors snap; small ones blend after replay.
                var predictedBeforeRewind = GlobalPosition;
                GlobalPosition = position;
                Velocity = velocity;
                Rotation = new Vector3(Rotation.X, yaw, Rotation.Z);
                foreach (var input in _pendingInputs)
                    ApplyMovement(input.Move, input.Jump, input.Yaw, input.Delta);
                if (error < HardSnapThreshold)
                    GlobalPosition = predictedBeforeRewind.Lerp(GlobalPosition, 0.35f);
            }
            return;
        }
        _targetPosition = position;
        _targetVelocity = velocity;
        _targetYaw = yaw;
        _targetGrounded = grounded;
    }
}
