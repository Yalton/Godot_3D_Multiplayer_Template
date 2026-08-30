using Godot;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Game;

/// <summary>
/// Floating name, speaking icon, and AFK tag. Client-only; a headless server never
/// builds one.
/// </summary>
public partial class PlayerNameplate : Label3D
{
    private const float FullAlphaDistance = 12f;
    private const float FadeEndDistance = 40f;

    private long _peerId;
    private bool _isLocal;
    private bool _textInitialized;
    private string? _lastDisplayName;
    private bool _lastAfk;
    private bool _lastSpeaking;

    public void Initialize(long peerId, bool isLocal)
    {
        _peerId = peerId;
        _isLocal = isLocal;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            Visible = false;
            return;
        }
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = true;
        FontSize = 32;
        Visible = !isLocal;
    }

    public override void _Process(double delta)
    {
        if (_isLocal) return;
        var identity = PlayerIdentityService.Instance.Get(_peerId);
        var speaking = PositionalVoiceChat.Instance != null &&
                       PositionalVoiceChat.Instance.IsSpeaking(_peerId);
        if (!_textInitialized || identity.DisplayName != _lastDisplayName ||
            identity.Afk != _lastAfk || speaking != _lastSpeaking)
        {
            Text = identity.Afk
                ? $"{identity.DisplayName}  (away)"
                : speaking
                    ? $"{identity.DisplayName}  <))"
                    : identity.DisplayName;
            _lastDisplayName = identity.DisplayName;
            _lastAfk = identity.Afk;
            _lastSpeaking = speaking;
            _textInitialized = true;
        }
        var alpha = 1f;
        if (GetViewport().GetCamera3D() is { } camera)
        {
            var distance = camera.GlobalPosition.DistanceTo(GlobalPosition);
            alpha = 1f - Mathf.Clamp(
                (distance - FullAlphaDistance) / (FadeEndDistance - FullAlphaDistance), 0f, 1f);
        }
        Modulate = new Color(1f, 1f, 1f, identity.Afk ? alpha * 0.45f : alpha);
        Visible = alpha > 0.02f;
    }
}
