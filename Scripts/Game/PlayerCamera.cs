using Godot;
using SettingsKit;
using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Game;

public enum CameraMode
{
    FirstPerson,
    ThirdPerson
}

/// <summary>
/// The head pivot and its camera. Deliberately a plain Node3D and not a
/// BoneAttachment3D: parenting the camera to an animated bone makes the
/// first-person view shake.
/// </summary>
public partial class PlayerCamera : Node3D
{
    private const float ThirdPersonDistance = 3.5f;

    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private AvatarRig? _rig;
    private bool _isLocal;
    private readonly System.Collections.Generic.HashSet<string> _temporaryOwners = new();

    public CameraMode Mode { get; private set; } = CameraMode.FirstPerson;

    /// <summary>Interaction and aim always originate at the head, in both modes.</summary>
    public Vector3 AimOrigin => GlobalPosition;

    public Vector3 AimForward => -GlobalBasis.Z;

    public Vector3 EyePosition => GlobalPosition;

    public override void _Ready()
    {
        Name = "Head";
        _arm = new SpringArm3D { Name = "SpringArm3D", SpringLength = 0f, Margin = 0.2f };
        AddChild(_arm);
        _camera = new Camera3D { Name = "Camera3D", Fov = SettingsManager.Instance.GetFov() };
        _arm.AddChild(_camera);
    }

    public void Initialize(bool isLocal, AvatarRig? rig)
    {
        _isLocal = isLocal;
        _rig = rig;
        if (DisplayServer.GetName() == "headless") return;
        _camera.Current = isLocal;
        if (!isLocal) return;
        Mode = (CameraMode)Mathf.Clamp(SettingsManager.Instance.GetCameraMode(), 0, 1);
        ApplyMode();
    }

    public void ApplyPitch(float pitch) => Rotation = new Vector3(pitch, 0, 0);

    public void ToggleMode()
    {
        if (!_isLocal) return;
        Mode = Mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
        SettingsManager.Instance.SetCameraMode((int)Mode);
        ApplyMode();
    }

    public void PushTemporaryThirdPerson(string owner)
    {
        if (!_isLocal || !_temporaryOwners.Add(owner)) return;
        ApplyMode();
    }

    public void PopTemporary(string owner)
    {
        if (!_isLocal || !_temporaryOwners.Remove(owner)) return;
        ApplyMode();
    }

    private void ApplyMode()
    {
        var thirdPerson = Mode == CameraMode.ThirdPerson || _temporaryOwners.Count > 0;
        _arm.SpringLength = thirdPerson ? ThirdPersonDistance : 0f;
        _camera.Fov = SettingsManager.Instance.GetFov();
        // The local body is hidden in first person; remote bodies are always visible.
        if (_isLocal && _rig != null) _rig.Visible = thirdPerson;
    }
}
