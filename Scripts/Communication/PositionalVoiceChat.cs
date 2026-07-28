using Godot;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Communication;

/// <summary>
/// Dependency-free positional push-to-talk. Captures 48 kHz microphone audio,
/// downsamples it to 16 kHz mono PCM, relays through the server, and plays it
/// from the speaking player's world position.
/// </summary>
public partial class PositionalVoiceChat : Node
{
    private const int CaptureFrames = 960;
    private const int PlaybackRate = 16000;
    private AudioEffectCapture? _capture;
    private AudioStreamPlayer? _micPump;
    private readonly System.Collections.Generic.Dictionary<long, AudioStreamPlayer3D> _speakers = new();

    public override void _Ready()
    {
        AudioServer.InputDevice = "Default";
        var bus = AudioServer.BusCount;
        AudioServer.AddBus(bus);
        AudioServer.SetBusName(bus, "VoiceCapture");
        AudioServer.SetBusMute(bus, true);
        _capture = new AudioEffectCapture { BufferLength = 0.25f };
        AudioServer.AddBusEffect(bus, _capture);
        _micPump = new AudioStreamPlayer
        {
            Stream = new AudioStreamMicrophone(),
            Bus = "VoiceCapture",
            Autoplay = true
        };
        AddChild(_micPump);
        _micPump.Play();
    }

    public override void _Process(double delta)
    {
        UpdateSpeakerPositions();
        if (!Multiplayer.HasMultiplayerPeer() || !SessionManager.Instance.MatchActive ||
            !Input.IsActionPressed("push_to_talk") || _capture == null) return;
        while (_capture.CanGetBuffer(CaptureFrames))
        {
            var stereo = _capture.GetBuffer(CaptureFrames);
            var pcm = new byte[(stereo.Length / 3) * 2];
            var write = 0;
            for (var i = 0; i + 2 < stereo.Length; i += 3)
            {
                var mono = Mathf.Clamp((stereo[i].X + stereo[i].Y) * 0.5f, -1f, 1f);
                var sample = (short)(mono * short.MaxValue);
                pcm[write++] = (byte)(sample & 0xff);
                pcm[write++] = (byte)((sample >> 8) & 0xff);
            }
            if (Multiplayer.IsServer()) RelayVoice(Multiplayer.GetUniqueId(), pcm);
            else RpcId(1, nameof(SubmitVoice), pcm);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void SubmitVoice(byte[] pcm)
    {
        if (!Multiplayer.IsServer() || pcm.Length > 2048) return;
        RelayVoice(Multiplayer.GetRemoteSenderId(), pcm);
    }

    private void RelayVoice(long peerId, byte[] pcm)
    {
        if (!SessionManager.Instance.Players.ContainsKey(peerId)) return;
        PlayPacket(peerId, pcm);
        Rpc(nameof(ReceiveVoice), peerId, pcm);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void ReceiveVoice(long peerId, byte[] pcm)
    {
        if (peerId != Multiplayer.GetUniqueId()) PlayPacket(peerId, pcm);
    }

    private void PlayPacket(long peerId, byte[] pcm)
    {
        if (peerId == Multiplayer.GetUniqueId() || IsLocallyMuted(peerId) || pcm.Length % 2 != 0) return;
        var player = EnsureSpeaker(peerId);
        if (player.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback) return;
        var frames = new Vector2[pcm.Length / 2];
        for (var i = 0; i < frames.Length; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            var value = sample / 32768f;
            frames[i] = new Vector2(value, value);
        }
        if (playback.GetFramesAvailable() >= frames.Length) playback.PushBuffer(frames);
    }

    private AudioStreamPlayer3D EnsureSpeaker(long peerId)
    {
        if (_speakers.TryGetValue(peerId, out var existing)) return existing;
        var speaker = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = PlaybackRate, BufferLength = 0.25f },
            UnitSize = 8f,
            MaxDistance = 35f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
        };
        AddChild(speaker);
        speaker.Play();
        _speakers[peerId] = speaker;
        return speaker;
    }

    private void UpdateSpeakerPositions()
    {
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && _speakers.TryGetValue(player.PeerId, out var speaker))
                speaker.GlobalPosition = player.GlobalPosition + Vector3.Up * 1.5f;
    }

    private static bool IsLocallyMuted(long peerId) =>
        SessionManager.Instance.Players.TryGetValue(peerId, out var data) &&
        PlayerIdentityService.Instance.IsMuted(PlayerIdentity.FromDictionary(peerId, data));
}
