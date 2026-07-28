using Godot;
using Godot.Collections;
using PlayerRoster = Godot.Collections.Dictionary<long, Godot.Collections.Dictionary>;
using DebugLoggerKit;

namespace MultiplayerTemplate.Network;

public partial class SessionManager : Node
{
    private const string LogModule = "Session";
    private readonly RpcRateLimiter _requestLimiter = new();
    public static SessionManager Instance { get; private set; } = null!;

    public event Action<string, string>? ConnectionStateChanged;
    public event Action<SessionState, SessionState>? StateChanged;
    public event Action<PlayerRoster>? RosterChanged;
    public event Action? MatchStarted;
    public event Action? MatchEnded;
    public event Action<Array<Dictionary>>? LanServersChanged;
    public event Action<Array<Dictionary>>? SteamLobbiesChanged;
    public event Action<bool>? SteamAvailabilityChanged;

    public enum TransportKind { None, Enet, Steam }
    public enum SessionState { Offline, Connecting, Lobby, Loading, Playing, Results, Disconnecting }

    public const int DefaultPort = 8910;
    public TransportKind Transport { get; private set; }
    public PlayerRoster Players { get; private set; } = new();
    public string PlayerName { get; set; } = "Player";
    public string ServerName { get; set; } = "LAN Game";
    public int MaxPlayers { get; set; } = 8;
    public bool MatchActive { get; private set; }
    public bool IsDedicatedServer { get; private set; }
    public SessionState State { get; private set; } = SessionState.Offline;
    // Test harness seam. Production callers should leave this at zero.
    public int ProtocolVersionOverrideForTesting { get; set; }
    public EnetTransport Enet { get; private set; } = null!;
    public SteamTransport Steam { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        DebugLogger.Instance.RegisterModule(LogModule, true);
        DebugLogger.Instance.RegisterModule("RpcSecurity", true);
        Enet = new EnetTransport();
        AddChild(Enet);
        Steam = new SteamTransport();
        AddChild(Steam);
        Enet.LanServersChanged += servers => LanServersChanged?.Invoke(servers);
        Steam.LobbyListReceived += lobbies => SteamLobbiesChanged?.Invoke(lobbies);
        Steam.AvailabilityChanged += available => SteamAvailabilityChanged?.Invoke(available);
        Steam.Failed += OnTransportFailed;
        Steam.LobbyReady += OnSteamHostReady;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        ParseCommandLine();
    }

    public void HostEnet(int port = DefaultPort)
    {
        DebugLogger.Instance.Info(LogModule, $"Starting ENet host on UDP {port}");
        DisconnectSession(false);
        Transport = TransportKind.Enet;
        var error = Enet.Host(port, MaxPlayers);
        if (error != Error.Ok)
        {
            OnTransportFailed($"Could not host UDP {port}: {error}");
            return;
        }
        AddHost();
        SetState(SessionState.Lobby);
        ConnectionStateChanged?.Invoke("lobby", $"Hosting on UDP {port}");
    }

    public void JoinEnet(string address, int port = DefaultPort)
    {
        DebugLogger.Instance.Info(LogModule, $"Joining ENet host {address}:{port}");
        DisconnectSession(false);
        Transport = TransportKind.Enet;
        SetState(SessionState.Connecting);
        ConnectionStateChanged?.Invoke("connecting", $"Connecting to {address}:{port}…");
        var error = Enet.Join(address, port);
        if (error != Error.Ok) OnTransportFailed($"Could not start connection: {error}");
    }

    public void SearchLan() => Enet.SearchLan();
    public void RequestSteamLobbies() => Steam.RequestLobbies();

    public void HostSteam()
    {
        DebugLogger.Instance.Info(LogModule, "Creating Steam lobby");
        DisconnectSession(false);
        Transport = TransportKind.Steam;
        SetState(SessionState.Connecting);
        ConnectionStateChanged?.Invoke("connecting", "Creating Steam lobby…");
        Steam.Host(MaxPlayers, ServerName);
    }

    public void JoinSteamLobby(long lobbyId)
    {
        DebugLogger.Instance.Info(LogModule, $"Joining Steam lobby {lobbyId}");
        DisconnectSession(false);
        Transport = TransportKind.Steam;
        SetState(SessionState.Connecting);
        ConnectionStateChanged?.Invoke("connecting", "Joining Steam lobby…");
        Steam.JoinLobby(lobbyId);
    }

    public void SetReady(bool ready)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (Multiplayer.IsServer()) SetReadyServer(Multiplayer.GetUniqueId(), ready);
        else RpcId(1, nameof(RequestReady), ready);
    }

    public void StartMatch()
    {
        if (!Multiplayer.IsServer() || Players.Count == 0) return;
        foreach (var (id, data) in Players)
        {
            if (id != 1 && !data.GetValueOrDefault("ready", false).AsBool())
            {
                ConnectionStateChanged?.Invoke("lobby", "Every client must be ready.");
                return;
            }
        }
        MatchSceneCoordinator.Instance.BeginMatchLoad();
    }

    public void EndMatch()
    {
        if (!Multiplayer.IsServer()) return;
        MatchSceneCoordinator.Instance.ReturnToLobby();
    }

    public void DisconnectSession(bool emitState = true)
    {
        if (Transport != TransportKind.None)
            DebugLogger.Instance.Info(LogModule, $"Disconnecting {Transport} session");
        SetState(SessionState.Disconnecting);
        MatchActive = false;
        if (Transport == TransportKind.Enet && Enet != null) Enet.StopDiscovery();
        else if (Transport == TransportKind.Steam && Steam != null) Steam.DisconnectTransport();
        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        Players.Clear();
        Transport = TransportKind.None;
        SetState(SessionState.Offline);
        RosterChanged?.Invoke(Players);
        MatchEnded?.Invoke();
        if (emitState) ConnectionStateChanged?.Invoke("menu", "Disconnected.");
    }

    public string DisplayName(long peerId) =>
        Players.TryGetValue(peerId, out var data)
            ? data.GetValueOrDefault("name", $"Player {peerId}").AsString()
            : $"Player {peerId}";

    private void AddHost()
    {
        var steamId = Transport == TransportKind.Steam ? Steam.SteamId : 0;
        var platform = steamId != 0 ? "Steam" : "LAN";
        Players[1] = new PlayerIdentity(1, PlayerName, platform, steamId, true, true).ToDictionary();
        RosterChanged?.Invoke(Players);
    }

    private void OnSteamHostReady()
    {
        AddHost();
        SetState(SessionState.Lobby);
        ConnectionStateChanged?.Invoke("lobby", "Steam lobby ready.");
    }

    private void OnPeerConnected(long peerId)
    {
        DebugLogger.Instance.Info(LogModule, $"Peer connected: {peerId}");
        if (Multiplayer.IsServer())
            ConnectionStateChanged?.Invoke("lobby", $"Peer {peerId} connected.");
    }

    private void OnConnectedToServer()
    {
        var protocol = ProtocolVersionOverrideForTesting != 0
            ? ProtocolVersionOverrideForTesting : NetworkBuildInfo.ProtocolVersion;
        var steamId = Transport == TransportKind.Steam ? Steam.SteamId : 0;
        var platform = steamId != 0 ? "Steam" : OS.GetName();
        RpcId(1, nameof(RegisterPlayer), PlayerName, steamId, platform,
            NetworkBuildInfo.GameVersion, protocol,
            $"{NetworkBuildInfo.GameVersion}-protocol.{protocol}", OS.GetName());
        ConnectionStateChanged?.Invoke("connecting", "Validating session compatibility…");
    }

    private void OnPeerDisconnected(long peerId)
    {
        DebugLogger.Instance.Info(LogModule, $"Peer disconnected: {peerId}");
        if (!Multiplayer.IsServer()) return;
        Players.Remove(peerId);
        Rpc(nameof(SyncRoster), Players);
        RosterChanged?.Invoke(Players);
    }

    private void OnConnectionFailed() => OnTransportFailed("Connection failed or timed out.");

    private void OnServerDisconnected()
    {
        DisconnectSession(false);
        ConnectionStateChanged?.Invoke("menu", "The host ended the session.");
    }

    private void OnTransportFailed(string message)
    {
        DebugLogger.Instance.Error(LogModule, message);
        DisconnectSession(false);
        ConnectionStateChanged?.Invoke("menu", message);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegisterPlayer(
        string requestedName, long steamId, string identityPlatform, string gameVersion,
        int protocolVersion, string buildId, string platform)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RegisterPlayer))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!_requestLimiter.Allow(sender, nameof(RegisterPlayer), 2, 5000))
        {
            RpcSecurity.Reject(nameof(RegisterPlayer), sender, "rate limit exceeded");
            return;
        }
        if (protocolVersion != NetworkBuildInfo.ProtocolVersion || gameVersion != NetworkBuildInfo.GameVersion)
        {
            var reason = $"Incompatible build. Host requires {NetworkBuildInfo.BuildId}; " +
                         $"client sent {buildId} ({platform}).";
            DebugLogger.Instance.Warning(LogModule, $"Rejected peer {sender}: {reason}");
            RpcId(sender, nameof(ConnectionRejected), reason);
            _ = DisconnectRejectedPeer(sender);
            return;
        }
        var safeName = requestedName.Trim();
        if (safeName.Length > 24) safeName = safeName[..24];
        if (safeName.Length == 0) safeName = $"Player {sender}";
        Players[sender] = new PlayerIdentity(
            sender, safeName, identityPlatform, steamId, false, false).ToDictionary();
        Rpc(nameof(SyncRoster), Players);
        RpcId(sender, nameof(ConnectionAccepted), (int)State, Players);
        RosterChanged?.Invoke(Players);
        MatchSceneCoordinator.Instance.HandleLateJoin(sender);
    }

    private async Task DisconnectRejectedPeer(long peerId)
    {
        await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        if (Multiplayer.HasMultiplayerPeer() && Multiplayer.GetPeers().Contains((int)peerId))
            Multiplayer.MultiplayerPeer.DisconnectPeer((int)peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ConnectionAccepted(int serverStateValue, PlayerRoster roster)
    {
        var serverState = (SessionState)serverStateValue;
        Players = roster;
        SetState(serverState);
        RosterChanged?.Invoke(Players);
        ConnectionStateChanged?.Invoke(
            serverState == SessionState.Lobby ? "lobby" : "connecting", "Compatibility confirmed.");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ConnectionRejected(string reason)
    {
        ConnectionStateChanged?.Invoke("menu", reason);
        CallDeferred(nameof(DisconnectSession), false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestReady(bool ready)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestReady)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(RequestReady), out var sender) ||
            !RpcSecurity.RequireState(State, nameof(RequestReady), SessionState.Lobby)) return;
        if (!_requestLimiter.Allow(sender, nameof(RequestReady), 6, 2000))
        {
            RpcSecurity.Reject(nameof(RequestReady), sender, "rate limit exceeded");
            return;
        }
        SetReadyServer(sender, ready);
    }

    private void SetReadyServer(long peerId, bool ready)
    {
        if (!Players.TryGetValue(peerId, out var data)) return;
        data["ready"] = ready;
        Rpc(nameof(SyncRoster), Players);
        RosterChanged?.Invoke(Players);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncRoster(PlayerRoster serverPlayers)
    {
        Players = serverPlayers;
        RosterChanged?.Invoke(Players);
    }

    public void SetStateServer(SessionState state)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(nameof(ApplyState), (int)state);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyState(int state) => SetState((SessionState)state);

    public void ApplyLoadingStateLocally() => SetState(SessionState.Loading);

    public void ActivateMatchLocally()
    {
        SetState(SessionState.Playing);
        MatchStarted?.Invoke();
    }

    public void ReturnToLobbyLocally()
    {
        MatchActive = false;
        MatchEnded?.Invoke();
        SetState(SessionState.Lobby);
        ConnectionStateChanged?.Invoke("lobby", "Returned to lobby.");
    }

    private void SetState(SessionState state)
    {
        if (State == state) return;
        var previous = State;
        State = state;
        MatchActive = state == SessionState.Playing;
        DebugLogger.Instance.Info(LogModule, $"State {previous} -> {state}");
        StateChanged?.Invoke(previous, state);
    }

    private void ParseCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        IsDedicatedServer = DisplayServer.GetName() == "headless" &&
                            args.Contains("--server");
        LoadServerConfiguration(args);
        foreach (var arg in args)
            if (arg.StartsWith("--name=")) PlayerName = arg["--name=".Length..][..Math.Min(24, arg.Length - "--name=".Length)];
        foreach (var arg in args)
        {
            if (arg == "--server") CallDeferred(nameof(HostEnet), ArgumentInt(args, "--port=", DefaultPort));
            else if (arg.StartsWith("--join="))
                CallDeferred(nameof(JoinEnet), arg["--join=".Length..], ArgumentInt(args, "--port=", DefaultPort));
        }
    }

    private void LoadServerConfiguration(string[] args)
    {
        var requestedPath = args.FirstOrDefault(arg => arg.StartsWith("--config="));
        var path = requestedPath == null ? "res://server.cfg" : requestedPath["--config=".Length..];
        var config = new ConfigFile();
        if (config.Load(path) == Error.Ok)
        {
            ServerName = config.GetValue("server", "name", ServerName).AsString();
            MaxPlayers = Math.Clamp(config.GetValue("server", "max_players", MaxPlayers).AsInt32(), 1, 64);
        }
        foreach (var arg in args)
        {
            if (arg.StartsWith("--server-name=")) ServerName = arg["--server-name=".Length..];
            if (arg.StartsWith("--max-players=") &&
                int.TryParse(arg["--max-players=".Length..], out var maximum))
                MaxPlayers = Math.Clamp(maximum, 1, 64);
        }
        if (IsDedicatedServer)
            DebugLogger.Instance.Info(LogModule,
                $"Dedicated server configuration: name='{ServerName}', max={MaxPlayers}");
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        DisconnectSession(false);
        GetTree().Quit();
    }

    private static int ArgumentInt(string[] args, string prefix, int fallback)
    {
        foreach (var arg in args)
            if (arg.StartsWith(prefix) && int.TryParse(arg[prefix.Length..], out var value)) return value;
        return fallback;
    }
}
