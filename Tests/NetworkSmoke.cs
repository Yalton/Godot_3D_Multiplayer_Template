using Godot;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Interaction;
using MultiplayerTemplate.Game;

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
    private bool _sawPlayerNodes;
    private bool _avatarBuiltHeadless;
    private bool _sawAfkFlag;
    private bool _sawAfkCleared;
    private bool _sawKickReason;
    private bool _requestedRogueKick;
    private bool _startedKickProbe;
    private int _rosterBeforeHostKick;
    private bool _identityRoundTripPassed;

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
            _sawKickReason |= message.Contains("smoke kick");
        };
        NetworkClock.Instance.SampleReceived += sample =>
        {
            _clockSynchronized |= NetworkClock.Instance.IsSynchronized;
            _qualitySampled |= sample.RttMs >= 0;
        };
        _securityHelpersPassed = CheckSecurityHelpers();
        _identityRoundTripPassed = CheckIdentityRoundTrip();
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
        else if (_role == "kick-server")
        {
            SessionManager.Instance.PlayerName = "KickHost";
            SessionManager.Instance.HostEnet(Port + 2);
            // The host's own roster snapshot (Players.Count == 2, in FinishKickServer) must
            // be taken BEFORE the rogue client's own natural end-of-scenario teardown, or the
            // rogue's disconnect (whether graceful or via process exit) removes it from the
            // roster too and the count reads 1, not 2. The runner staggers kick-target
            // (+0.3s) and kick-rogue (+0.5s) after this process starts, so their own
            // FinishAfter(3.5, ...) lands at wall-clock 3.8s and 4.0s respectively. Finishing
            // the host well before that (roster forms and the kick completes within ~2.5s)
            // keeps this deterministic instead of racing the clients' teardown.
            FinishAfter(3, FinishKickServer);
        }
        else if (_role == "kick-target")
        {
            SessionManager.Instance.PlayerName = "KickTarget";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 2);
            FinishAfter(3.5, FinishKickTarget);
        }
        else if (_role == "kick-rogue")
        {
            SessionManager.Instance.PlayerName = "KickRogue";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 2);
            FinishAfter(3.5, FinishKickRogue);
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
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player)
            {
                _sawPlayerNodes = true;
                _avatarBuiltHeadless |= player.Avatar != null;
            }
    }

    private void SubmitFeatureChecks()
    {
        if (_submittedFeatureChecks) return;
        _submittedFeatureChecks = true;
        TextChatManager.Instance.SubmitText("smoke-chat");
        if (Multiplayer.IsServer()) _ = SubmitInteractionAfterSpawn();
        if (Multiplayer.IsServer()) _ = DriveAfkProbe();
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

    private async Task DriveAfkProbe()
    {
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        foreach (var peerId in SessionManager.Instance.Players.Keys)
            if (peerId != 1) SessionManager.Instance.SetAfkServer(peerId, true);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        foreach (var peerId in SessionManager.Instance.Players.Keys)
            if (peerId != 1) SessionManager.Instance.SetAfkServer(peerId, false);
    }

    private async Task DriveKickProbe()
    {
        // Long enough for the rogue client's illegal request to have been served
        // and refused before the roster is sampled.
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        _rosterBeforeHostKick = SessionManager.Instance.Players.Count;
        foreach (var (peerId, data) in SessionManager.Instance.Players)
            if (data.GetValueOrDefault("name", "").AsString() == "KickTarget")
            {
                SessionManager.Instance.RequestKick(peerId, "smoke kick");
                break;
            }
    }

    private void OnRoster(Godot.Collections.Dictionary<long, Godot.Collections.Dictionary> players)
    {
        _sawThreePeers |= players.Count == 3;
        // HasMultiplayerPeer() must be checked before IsServer(): once DisconnectSession()
        // nulls the peer, IsServer() itself calls get_unique_id() and prints a Godot-level
        // ERROR line, which the harness treats as a failure.
        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer() &&
            players.TryGetValue(Multiplayer.GetUniqueId(), out var mine))
        {
            var afk = mine.GetValueOrDefault("afk", false).AsBool();
            _sawAfkFlag |= afk;
            _sawAfkCleared |= _sawAfkFlag && !afk;
        }
        if (_role == "kick-server" && players.Count == 3 && !_startedKickProbe && Multiplayer.IsServer())
        {
            _startedKickProbe = true;
            _ = DriveKickProbe();
        }
        if (_role == "kick-rogue" && !_requestedRogueKick && players.Count == 3)
        {
            _requestedRogueKick = true;
            foreach (var (peerId, data) in players)
                if (data.GetValueOrDefault("name", "").AsString() == "KickTarget")
                {
                    SessionManager.Instance.RequestKick(peerId, "rogue kick");
                    break;
                }
        }
        if (_role is not ("server" or "client")) return;
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
        var avatarGuard = _sawPlayerNodes && !_avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && _securityHelpersPassed &&
                      _sawChat && _sawPhysicsInteraction && _identityRoundTripPassed && avatarGuard &&
                      SessionManager.Instance.Players.Count == 1;
        GD.Print(success
            ? "SMOKE SERVER PASS (3 peers, synchronized load, playing)"
            : $"SMOKE SERVER FAIL roster={SessionManager.Instance.Players}, three={_sawThreePeers}, " +
              $"playing={_sawPlaying}, avatarGuard={avatarGuard}, identityRoundTrip={_identityRoundTripPassed}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishClient()
    {
        var loadedScene = MatchSceneCoordinator.Instance.LoadedSceneCount == 1;
        var avatarGuard = _sawPlayerNodes && !_avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && loadedScene &&
                      _sawChat && _sawPhysicsInteraction &&
                      _clockSynchronized && _qualitySampled && _identityRoundTripPassed &&
                      avatarGuard && _sawAfkFlag && _sawAfkCleared;
        GD.Print(success
            ? $"SMOKE CLIENT PASS ({_clientName}, synchronized scene and clock)"
            : $"SMOKE CLIENT FAIL name={_clientName}, roster={SessionManager.Instance.Players}, " +
              $"three={_sawThreePeers}, playing={_sawPlaying}, loaded={loadedScene}, " +
              $"clock={_clockSynchronized}, quality={_qualitySampled}, identityRoundTrip={_identityRoundTripPassed}, " +
              $"avatarGuard={avatarGuard}, afkSet={_sawAfkFlag}, afkCleared={_sawAfkCleared}");
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

    private async void FinishKickServer()
    {
        var rogueRefused = _rosterBeforeHostKick == 3;
        var hostKickWorked = SessionManager.Instance.Players.Count == 2;
        var success = rogueRefused && hostKickWorked;
        GD.Print(success
            ? "SMOKE KICK SERVER PASS (client request refused, host request honoured)"
            : $"SMOKE KICK SERVER FAIL before={_rosterBeforeHostKick}, " +
              $"after={SessionManager.Instance.Players.Count}");
        // Read the roster (above) well before the target and rogue reach their own
        // FinishAfter deadlines, then wait for them to finish and quit on their own before
        // tearing this session down. DisconnectSession() broadcasts "host ended the
        // session" to every still-connected peer, which would flip the rogue's State to
        // Offline out from under FinishKickRogue's own State == Lobby check if the two
        // raced — the rogue peer must still look connected to both sides until it has
        // finished checking that for itself.
        await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishKickTarget()
    {
        var success = _sawKickReason &&
                      SessionManager.Instance.State == SessionManager.SessionState.Offline;
        GD.Print(success
            ? "SMOKE KICK TARGET PASS (removed with a reason)"
            : $"SMOKE KICK TARGET FAIL reason={_sawKickReason}, " +
              $"state={SessionManager.Instance.State}");
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishKickRogue()
    {
        var success = _requestedRogueKick &&
                      SessionManager.Instance.State == SessionManager.SessionState.Lobby;
        GD.Print(success
            ? "SMOKE KICK ROGUE PASS (unauthorized request did not remove anyone)"
            : $"SMOKE KICK ROGUE FAIL attempted={_requestedRogueKick}, " +
              $"state={SessionManager.Instance.State}");
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

    private static bool CheckIdentityRoundTrip()
    {
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, true);
        var restored = PlayerIdentity.FromDictionary(7, identity.ToDictionary());
        var legacy = new Godot.Collections.Dictionary
        {
            ["name"] = "Bo",
            ["platform"] = "LAN",
            ["steam_id"] = 0L,
            ["is_host"] = false,
            ["ready"] = false
        };
        return restored.Afk && restored.Ready && restored.DisplayName == "Ann" &&
               !PlayerIdentity.FromDictionary(9, legacy).Afk;
    }
}
