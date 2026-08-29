using Godot;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

public readonly record struct PlayerRowOptions(
    bool ShowReady,
    bool ShowQuality,
    bool ShowVoiceControls,
    bool ShowKick,
    long LocalPeerId);

/// <summary>
/// One roster entry, shared by the lobby list and the in-match player list so both
/// render identity, presence, and social controls the same way.
/// </summary>
public partial class PlayerRow : HBoxContainer
{
    public event Action? Changed;
    public TextureRect Avatar { get; private set; } = null!;

    private Label _name = null!;
    private Label _quality = null!;
    private long _peerId;
    private PlayerRowOptions _options;

    public void Configure(PlayerIdentity identity, PlayerRowOptions options)
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _peerId = identity.PeerId;
        // Remembered so RefreshSpeaking rebuilds the same columns instead of
        // silently dropping the ready state on the next frame.
        _options = options;

        Avatar = new TextureRect
        {
            CustomMinimumSize = new Vector2(36, 36),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        AddChild(Avatar);

        _name = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_name);
        WriteName(identity);

        _quality = new Label
        {
            TooltipText = "Round-trip latency and rolling packet-loss estimate",
            CustomMinimumSize = new Vector2(110, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visible = options.ShowQuality
        };
        AddChild(_quality);
        RefreshQuality();

        if (identity.PeerId == options.LocalPeerId) return;

        var muted = PlayerIdentityService.Instance.IsMuted(identity);
        AddChild(TextButton(muted ? "Unmute" : "Mute", () =>
        {
            PlayerIdentityService.Instance.SetMuted(
                identity, !PlayerIdentityService.Instance.IsMuted(identity));
            Changed?.Invoke();
        }));

        if (options.ShowVoiceControls)
        {
            var slider = new HSlider
            {
                MinValue = VoiceVolumePolicy.Min,
                MaxValue = VoiceVolumePolicy.Max,
                Step = 0.05,
                Value = PositionalVoiceChat.Instance.GetPeerVolume(identity.PeerId),
                CustomMinimumSize = new Vector2(110, 0),
                TooltipText = "How loudly you hear this player"
            };
            var peerId = identity.PeerId;
            slider.ValueChanged += value =>
                PositionalVoiceChat.Instance.SetPeerVolume(peerId, (float)value);
            AddChild(slider);

            var blocked = PlayerIdentityService.Instance.IsBlocked(identity);
            AddChild(TextButton(blocked ? "Unblock" : "Block", () =>
            {
                PlayerIdentityService.Instance.SetBlocked(
                    identity, !PlayerIdentityService.Instance.IsBlocked(identity));
                Changed?.Invoke();
            }));
        }

        if (!options.ShowKick) return;
        AddChild(TextButton("Kick", () =>
        {
            SessionManager.Instance.RequestKick(identity.PeerId, "Removed by the host.");
            Changed?.Invoke();
        }));
    }

    public void RefreshSpeaking()
    {
        if (!IsInstanceValid(_name)) return;
        WriteName(PlayerIdentityService.Instance.Get(_peerId));
    }

    public void RefreshQuality()
    {
        if (!_options.ShowQuality || !IsInstanceValid(_quality)) return;
        _quality.Text = QualityText(_peerId);
    }

    /// <summary>Moved here verbatim from Main.QualityText so both lists format alike.</summary>
    private static string QualityText(long peerId)
    {
        var quality = ConnectionQualityMonitor.Instance.ForPeer(peerId);
        var icon = quality.Health switch
        {
            ConnectionHealth.Excellent => "●",
            ConnectionHealth.Good => "●",
            ConnectionHealth.Degraded => "▲",
            _ => "×"
        };
        return $"{icon} {quality.ShortText}";
    }

    private void WriteName(PlayerIdentity identity)
    {
        var host = identity.IsHost ? " ★ HOST" : "";
        var ready = _options.ShowReady ? identity.Ready ? "    READY" : "    NOT READY" : "";
        var away = identity.Afk ? "  (away)" : "";
        var speaking = PositionalVoiceChat.Instance != null &&
                       PositionalVoiceChat.Instance.IsSpeaking(identity.PeerId)
            ? "  <))"
            : "";
        _name.Text = $"{identity.DisplayName}{host}{ready}{away}{speaking}    [{identity.Platform}]";
    }

    private static Button TextButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }
}
