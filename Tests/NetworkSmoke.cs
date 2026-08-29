using Godot;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Interaction;

namespace MultiplayerTemplate.Tests;

public partial class NetworkSmoke : Node
{
    private const int Port = 18910;
    private string _role = "";
    private string _clientName = "SmokeClient";
    private bool _sawThreePeers;
    private bool _sawPlaying;
    private bool _requestedReady;
    private bool _requestedStart;
    private bool _sawCompatibilityRejection;
    private bool _clockSynchronized;
    private bool _qualitySampled;
    private bool _securityHelpersPassed;
    private bool _hostDisconnected;
    private bool _sawChat;
    private bool _sawPhysicsInteraction;
    private bool _submittedFeatureChecks;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--smoke-role=")) _role = arg["--smoke-role=".Length..];
            if (arg.StartsWith("--smoke-name=")) _clientName = arg["--smoke-name=".Length..];
        }
        SessionManager.Instance.RosterChanged += OnRoster;
        SessionManager.Instance.StateChanged += (_, current) =>
        {
            _sawPlaying |= current == SessionManager.SessionState.Playing;
            if (current == SessionManager.SessionState.Playing) SubmitFeatureChecks();
        };
        TextChatManager.Instance.MessageReceived += (_, _, text) =>
            _sawChat |= text == "smoke-chat";
        SessionManager.Instance.ConnectionStateChanged += (_, message) =>
        {
            _sawCompatibilityRejection |= message.Contains("Incompatible build");
            _hostDisconnected |= message.Contains("host ended", StringComparison.OrdinalIgnoreCase);
        };
        NetworkClock.Instance.SampleReceived += sample =>
        {
            _clockSynchronized |= NetworkClock.Instance.IsSynchronized;
            _qualitySampled |= sample.RttMs >= 0;
        };
        _securityHelpersPassed = CheckSecurityHelpers();
        if (_role == "server")
        {
            SessionManager.Instance.PlayerName = "SmokeHost";
            SessionManager.Instance.HostEnet(Port);
            FinishAfter(16, FinishServer);
        }
        else if (_role == "client")
        {
            SessionManager.Instance.PlayerName = _clientName;
            SessionManager.Instance.JoinEnet("127.0.0.1", Port);
            FinishAfter(9, FinishClient);
        }
        else if (_role == "incompatible")
        {
            SessionManager.Instance.PlayerName = "WrongProtocol";
            SessionManager.Instance.ProtocolVersionOverrideForTesting =
                NetworkBuildInfo.ProtocolVersion + 1;
            SessionManager.Instance.JoinEnet("127.0.0.1", Port);
            FinishAfter(3, FinishIncompatible);
        }
        else if (_role == "host-loss-server")
        {
            SessionManager.Instance.PlayerName = "DisposableHost";
            SessionManager.Instance.HostEnet(Port + 1);
            FinishAfter(1.5, () => GetTree().Quit(0));
        }
        else if (_role == "host-loss-client")
        {
            SessionManager.Instance.PlayerName = "HostLossClient";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 1);
            FinishAfter(5, FinishHostLossClient);
        }
        else if (_role == "dedicated")
        {
            FinishAfter(1, FinishDedicated);
        }
        else
        {
            GD.PushError("Pass --smoke-role=server or --smoke-role=client");
            GetTree().Quit(2);
        }
    }

    public override void _Process(double delta)
    {
        if (!_sawPlaying) return;
        foreach (var node in GetTree().GetNodesInGroup("network_physics"))
            if (node is NetworkPhysicsObject { HeldByPeer: not 0 })
            {
                _sawPhysicsInteraction = true;
                return;
            }
    }

    private void SubmitFeatureChecks()
    {
        if (_submittedFeatureChecks) return;
        _submittedFeatureChecks = true;
        TextChatManager.Instance.SubmitText("smoke-chat");
        if (Multiplayer.IsServer()) _ = SubmitInteractionAfterSpawn();
    }

    private async Task SubmitInteractionAfterSpawn()
    {
        await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        foreach (var node in GetTree().GetNodesInGroup("network_physics"))
            if (node is NetworkPhysicsObject physics)
            {
                InteractionManager.Instance.Request(physics.GetPath());
                break;
            }
    }

    private void OnRoster(Godot.Collections.Dictionary<long, Godot.Collections.Dictionary> players)
    {
        _sawThreePeers |= players.Count == 3;
        if (players.Count != 3) return;
        if (!Multiplayer.IsServer() && !_requestedReady)
        {
            _requestedReady = true;
            SessionManager.Instance.SetReady(true);
            return;
        }
        if (!Multiplayer.IsServer() || _requestedStart) return;
        foreach (var (id, data) in players)
            if (id != 1 && !data.GetValueOrDefault("ready", false).AsBool()) return;
        _requestedStart = true;
        SessionManager.Instance.StartMatch();
    }

    private async void FinishAfter(double seconds, Action callback)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        callback();
    }

    private void FinishServer()
    {
        var success = _sawThreePeers && _sawPlaying && _securityHelpersPassed &&
                      _sawChat && _sawPhysicsInteraction &&
                      SessionManager.Instance.Players.Count == 1;
        GD.Print(success
            ? "SMOKE SERVER PASS (3 peers, synchronized load, playing)"
            : $"SMOKE SERVER FAIL roster={SessionManager.Instance.Players}, three={_sawThreePeers}, playing={_sawPlaying}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishClient()
    {
        var loadedScene = MatchSceneCoordinator.Instance.LoadedSceneCount == 1;
        var success = _sawThreePeers && _sawPlaying && loadedScene &&
                      _sawChat && _sawPhysicsInteraction &&
                      _clockSynchronized && _qualitySampled;
        GD.Print(success
            ? $"SMOKE CLIENT PASS ({_clientName}, synchronized scene and clock)"
            : $"SMOKE CLIENT FAIL name={_clientName}, roster={SessionManager.Instance.Players}, " +
              $"three={_sawThreePeers}, playing={_sawPlaying}, loaded={loadedScene}, " +
              $"clock={_clockSynchronized}, quality={_qualitySampled}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishIncompatible()
    {
        var success = _sawCompatibilityRejection &&
                      SessionManager.Instance.State == SessionManager.SessionState.Offline;
        GD.Print(success
            ? "SMOKE INCOMPATIBLE PASS (rejected before registration)"
            : $"SMOKE INCOMPATIBLE FAIL rejected={_sawCompatibilityRejection}, " +
              $"state={SessionManager.Instance.State}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishHostLossClient()
    {
        var success = _hostDisconnected &&
                      SessionManager.Instance.State == SessionManager.SessionState.Offline;
        GD.Print(success
            ? "SMOKE HOST LOSS PASS (client returned offline)"
            : $"SMOKE HOST LOSS FAIL noticed={_hostDisconnected}, state={SessionManager.Instance.State}");
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishDedicated()
    {
        var voiceDisabled = !PositionalVoiceChat.Instance.ClientCaptureEnabled;
        var chatDisabled = !TextChatManager.Instance.ClientUiEnabled;
        var success = SessionManager.Instance.IsDedicatedServer && voiceDisabled && chatDisabled;
        GD.Print(success
            ? "SMOKE DEDICATED PASS (client-only services disabled)"
            : $"SMOKE DEDICATED FAIL dedicated={SessionManager.Instance.IsDedicatedServer}, " +
              $"voice={voiceDisabled}, chat={chatDisabled}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private static bool CheckSecurityHelpers()
    {
        var limiter = new RpcRateLimiter();
        var limited = limiter.Allow(7, "test", 2, 10_000) &&
                      limiter.Allow(7, "test", 2, 10_000) &&
                      !limiter.Allow(7, "test", 2, 10_000);
        var cooldowns = new RpcCooldowns();
        var cooled = cooldowns.TryConsume(7, "test", 10_000) &&
                     !cooldowns.TryConsume(7, "test", 10_000);
        var distance = RpcSecurity.WithinDistance(Vector3.Zero, new Vector3(3, 0, 0), 4,
            "test", 7);
        return limited && cooled && distance;
    }
}
