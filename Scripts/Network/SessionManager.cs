using Godot;
using Godot.Collections;
using GDictionary = Godot.Collections.Dictionary;
using PlayerRoster = Godot.Collections.Dictionary<long, Godot.Collections.Dictionary>;
using DebugLoggerKit;

namespace MultiplayerTemplate.Network;

public partial class SessionManager : Node
{
    private const string LogModule = "Session";
    public static SessionManager Instance { get; private set; } = null!;

    public event Action<string, string>? ConnectionStateChanged;
    public event Action<PlayerRoster>? RosterChanged;
    public event Action? MatchStarted;
    public event Action? MatchEnded;
    public event Action<Array<Dictionary>>? LanServersChanged;
    public event Action<Array<Dictionary>>? SteamLobbiesChanged;
    public event Action<bool>? SteamAvailabilityChanged;

    public enum TransportKind { None, Enet, Steam }

    public const int DefaultPort = 8910;
    public TransportKind Transport { get; private set; }
    public PlayerRoster Players { get; private set; } = new();
    public string PlayerName { get; set; } = "Player";
    public string ServerName { get; set; } = "LAN Game";
    public int MaxPlayers { get; set; } = 8;
    public bool MatchActive { get; private set; }
    public EnetTransport Enet { get; private set; } = null!;
    public SteamTransport Steam { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        DebugLogger.Instance.RegisterModule(LogModule, true);
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
        ConnectionStateChanged?.Invoke("lobby", $"Hosting on UDP {port}");
    }

    public void JoinEnet(string address, int port = DefaultPort)
    {
        DebugLogger.Instance.Info(LogModule, $"Joining ENet host {address}:{port}");
        DisconnectSession(false);
        Transport = TransportKind.Enet;
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
        ConnectionStateChanged?.Invoke("connecting", "Creating Steam lobby…");
        Steam.Host(MaxPlayers, ServerName);
    }

    public void JoinSteamLobby(long lobbyId)
    {
        DebugLogger.Instance.Info(LogModule, $"Joining Steam lobby {lobbyId}");
        DisconnectSession(false);
        Transport = TransportKind.Steam;
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
        MatchActive = true;
        Rpc(nameof(BeginMatch));
    }

    public void EndMatch()
    {
        if (!Multiplayer.IsServer()) return;
        MatchActive = false;
        Rpc(nameof(EndMatchRemote));
    }

    public void DisconnectSession(bool emitState = true)
    {
        if (Transport != TransportKind.None)
            DebugLogger.Instance.Info(LogModule, $"Disconnecting {Transport} session");
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
        Players[1] = PlayerRecord(PlayerName, true);
        RosterChanged?.Invoke(Players);
    }

    private void OnSteamHostReady()
    {
        AddHost();
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
        RpcId(1, nameof(RegisterPlayer), PlayerName);
        ConnectionStateChanged?.Invoke("lobby", "Connected.");
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
    private void RegisterPlayer(string requestedName)
    {
        if (!Multiplayer.IsServer()) return;
        var sender = Multiplayer.GetRemoteSenderId();
        var safeName = requestedName.Trim();
        if (safeName.Length > 24) safeName = safeName[..24];
        if (safeName.Length == 0) safeName = $"Player {sender}";
        Players[sender] = PlayerRecord(safeName, false);
        Rpc(nameof(SyncRoster), Players);
        RosterChanged?.Invoke(Players);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestReady(bool ready)
    {
        if (Multiplayer.IsServer()) SetReadyServer(Multiplayer.GetRemoteSenderId(), ready);
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

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMatch()
    {
        MatchActive = true;
        MatchStarted?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EndMatchRemote()
    {
        MatchActive = false;
        MatchEnded?.Invoke();
        ConnectionStateChanged?.Invoke("lobby", "Returned to lobby.");
    }

    private static GDictionary PlayerRecord(string name, bool ready) =>
        new() { ["name"] = name, ["ready"] = ready };

    private void ParseCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (var arg in args)
            if (arg.StartsWith("--name=")) PlayerName = arg["--name=".Length..][..Math.Min(24, arg.Length - "--name=".Length)];
        foreach (var arg in args)
        {
            if (arg == "--server") CallDeferred(nameof(HostEnet), ArgumentInt(args, "--port=", DefaultPort));
            else if (arg.StartsWith("--join="))
                CallDeferred(nameof(JoinEnet), arg["--join=".Length..], ArgumentInt(args, "--port=", DefaultPort));
        }
    }

    private static int ArgumentInt(string[] args, string prefix, int fallback)
    {
        foreach (var arg in args)
            if (arg.StartsWith(prefix) && int.TryParse(arg[prefix.Length..], out var value)) return value;
        return fallback;
    }
}
