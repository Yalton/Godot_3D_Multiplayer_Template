using Godot;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.UI;
using SettingsKit;

namespace MultiplayerTemplate.Communication;

/// <summary>
/// Dependency-free positional push-to-talk. Captures microphone audio at the
/// engine mix rate, resamples it to 16 kHz mono PCM, relays through the server,
/// and plays it from the speaking player's world position. The microphone is
/// only hot while a match is active — never at the menu or in the lobby.
/// </summary>
public partial class PositionalVoiceChat : Node
{
    public static PositionalVoiceChat Instance { get; private set; } = null!;
    public bool ClientCaptureEnabled => _captureAvailable;
    public float VoiceCaptureMeter { get; private set; }

    public bool IsSpeaking(long peerId) =>
        _speaking.IsSpeaking(peerId, Time.GetTicksMsec() / 1000.0);

    public float GetPeerVolume(long peerId) =>
        PlayerIdentityService.Instance.GetVoiceVolume(PlayerIdentityService.Instance.Get(peerId));

    public void SetPeerVolume(long peerId, float linear)
    {
        var clamped = VoiceVolumePolicy.Clamp(linear);
        PlayerIdentityService.Instance.SetVoiceVolume(PlayerIdentityService.Instance.Get(peerId), clamped);
        if (_speakers.TryGetValue(peerId, out var speaker))
            speaker.VolumeDb = VoiceVolumePolicy.ToDecibels(clamped);
    }
    private const int PlaybackRate = 16000;
    private AudioEffectCapture? _capture;
    private AudioStreamPlayer? _micPump;
    private AudioStreamPlayer? _micTestPlayer;
    private VoiceResampler? _resampler;
    private readonly VoiceFrameAssembler _frameAssembler = new();
    private IVoiceCodec? _encoder;
    private VoiceActivityDetector _vad = new(VoiceSettingsPolicy.DefaultVadThreshold, 2);
    private float _vadThreshold = VoiceSettingsPolicy.DefaultVadThreshold;
    private readonly System.Collections.Generic.Dictionary<long, AudioStreamPlayer3D> _speakers = new();
    private readonly System.Collections.Generic.Dictionary<long, VoiceJitterBuffer> _jitterBuffers = new();
    private readonly System.Collections.Generic.Dictionary<long, IVoiceCodec> _decoders = new();
    private readonly System.Collections.Generic.Dictionary<long, uint> _legacyReceiveSequences = new();
    private readonly RpcRateLimiter _rateLimiter = new();
    private readonly SpeakingTracker _speaking = new();
    private bool _captureAvailable;
    private bool _micTestActive;

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        SessionManager.Instance.MatchEnded += OnMatchEnded;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        _captureAvailable = true;
        _encoder = new OpusVoiceCodec();
        AudioServer.InputDevice = SettingsManager.Instance.GetVoiceInputDevice();
        var bus = AudioServer.BusCount;
        AudioServer.AddBus(bus);
        AudioServer.SetBusName(bus, "VoiceCapture");
        AudioServer.SetBusMute(bus, true);
        _capture = new AudioEffectCapture { BufferLength = 0.25f };
        AudioServer.AddBusEffect(bus, _capture);
        _resampler = new VoiceResampler(AudioServer.GetMixRate(), PlaybackRate);
        _micPump = new AudioStreamPlayer
        {
            Stream = new AudioStreamMicrophone(),
            Bus = "VoiceCapture"
        };
        AddChild(_micPump);
        _micTestPlayer = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = PlaybackRate, BufferLength = 0.25f },
            Bus = "Voice"
        };
        AddChild(_micTestPlayer);
        SessionManager.Instance.MatchStarted += StartCapture;
    }

    private void StartCapture()
    {
        if (_micPump == null || _micPump.Playing) return;
        ResetCaptureState();
        _micPump.Play();
    }

    public void StartMicTest()
    {
        if (!_captureAvailable || _micTestActive) return;
        StopCapture();
        _micTestActive = true;
        StartCapture();
        _micTestPlayer?.Play();
    }

    public void StopMicTest()
    {
        if (!_micTestActive) return;
        _micTestActive = false;
        _micTestPlayer?.Stop();
        StopCapture();
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.MatchEnded -= OnMatchEnded;
            SessionManager.Instance.MatchStarted -= StartCapture;
        }
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
    }

    private void OnMatchEnded()
    {
        StopMicTest();
        StopCapture();
        foreach (var speaker in _speakers.Values) speaker.QueueFree();
        _speakers.Clear();
        _jitterBuffers.Clear();
        _decoders.Clear();
        _legacyReceiveSequences.Clear();
        _speaking.Clear();
    }

    private void OnPeerDisconnected(long peerId)
    {
        _rateLimiter.Forget(peerId);
        if (_speakers.Remove(peerId, out var speaker)) speaker.QueueFree();
        _jitterBuffers.Remove(peerId);
        _decoders.Remove(peerId);
        _legacyReceiveSequences.Remove(peerId);
        _speaking.Remove(peerId);
    }

    public override void _Process(double delta)
    {
        UpdateSpeakerPositions();
        if (!_captureAvailable || _capture == null || _resampler == null) return;

        var inputDevice = SettingsManager.Instance.GetVoiceInputDevice();
        if (AudioServer.InputDevice != inputDevice)
        {
            AudioServer.InputDevice = inputDevice;
            ResetCaptureState();
        }

        var canTransmit = Multiplayer.HasMultiplayerPeer() &&
            SessionManager.Instance.State == SessionManager.SessionState.Playing &&
            !LocalInputContext.Instance.GameplaySuppressed;
        if (!canTransmit && !_micTestActive)
        {
            if (_micPump?.Playing == true)
                StopCapture();
            return;
        }
        StartCapture();

        while (_capture.CanGetBuffer(960))
        {
            var stereo = _capture.GetBuffer(960);
            var mono = new float[stereo.Length];
            var gain = SettingsManager.Instance.GetVoiceGain();
            for (var i = 0; i < stereo.Length; i++) mono[i] = (stereo[i].X + stereo[i].Y) * 0.5f * gain;
            var samples = _resampler.Process(mono);
            foreach (var frame in _frameAssembler.Push(samples))
            {
                VoiceCaptureMeter = ComputeRms(frame);
                if (_micTestActive)
                    PushSamples(_micTestPlayer, frame);
                if (!canTransmit || _micTestActive || !ShouldTransmit(frame)) continue;
                var encoded = _encoder?.Encode(frame) ?? [];
                if (encoded.Length == 0) continue;
                if (Multiplayer.IsServer()) RelayVoice(Multiplayer.GetUniqueId(), encoded);
                else RpcId(1, nameof(SubmitVoice), encoded);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void SubmitVoice(byte[] pcm)
    {
        if (!RpcSecurity.RequireServer(this, nameof(SubmitVoice)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(SubmitVoice), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, sender, nameof(SubmitVoice),
                SessionManager.SessionState.Playing) || pcm.Length > 2048) return;
        if (!_rateLimiter.Allow(sender, nameof(SubmitVoice), 60, 1000))
        {
            RpcSecurity.Reject(nameof(SubmitVoice), sender, "rate limit exceeded");
            return;
        }
        RelayVoice(sender, pcm);
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

    private void PlayPacket(long peerId, byte[] payload)
    {
        if (!_captureAvailable || peerId == Multiplayer.GetUniqueId() || IsLocallyMuted(peerId)) return;
        // Without a body to attach to, the speaker would sit at the world origin.
        if (FindPlayer(peerId) is not { } body) return;
        var sequence = _legacyReceiveSequences.TryGetValue(peerId, out var previous)
            ? previous
            : 0;
        _legacyReceiveSequences[peerId] = unchecked(sequence + 1);
        var packet = new VoicePacket(VoiceWirePolicy.FormatVersion, VoiceChannel.Proximity, sequence, payload);
        if (!_jitterBuffers.TryGetValue(peerId, out var jitter))
            _jitterBuffers[peerId] = jitter = new VoiceJitterBuffer();
        if (!_decoders.TryGetValue(peerId, out var decoder))
            _decoders[peerId] = decoder = new OpusVoiceCodec();
        jitter.Enqueue(packet);
        while (jitter.TryTake(out var next, out var packetLoss))
        {
            var frame = decoder.Decode(next, packetLoss);
            if (frame.Length == 0) continue;
            _speaking.NotePacket(peerId, Time.GetTicksMsec() / 1000.0);
            var player = EnsureSpeaker(peerId);
            player.GlobalPosition = body.GlobalPosition + Vector3.Up * 1.5f;
            PushSamples(player, frame);
        }
    }

    private AudioStreamPlayer3D EnsureSpeaker(long peerId)
    {
        if (_speakers.TryGetValue(peerId, out var existing)) return existing;
        var speaker = new AudioStreamPlayer3D
        {
            Stream = new AudioStreamGenerator { MixRate = PlaybackRate, BufferLength = 0.25f },
            Bus = "Voice",
            UnitSize = 8f,
            MaxDistance = 35f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
        };
        speaker.VolumeDb = VoiceVolumePolicy.ToDecibels(GetPeerVolume(peerId));
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

    private NetworkPlayer? FindPlayer(long peerId)
    {
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == peerId) return player;
        return null;
    }

    private static bool IsLocallyMuted(long peerId)
    {
        if (!SessionManager.Instance.Players.TryGetValue(peerId, out var data)) return false;
        var identity = PlayerIdentity.FromDictionary(peerId, data);
        return PlayerIdentityService.Instance.IsMuted(identity) ||
            PlayerIdentityService.Instance.IsBlocked(identity);
    }

    private bool ShouldTransmit(short[] frame)
    {
        if (SettingsManager.Instance.GetVoiceTransmissionMode() == (int)VoiceTransmissionMode.PushToTalk)
            return Input.IsActionPressed("push_to_talk");

        var threshold = SettingsManager.Instance.GetVoiceVadThreshold();
        if (!Mathf.IsEqualApprox(threshold, _vadThreshold))
        {
            _vadThreshold = threshold;
            _vad = new VoiceActivityDetector(threshold, 2);
        }
        return _vad.IsActive(frame);
    }

    private void ResetCaptureState()
    {
        _capture?.ClearBuffer();
        _resampler?.Reset();
        _frameAssembler.Reset();
        _vadThreshold = SettingsManager.Instance.GetVoiceVadThreshold();
        _vad = new VoiceActivityDetector(_vadThreshold, 2);
        VoiceCaptureMeter = 0f;
    }

    private void StopCapture()
    {
        _micPump?.Stop();
        ResetCaptureState();
    }

    private static float ComputeRms(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0) return 0f;
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / (double)short.MaxValue;
            sumSquares += normalized * normalized;
        }
        return (float)Math.Sqrt(sumSquares / samples.Length);
    }

    private static void PushSamples(AudioStreamPlayer? player, short[] samples)
    {
        if (player?.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback) return;
        PushSamples(playback, samples);
    }

    private static void PushSamples(AudioStreamPlayer3D player, short[] samples)
    {
        if (player.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback) return;
        PushSamples(playback, samples);
    }

    private static void PushSamples(AudioStreamGeneratorPlayback playback, short[] samples)
    {
        if (playback.GetFramesAvailable() < samples.Length) return;
        var frames = new Vector2[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i] / 32768f;
            frames[i] = new Vector2(value, value);
        }
        playback.PushBuffer(frames);
    }
}
