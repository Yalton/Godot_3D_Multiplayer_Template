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
    [Export] public float Speed { get; set; } = 6f;
    [Export] public float JumpVelocity { get; set; } = 5.5f;
    private const float SnapshotRate = 20f;
    private const float ReconcileThreshold = 0.12f;
    private const float HardSnapThreshold = 2.5f;
    private const float YawIdleEpsilon = 0.01f;
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
    private AvatarRig? _avatar;
    private float _airborneSeconds;
    private bool _wasGrounded = true;
    private bool _targetGrounded = true;
    private PlayerCamera _view = null!;
    private PlayerNameplate _nameLabel = null!;
    private uint _unackedJumpSequence;
    private (Vector3 Position, Vector3 Velocity, float Yaw, float Error)? _pendingCorrection;
    private Label _interactionPrompt = null!;
    private readonly List<PredictedInput> _pendingInputs = new();
    private readonly RpcCooldowns _rejectLog = new();
    private readonly RpcRateLimiter _emoteLimiter = new();
    public int ActiveEmote { get; private set; } = EmoteCatalogue.None;
    private double _lastInputSeconds;
    private bool _afk;
    private bool _afkKickRequested;
    private bool _awaitingEmoteStart;
    private double _emoteStartDeadline;

    private readonly record struct PredictedInput(
        uint Sequence, Vector2 Move, bool Jump, float Yaw, float Delta);

    private bool IsLocalPlayer =>
        Multiplayer.HasMultiplayerPeer() && Multiplayer.GetUniqueId() == PeerId;

    public void Setup(long id, PlayerCamera view, PlayerNameplate nameLabel)
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
        if (rig.GetParent() == null) AddChild(rig);
    }

    public override void _Ready()
    {
        _lastInputSeconds = Time.GetTicksMsec() / 1000.0;
        AddToGroup("network_players");
        if (Multiplayer.IsServer()) SessionManager.Instance.SetAfkServer(PeerId, false);
        _nameLabel.Initialize(PeerId, IsLocalPlayer);
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
        _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sensitivity, -PitchLimit, PitchLimit);
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
                NoteActivity(local.Move, local.Jump, _yaw);
                if (ActiveEmote != EmoteCatalogue.None &&
                    (local.Move.LengthSquared() > 0f || local.Jump)) SetEmoteServer(EmoteCatalogue.None);
                _serverMoveInput = local.Move;
                _serverJump = local.Jump;
                _serverYaw = _yaw;
                _serverPitch = _pitch;
                _lastReceivedSequence = ++_nextInputSequence;
            }
            ApplyMovement(_serverMoveInput, _serverJump, _serverYaw, step);
            // Server-side pitch: HoldPosition and remote aim read it from _view.
            _view.ApplyPitch(_serverPitch);
            _serverJump = false;
            SendSnapshots(delta);
            UpdateAfk();
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
            _pitch = Mathf.LerpAngle(_pitch, _targetPitch, weight);
            _view.ApplyPitch(_pitch);
            Velocity = _targetVelocity;
        }
        if (IsLocalPlayer && _avatar != null)
        {
            // PlayAction only queues a Fire request; ActionPlaying stays false until the
            // AnimationTree processes it. Without this latch the pop below would fire on
            // the same frame as the push and snap the camera back before the clip shows.
            // A rig that does not define the clip never fires it, so ActionPlaying would
            // never go true; the deadline bounds how long the latch can hold the camera.
            if (_awaitingEmoteStart &&
                (_avatar.ActionPlaying || Time.GetTicksMsec() / 1000.0 >= _emoteStartDeadline))
                _awaitingEmoteStart = false;
            else if (!_awaitingEmoteStart && !_avatar.ActionPlaying &&
                     ActiveEmote == EmoteCatalogue.None) _view.PopTemporary("emote");
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

    private void NoteActivity(Vector2 move, bool jump, float yaw)
    {
        if (move.LengthSquared() <= 0f && !jump && Mathf.Abs(Mathf.AngleDifference(yaw, _serverYaw)) < YawIdleEpsilon)
            return;
        _lastInputSeconds = Time.GetTicksMsec() / 1000.0;
    }

    private void UpdateAfk()
    {
        if (!Multiplayer.IsServer()) return;
        // A dedicated server spawns a NetworkPlayer for its own phantom peer-1 entry, and
        // nothing ever feeds it input. A listen-server host is a real player behind peer 1
        // and must still be tracked, so only exempt the headless case.
        if (SessionManager.Instance.IsDedicatedServer && PeerId == 1) return;
        var idleSeconds = Time.GetTicksMsec() / 1000.0 - _lastInputSeconds;
        var afk = AfkPolicy.Evaluate(idleSeconds, SessionManager.Instance.AfkSeconds, _afk);
        if (afk != _afk)
        {
            _afk = afk;
            SessionManager.Instance.SetAfkServer(PeerId, afk);
        }
        if (!_afkKickRequested && PeerId != 1 &&
            AfkPolicy.ShouldKick(idleSeconds, SessionManager.Instance.AfkKickSeconds))
        {
            _afkKickRequested = true;
            SessionManager.Instance.RequestKick(PeerId, "Idle for too long.");
        }
    }

    private void SendSnapshots(double delta)
    {
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator < 1.0 / SnapshotRate) return;
        _snapshotAccumulator = 0;
        Rpc(nameof(ReceiveSnapshot), GlobalPosition, Velocity, Rotation.Y, _serverPitch, IsOnFloor(),
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
            // Always drop the packet, but log at most once a second per peer. Input arrives
            // at the physics rate, so a client sending NaN every tick would otherwise emit
            // ~60 warnings a second — and warnings are no longer suppressible.
            if (_rejectLog.TryConsume(sender, nameof(SubmitInput), 1000))
                RpcSecurity.Reject(nameof(SubmitInput), sender, "non-finite input values");
            return;
        }
        if (sequence <= _lastReceivedSequence) return;
        _lastReceivedSequence = sequence;
        NoteActivity(move, jump, yaw);
        _serverMoveInput = move.LimitLength(1f);
        _serverJump |= jump;
        _serverYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        _serverPitch = Mathf.Clamp(pitch, -PitchLimit, PitchLimit);
        // Movement ends a looping emote. NoteActivity already decides what counts as the
        // player doing something; reuse that rather than inventing a second notion.
        if (ActiveEmote != EmoteCatalogue.None &&
            (move.LengthSquared() > 0f || jump)) SetEmoteServer(EmoteCatalogue.None);
    }

    public void RequestEmote(int id)
    {
        if (!IsLocalPlayer) return;
        if (Multiplayer.IsServer()) SubmitEmoteServer(Multiplayer.GetUniqueId(), id);
        else RpcId(1, nameof(SubmitEmote), id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitEmote(int id)
    {
        if (!RpcSecurity.RequireServer(this, nameof(SubmitEmote))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!RpcSecurity.RequireSender(sender, PeerId, nameof(SubmitEmote)) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(SubmitEmote),
                SessionManager.SessionState.Playing)) return;
        SubmitEmoteServer(sender, id);
    }

    private void SubmitEmoteServer(long sender, int id)
    {
        if (!_emoteLimiter.Allow(sender, nameof(SubmitEmote), 4, 2000))
        {
            // Throttled: a wheel is trivially spammable and warnings are no longer suppressed.
            if (_rejectLog.TryConsume(sender, nameof(SubmitEmote), 1000))
                RpcSecurity.Reject(nameof(SubmitEmote), sender, "rate limit exceeded");
            return;
        }
        if (id != EmoteCatalogue.None && !EmoteCatalogue.TryGet(id, out _))
        {
            if (_rejectLog.TryConsume(sender, nameof(SubmitEmote), 1000))
                RpcSecurity.Reject(nameof(SubmitEmote), sender, $"unknown emote {id}");
            return;
        }
        SetEmoteServer(id);
    }

    private void SetEmoteServer(int id)
    {
        if (!Multiplayer.IsServer()) return;
        // Only looping emotes are state. A one-shot is broadcast and forgotten, so a peer
        // that joins later correctly sees nothing.
        var looping = EmoteCatalogue.TryGet(id, out var definition) && definition.Looping;
        ActiveEmote = looping ? id : EmoteCatalogue.None;
        Rpc(nameof(ReceiveEmote), id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEmote(int id)
    {
        if (!Multiplayer.IsServer())
            ActiveEmote = EmoteCatalogue.TryGet(id, out var d) && d.Looping ? id : EmoteCatalogue.None;
        PlayEmoteLocally(id);
    }

    public void PlayEmoteLocally(int id)
    {
        if (_avatar == null) return;
        if (id == EmoteCatalogue.None)
        {
            _avatar.StopAction();
            if (IsLocalPlayer)
            {
                _view.PopTemporary("emote");
                _awaitingEmoteStart = false;
            }
            return;
        }
        if (!EmoteCatalogue.TryGet(id, out var definition)) return;
        _avatar.PlayAction(definition.Clip);
        if (IsLocalPlayer)
        {
            _view.PushTemporaryThirdPerson("emote");
            _awaitingEmoteStart = true;
            // A rig that does not define this clip never fires, so ActionPlaying would
            // never go true and the latch would strand the camera in third person.
            // Bound the wait.
            _emoteStartDeadline = Time.GetTicksMsec() / 1000.0 + 0.5;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveSnapshot(
        Vector3 position, Vector3 velocity, float yaw, float pitch, bool grounded, uint acknowledgedInput)
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
        _targetGrounded = grounded;
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
