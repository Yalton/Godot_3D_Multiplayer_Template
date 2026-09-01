using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Interaction;
using MultiplayerTemplate.Game;

namespace MultiplayerTemplate.Tests;

public partial class NetworkSmoke : Node
{
    private const int Port = 18910;
    // Looping (state-bearing) emotes from EmoteCatalogue: 5 is "Sit", 6 is "Dance". Two
    // distinct looping ids let the bad-id probe on emote-b prove its RPC pipe still works
    // (by landing id 6) without ever being confused for the id-5 sitter emote-late looks
    // for on emote-a.
    private const int SitEmoteId = 5;
    private const int ProofEmoteId = 6;
    private const int UnknownEmoteId = 9999;
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
    private bool _emoteClipsResolved;
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
    private bool _requestedLoopEmote;
    private bool _cancelledEmoteByMoving;
    private bool _sawEmoteReplicated;
    private bool _sawEmoteCancelReplicated;
    private bool _requestedBadEmote;
    private bool _sawBadEmoteRefused;
    private bool _sawProofEmoteApplied;
    private long _sitterPeerId = -1;
    private bool _sawLateJoinSpawnedSitterNode;
    private bool _sawLateJoinEmote;
    private bool _appearanceRegistrationPassed;
    private bool _appearanceInvalidRefused;
    private bool _appearancePlayingRefused;
    private bool _appearanceUpdateRequested;
    private bool _appearancePlayingRequestPending;
    private bool _appearancePlayingRequestedInPlaying;
    private bool _hostAppearanceRegistrationPassed;
    private bool _hostAppearancePlayingRefused;
    private bool _hostAppearanceProbeRequested;
    private bool _rigAppearanceRenderingPassed;
    private bool _spawnedClientAAppearanceRendered;
    private bool _clientALobbyAppearanceUpdated;
    private bool _clientAExistingRigUpdated;
    private BlockmanRig? _clientARigBeforeLobbyUpdate;
    private GameWorld? _lobbyAppearanceWorld;

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
            if (current == SessionManager.SessionState.Playing)
            {
                SubmitFeatureChecks();
                RequestPlayingAppearanceProbeIfReady();
            }
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
        _emoteClipsResolved = CheckEmoteClipsResolve();
        _rigAppearanceRenderingPassed = CheckRigAppearanceRendering();
        if (_role == "server")
        {
            AppearanceProfileService.Instance.SetRequestedAppearance(HostSmokeAppearance);
            SessionManager.Instance.PlayerName = "SmokeHost";
            SessionManager.Instance.HostEnet(Port);
            FinishAfter(16, FinishServer);
        }
        else if (_role == "client")
        {
            if (_clientName == "ClientA")
                AppearanceProfileService.Instance.SetRequestedAppearance(SmokeAppearance);
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
        else if (_role == "emote-server")
        {
            SessionManager.Instance.PlayerName = "EmoteHost";
            SessionManager.Instance.HostEnet(Port + 3);
            FinishAfter(12, FinishEmoteServer);
        }
        else if (_role == "emote-a")
        {
            // The looping-emote requester: sits down shortly after the match starts,
            // holds it long enough for emote-late to join mid-sit, then cancels it by
            // moving.
            SessionManager.Instance.PlayerName = "EmoteSitter";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 3);
            FinishAfter(10, FinishEmoteA);
        }
        else if (_role == "emote-b")
        {
            // The observer: watches EmoteSitter's node from a different process (never
            // its own), and separately probes an unknown emote id against itself.
            SessionManager.Instance.PlayerName = "EmoteObserver";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 3);
            FinishAfter(10, FinishEmoteB);
        }
        else if (_role == "emote-late")
        {
            // Joins well after EmoteSitter has already sat down, through the same
            // late-join path host-loss and kick don't exercise: HandleLateJoin /
            // SpawnForLateJoin while the match is already Playing.
            SessionManager.Instance.PlayerName = "LateJoiner";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 3);
            FinishAfter(5, FinishEmoteLate);
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
        if (_role == "emote-b") ObserveSitterFromAnotherClient();
        if (_role == "emote-late") ObserveLateJoinSnapshot();
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
                var clientA = ResolvePeerIdByName("ClientA");
                if (player.PeerId == clientA && player.Avatar is BlockmanRig rig &&
                    TryAppearance(clientA, out var appearance))
                    _spawnedClientAAppearanceRendered |= RigReflects(rig, appearance);
            }
    }

    private void SubmitFeatureChecks()
    {
        if (_submittedFeatureChecks) return;
        _submittedFeatureChecks = true;
        TextChatManager.Instance.SubmitText("smoke-chat");
        if (Multiplayer.IsServer()) _ = SubmitInteractionAfterSpawn();
        if (Multiplayer.IsServer()) _ = DriveAfkProbe();
        if (_role == "emote-a") _ = DriveEmoteSitter();
        if (_role == "emote-b") _ = DriveEmoteBadIdProbe();
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

    // Sits down shortly after spawning, holds the emote long enough for emote-late to
    // join mid-sit and take its snapshot, then cancels it by moving. The move is
    // simulated the same way a real client would produce it — pressing the actual input
    // action so ReadLocalInput/SubmitInput carries it to the server — rather than
    // poking network state directly, so a regression in that path would fail this too.
    private async Task DriveEmoteSitter()
    {
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        var mine = FindOwnPlayer();
        if (mine != null)
        {
            _requestedLoopEmote = true;
            mine.RequestEmote(SitEmoteId);
        }
        await ToSignal(GetTree().CreateTimer(5.5), SceneTreeTimer.SignalName.Timeout);
        Input.ActionPress("move_forward");
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        Input.ActionRelease("move_forward");
        _cancelledEmoteByMoving = true;
    }

    // Requests an id absent from EmoteCatalogue against itself, then immediately follows
    // it with a real looping emote on the same connection. The second request is the
    // load-bearing part of this probe: without it, ActiveEmote staying at None after the
    // bad id would be indistinguishable from the request never reaching the server at
    // all. Only seeing the second one land proves the first one was actually evaluated
    // and refused.
    private async Task DriveEmoteBadIdProbe()
    {
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        var mine = FindOwnPlayer();
        if (mine == null) return;
        mine.RequestEmote(UnknownEmoteId);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        _requestedBadEmote = true;
        _sawBadEmoteRefused = mine.ActiveEmote == EmoteCatalogue.None;
        mine.RequestEmote(ProofEmoteId);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        _sawProofEmoteApplied = mine.ActiveEmote == ProofEmoteId &&
                                SessionManager.Instance.State == SessionManager.SessionState.Playing;
    }

    // Never returns emote-b's own node: this must observe replication on someone else's
    // process, or a bug that only updates the requester locally would pass anyway.
    private void ObserveSitterFromAnotherClient()
    {
        if (_sitterPeerId < 0) _sitterPeerId = ResolvePeerIdByName("EmoteSitter");
        if (_sitterPeerId < 0) return;
        var sitter = FindPlayerNode(_sitterPeerId);
        if (sitter == null) return;
        if (sitter.ActiveEmote == SitEmoteId) _sawEmoteReplicated = true;
        // Gated on having genuinely seen it active first, so a node that just starts and
        // stays at None the whole time cannot be mistaken for "the cancel replicated".
        if (_sawEmoteReplicated && sitter.ActiveEmote == EmoteCatalogue.None)
            _sawEmoteCancelReplicated = true;
    }

    private void ObserveLateJoinSnapshot()
    {
        if (_sitterPeerId < 0) _sitterPeerId = ResolvePeerIdByName("EmoteSitter");
        if (_sitterPeerId < 0) return;
        var sitter = FindPlayerNode(_sitterPeerId);
        if (sitter == null) return;
        // Distinguishes "the sitter's node never spawned for me" from "it spawned but
        // ActiveEmote was wrong" — collapsing those into one flag would hide which half
        // of the late-join path broke.
        _sawLateJoinSpawnedSitterNode = true;
        if (sitter.ActiveEmote == SitEmoteId) _sawLateJoinEmote = true;
    }

    private NetworkPlayer? FindOwnPlayer() => FindPlayerNode(Multiplayer.GetUniqueId());

    private NetworkPlayer? FindPlayerNode(long peerId)
    {
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player && player.PeerId == peerId) return player;
        return null;
    }

    private static long ResolvePeerIdByName(string name)
    {
        foreach (var (peerId, data) in SessionManager.Instance.Players)
            if (data.GetValueOrDefault("name", "").AsString() == name) return peerId;
        return -1;
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
        var clientA = ResolvePeerIdByName("ClientA");
        if (clientA >= 0 && players.TryGetValue(clientA, out var clientAData))
        {
            var clientAAppearance = PlayerIdentity.FromDictionary(clientA, clientAData).Appearance;
            _appearanceRegistrationPassed |= clientAAppearance == SmokeAppearance;
        }
        if (players.TryGetValue(1, out var hostData))
            _hostAppearanceRegistrationPassed |= PlayerIdentity.FromDictionary(1, hostData).Appearance == HostSmokeAppearance;
        if (_role == "client" && _clientName == "ClientA" && players.Count == 3 && !_appearanceUpdateRequested &&
            SessionManager.Instance.State == SessionManager.SessionState.Lobby)
        {
            _appearanceUpdateRequested = true;
            PrepareClientARigForLobbyUpdate(clientA);
            _ = DriveAppearanceProbes();
        }
        RequestPlayingAppearanceProbeIfReady();
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
        if (_role == "emote-server" && players.Count == 3 && !_requestedStart)
        {
            var allReady = true;
            foreach (var (id, data) in players)
                if (id != 1 && !data.GetValueOrDefault("ready", false).AsBool()) { allReady = false; break; }
            if (allReady)
            {
                _requestedStart = true;
                SessionManager.Instance.StartMatch();
            }
        }
        if ((_role == "emote-a" || _role == "emote-b") && players.Count == 3 && !_requestedReady)
        {
            _requestedReady = true;
            SessionManager.Instance.SetReady(true);
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

    private async Task DriveAppearanceProbes()
    {
        SessionManager.Instance.RequestAppearance(LobbyUpdatedAppearance);
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        _clientALobbyAppearanceUpdated = TryMyAppearance(out var updated) && updated == LobbyUpdatedAppearance;
        _clientAExistingRigUpdated = _clientARigBeforeLobbyUpdate != null &&
                                     RigReflects(_clientARigBeforeLobbyUpdate, LobbyUpdatedAppearance);
        SessionManager.Instance.RequestAppearance(new AvatarAppearance(999, 999, 999, 999, 999));
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        _appearanceInvalidRefused = TryMyAppearance(out var appearance) && appearance == LobbyUpdatedAppearance;
        _appearancePlayingRequestPending = true;
        RequestPlayingAppearanceProbeIfReady();
    }

    private void RequestPlayingAppearanceProbeIfReady()
    {
        if (SessionManager.Instance.State != SessionManager.SessionState.Playing) return;
        if (_role == "server" && !_hostAppearanceProbeRequested)
        {
            _hostAppearanceProbeRequested = true;
            SessionManager.Instance.RequestAppearance(AvatarAppearance.Default);
            _hostAppearancePlayingRefused = TryAppearance(1, out var hostAppearance) &&
                                           hostAppearance == HostSmokeAppearance;
        }
        if (_role != "client" || _clientName != "ClientA" || !_appearancePlayingRequestPending ||
            _appearancePlayingRequestedInPlaying) return;
        _appearancePlayingRequestedInPlaying = true;
        SessionManager.Instance.RequestAppearance(AvatarAppearance.Default);
        FinishAppearancePlayingProbe();
    }

    private async void FinishAppearancePlayingProbe()
    {
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        _appearancePlayingRefused = TryMyAppearance(out var appearance) && appearance == LobbyUpdatedAppearance;
    }

    private void FinishServer()
    {
        var avatarGuard = _sawPlayerNodes && _avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && _securityHelpersPassed &&
                      _sawChat && _sawPhysicsInteraction && _identityRoundTripPassed && avatarGuard &&
                      _appearanceRegistrationPassed && _hostAppearanceRegistrationPassed && _hostAppearancePlayingRefused &&
                      _rigAppearanceRenderingPassed && _spawnedClientAAppearanceRendered &&
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
        var avatarGuard = _sawPlayerNodes && _avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && loadedScene &&
                      _sawChat && _sawPhysicsInteraction &&
                      _clockSynchronized && _qualitySampled && _identityRoundTripPassed &&
                      avatarGuard && _sawAfkFlag && _sawAfkCleared && _appearanceRegistrationPassed &&
                      _rigAppearanceRenderingPassed && _spawnedClientAAppearanceRendered &&
                      (_clientName != "ClientA" || (_clientALobbyAppearanceUpdated && _clientAExistingRigUpdated)) &&
                      (_clientName != "ClientA" || (_appearanceInvalidRefused && _appearancePlayingRefused &&
                                                     _appearancePlayingRequestedInPlaying));
        GD.Print(success
            ? $"SMOKE CLIENT PASS ({_clientName}, synchronized scene and clock)"
            : $"SMOKE CLIENT FAIL name={_clientName}, roster={SessionManager.Instance.Players}, " +
              $"three={_sawThreePeers}, playing={_sawPlaying}, loaded={loadedScene}, " +
              $"clock={_clockSynchronized}, quality={_qualitySampled}, identityRoundTrip={_identityRoundTripPassed}, " +
              $"avatarGuard={avatarGuard}, afkSet={_sawAfkFlag}, afkCleared={_sawAfkCleared}, " +
              $"rig={_rigAppearanceRenderingPassed}, spawnedClientA={_spawnedClientAAppearanceRendered}, " +
              $"lobbyUpdate={_clientALobbyAppearanceUpdated}, existingRig={_clientAExistingRigUpdated}");
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
        var nullRig = AvatarFactory.Build(1) == null;
        var success = SessionManager.Instance.IsDedicatedServer && voiceDisabled && chatDisabled && nullRig;
        GD.Print(success
            ? "SMOKE DEDICATED PASS (client-only services disabled)"
            : $"SMOKE DEDICATED FAIL dedicated={SessionManager.Instance.IsDedicatedServer}, " +
              $"voice={voiceDisabled}, chat={chatDisabled}, nullRig={nullRig}");
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
            ? "SMOKE KICK ROGUE PASS (rogue's own unauthorized request did not get it " +
              "disconnected; SMOKE KICK SERVER is what proves no one else was removed)"
            : $"SMOKE KICK ROGUE FAIL attempted={_requestedRogueKick}, " +
              $"state={SessionManager.Instance.State}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishEmoteServer()
    {
        var success = _sawPlaying && SessionManager.Instance.Players.Count == 1 && _emoteClipsResolved;
        GD.Print(success
            ? "SMOKE EMOTE SERVER PASS (hosted the sit/late-join/bad-id scenarios; every " +
              "EmoteCatalogue clip resolves against the built animation library)"
            : $"SMOKE EMOTE SERVER FAIL playing={_sawPlaying}, roster={SessionManager.Instance.Players}, " +
              $"emoteClipsResolved={_emoteClipsResolved}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishEmoteA()
    {
        // A regression here would be caught on emote-b's side (SMOKE EMOTE B), which
        // watches this peer from a different process; this just confirms the requester
        // itself carried out both actions and never got disconnected doing so.
        var success = _sawPlaying && _requestedLoopEmote && _cancelledEmoteByMoving &&
                      SessionManager.Instance.State == SessionManager.SessionState.Playing;
        GD.Print(success
            ? "SMOKE EMOTE A PASS (requested a looping emote, then cancelled it by moving)"
            : $"SMOKE EMOTE A FAIL playing={_sawPlaying}, requested={_requestedLoopEmote}, " +
              $"cancelled={_cancelledEmoteByMoving}, state={SessionManager.Instance.State}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishEmoteB()
    {
        // Movement cancelling the emote is scenario 2, and is only genuine if scenario 1
        // (replication) was seen first — see the latch in ObserveSitterFromAnotherClient.
        // The bad-id half proves both the refusal (ActiveEmote never took 9999) and that
        // the refusal was not silence (the follow-up valid id did land).
        var success = _sawEmoteReplicated && _sawEmoteCancelReplicated &&
                      _requestedBadEmote && _sawBadEmoteRefused && _sawProofEmoteApplied;
        GD.Print(success
            ? "SMOKE EMOTE B PASS (saw replication and cancellation on another peer; " +
              "unknown id refused without disconnecting, valid id after it still landed)"
            : $"SMOKE EMOTE B FAIL replicated={_sawEmoteReplicated}, " +
              $"cancelReplicated={_sawEmoteCancelReplicated}, badRequested={_requestedBadEmote}, " +
              $"badRefused={_sawBadEmoteRefused}, proofApplied={_sawProofEmoteApplied}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishEmoteLate()
    {
        var success = _sawPlaying && _sawLateJoinSpawnedSitterNode && _sawLateJoinEmote;
        GD.Print(success
            ? "SMOKE EMOTE LATE PASS (late joiner saw the sitter already emoting)"
            : $"SMOKE EMOTE LATE FAIL playing={_sawPlaying}, " +
              $"sitterSpawned={_sawLateJoinSpawnedSitterNode}, sawEmote={_sawLateJoinEmote}");
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

    // The clip-name contract between ProceduralAnimationLibrary and EmoteCatalogue is
    // otherwise unchecked: Godot silently ignores an unresolvable animation name, so a typo
    // in either table would leave a motionless character with no error, a green build, and
    // a green unit-test suite. This runs inside the real engine NetworkSmoke already has,
    // building the library once and confirming every catalogue row's clip actually resolves.
    private static bool CheckEmoteClipsResolve()
    {
        var library = ProceduralAnimationLibrary.Build();
        foreach (var entry in EmoteCatalogue.All)
            if (!library.HasAnimation(entry.Clip)) return false;
        return true;
    }

    private static bool CheckIdentityRoundTrip()
    {
        var appearance = new AvatarAppearance(CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.BackItemId, CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId);
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, true, appearance);
        var restored = PlayerIdentity.FromDictionary(7, identity.ToDictionary());
        var legacy = new Godot.Collections.Dictionary
        {
            ["name"] = "Bo",
            ["platform"] = "LAN",
            ["steam_id"] = 0L,
            ["is_host"] = false,
            ["ready"] = false
        };
        var partial = new Godot.Collections.Dictionary
        {
            [AppearanceProfileCodec.HeadKey] = CosmeticCatalogue.HeadItemId
        };
        var legacyIdentity = PlayerIdentity.FromDictionary(9, legacy);
        var partialIdentity = PlayerIdentity.FromDictionary(9, partial);
        return restored.Afk && restored.Ready && restored.DisplayName == "Ann" &&
               restored.Appearance == appearance && !legacyIdentity.Afk &&
               legacyIdentity.Appearance == AvatarAppearance.Default &&
               partialIdentity.Appearance == new AvatarAppearance(AvatarAppearance.Default.BodyColorId,
                   CosmeticCatalogue.HeadItemId, CosmeticCatalogue.None, CosmeticCatalogue.None,
                   CosmeticCatalogue.None);
    }

    private void PrepareClientARigForLobbyUpdate(long clientA)
    {
        foreach (var node in GetTree().GetNodesInGroup("game_world"))
        {
            if (node is GameWorld world)
            {
                SpawnLobbyProbePlayer(world, clientA);
                return;
            }
        }
        _lobbyAppearanceWorld = new GameWorld { Name = "LobbyAppearanceWorld" };
        AddChild(_lobbyAppearanceWorld);
        SpawnLobbyProbePlayer(_lobbyAppearanceWorld, clientA);
    }

    private void SpawnLobbyProbePlayer(GameWorld world, long clientA)
    {
        var spawn = typeof(GameWorld).GetMethod("SpawnPlayer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        spawn?.Invoke(world, [clientA, Vector3.Zero, EmoteCatalogue.None]);
        var player = FindOwnPlayer();
        if (player != null)
        {
            // This is a lobby-only renderer fixture. It must not submit a second network
            // input stream from a path other peers do not have.
            player.SetProcess(false);
            player.SetPhysicsProcess(false);
            player.SetProcessUnhandledInput(false);
        }
        _clientARigBeforeLobbyUpdate = player?.Avatar as BlockmanRig;
    }

    // This must exercise the real renderer: the pure xUnit project intentionally never
    // constructs Godot nodes. It verifies the direct rendering boundary separately from
    // the accepted Lobby update routed through GameWorld above.
    private bool CheckRigAppearanceRendering()
    {
        var rig = new BlockmanRig { Name = "SmokeAppearanceRig" };
        AddChild(rig);
        rig.ApplyAppearance(SmokeAppearance);
        var spawnedAppearance = RigReflects(rig, SmokeAppearance);
        rig.ApplyAppearance(new AvatarAppearance(999, 999, 999, 999, 999));
        var invalidFallback = RigReflects(rig, AvatarAppearance.Default);
        rig.QueueFree();
        return spawnedAppearance && invalidFallback;
    }

    private static bool RigReflects(BlockmanRig rig, AvatarAppearance requested)
    {
        var appearance = CosmeticCatalogue.Normalize(requested);
        if (!MarkerReflects(rig.GetAttachment(AvatarSlot.Head), CosmeticSlot.Head, appearance.HeadId,
                new Vector3(0.42f, 0.12f, 0.42f)) ||
            !MarkerReflects(rig.GetAttachment(AvatarSlot.Back), CosmeticSlot.Back, appearance.BackId,
                new Vector3(0.48f, 0.55f, 0.06f)) ||
            !MarkerReflects(rig.GetAttachment(AvatarSlot.Hips), CosmeticSlot.Hips, appearance.HipsId,
                new Vector3(0.56f, 0.12f, 0.34f)) ||
            !MarkerReflects(rig.GetAttachment(AvatarSlot.HandLeft), CosmeticSlot.Hand, appearance.HandId,
                new Vector3(0.22f, 0.16f, 0.22f)) ||
            !MarkerReflects(rig.GetAttachment(AvatarSlot.HandRight), CosmeticSlot.Hand, appearance.HandId,
                new Vector3(0.22f, 0.16f, 0.22f))) return false;

        var body = CosmeticCatalogue.All.First(entry =>
            entry.Slot == CosmeticSlot.BodyColor && entry.Id == appearance.BodyColorId);
        var expectedColor = new Color(body.Red / 255f, body.Green / 255f, body.Blue / 255f);
        var sawBodyBox = false;
        foreach (var box in rig.FindChildren("*", "MeshInstance3D", true, false))
            if (box is MeshInstance3D mesh && mesh.GetParent()?.Name.ToString().StartsWith("Box_") == true)
            {
                sawBodyBox = true;
                if (mesh.MaterialOverride is not StandardMaterial3D material ||
                    !material.AlbedoColor.IsEqualApprox(expectedColor)) return false;
            }
        return sawBodyBox;
    }

    private static bool MarkerReflects(Node3D marker, CosmeticSlot slot, int id, Vector3 expectedSize)
    {
        if (id == CosmeticCatalogue.None) return marker.GetChildCount() == 0;
        if (marker.GetChildCount() != 1 || marker.GetChild(0) is not MeshInstance3D
            {
                Name: var name,
                Mesh: BoxMesh { Size: var size },
                MaterialOverride: StandardMaterial3D material
            } || name != $"Cosmetic_{slot}_{id}" || !size.IsEqualApprox(expectedSize)) return false;
        var definition = CosmeticCatalogue.All.First(entry => entry.Slot == slot && entry.Id == id);
        return material.AlbedoColor.IsEqualApprox(new Color(definition.Red / 255f, definition.Green / 255f,
            definition.Blue / 255f));
    }

    private static readonly AvatarAppearance SmokeAppearance = new(
        CosmeticCatalogue.GreenBodyId, CosmeticCatalogue.HeadItemId, CosmeticCatalogue.BackItemId,
        CosmeticCatalogue.HipsItemId, CosmeticCatalogue.HandItemId);
    private static readonly AvatarAppearance HostSmokeAppearance = new(
        CosmeticCatalogue.RedBodyId, CosmeticCatalogue.None, CosmeticCatalogue.None,
        CosmeticCatalogue.None, CosmeticCatalogue.None);
    private static readonly AvatarAppearance LobbyUpdatedAppearance = new(
        CosmeticCatalogue.RedBodyId, CosmeticCatalogue.None, CosmeticCatalogue.None,
        CosmeticCatalogue.None, CosmeticCatalogue.None);

    private bool TryMyAppearance(out AvatarAppearance appearance)
    {
        appearance = AvatarAppearance.Default;
        if (!SessionManager.Instance.Players.TryGetValue(Multiplayer.GetUniqueId(), out var data)) return false;
        appearance = PlayerIdentity.FromDictionary(Multiplayer.GetUniqueId(), data).Appearance;
        return true;
    }

    private static bool TryAppearance(long peerId, out AvatarAppearance appearance)
    {
        appearance = AvatarAppearance.Default;
        if (!SessionManager.Instance.Players.TryGetValue(peerId, out var data)) return false;
        appearance = PlayerIdentity.FromDictionary(peerId, data).Appearance;
        return true;
    }
}
