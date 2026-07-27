using Godot;
using SettingsKit;

namespace MultiplayerTemplate.Game;

public partial class NetworkPlayer : CharacterBody3D
{
    private const float Speed = 6f;
    private const float JumpVelocity = 5.5f;
    private const float StateRate = 20f;

    public long PeerId { get; private set; } = 1;
    private float _yaw;
    private float _pitch;
    private double _stateAccumulator;
    private Vector3 _targetPosition;
    private float _targetYaw;
    private Camera3D _camera = null!;

    public void Setup(long id)
    {
        PeerId = id;
        Name = id.ToString();
        SetMultiplayerAuthority((int)id);
    }

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Head/Camera3D");
        GetNode<Label3D>("NameLabel").Text = Network.SessionManager.Instance.DisplayName(PeerId);
        _camera.Current = IsMultiplayerAuthority();
        if (IsMultiplayerAuthority()) Input.MouseMode = Input.MouseModeEnum.Captured;
        _targetPosition = GlobalPosition;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority() || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is not InputEventMouseMotion motion) return;
        var sensitivity = SettingsManager.Instance.GetMouseSensitivity() / 15000f;
        _yaw -= motion.Relative.X * sensitivity;
        _pitch = Mathf.Clamp(
            _pitch - motion.Relative.Y * sensitivity, -1.45f, 1.45f);
        Rotation = new Vector3(Rotation.X, _yaw, Rotation.Z);
        GetNode<Node3D>("Head").Rotation = new Vector3(_pitch, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsMultiplayerAuthority())
        {
            MoveLocal((float)delta);
            _stateAccumulator += delta;
            if (_stateAccumulator >= 1.0 / StateRate)
            {
                _stateAccumulator = 0;
                Rpc(nameof(SubmitState), GlobalPosition, _yaw, Velocity);
            }
        }
        else
        {
            GlobalPosition = GlobalPosition.Lerp(_targetPosition, Mathf.Min((float)delta * 14f, 1f));
            Rotation = new Vector3(
                Rotation.X,
                Mathf.LerpAngle(Rotation.Y, _targetYaw, Mathf.Min((float)delta * 14f, 1f)),
                Rotation.Z);
        }
    }

    private void MoveLocal(float delta)
    {
        if (!IsOnFloor()) Velocity += GetGravity() * delta;
        var velocity = Velocity;
        if (Input.IsActionJustPressed("jump") && IsOnFloor()) velocity.Y = JumpVelocity;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void SubmitState(Vector3 position, float yaw, Vector3 reportedVelocity)
    {
        if (Multiplayer.GetRemoteSenderId() != PeerId) return;
        // This is a readable starter. Competitive games should move simulation to the server.
        if (position.DistanceTo(_targetPosition) > 8f) return;
        _targetPosition = position;
        _targetYaw = yaw;
        Velocity = reportedVelocity.LimitLength(20f);
    }
}
