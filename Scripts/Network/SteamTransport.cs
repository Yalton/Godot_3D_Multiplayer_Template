using Godot;
using Godot.Collections;
using DebugLoggerKit;

namespace MultiplayerTemplate.Network;

public partial class SteamTransport : Node
{
    private const string LogModule = "Steam";
    public event Action<bool>? AvailabilityChanged;
    public event Action<Array<Dictionary>>? LobbyListReceived;
    public event Action? LobbyReady;
    public event Action<string>? Failed;

    public const int AppId = 480;
    private const string GameKey = "game";
    private const string GameTag = "godot-3d-mp-template";

    private GodotObject? _steam;
    private GodotObject? _steamPeerObject;
    private MultiplayerPeer? _steamPeer;
    private Callable _lobbyCreatedCallable;
    private Callable _lobbyJoinedCallable;
    private Callable _lobbyListCallable;
    private string _pendingLobbyName = "Steam Lobby";
    private bool _pendingHost;

    public bool Available { get; private set; }
    public long ActiveLobbyId { get; private set; }
    public long HostedLobbyId { get; private set; }

    public override void _Ready()
    {
        DebugLogger.Instance.RegisterModule(LogModule, true);
        if (!Engine.HasSingleton("Steam") || !ClassDB.ClassExists("SteamMultiplayerPeer"))
        {
            DebugLogger.Instance.Warning(LogModule, "GodotSteam extension unavailable");
            AvailabilityChanged?.Invoke(false);
            return;
        }
        _steam = Engine.GetSingleton("Steam");
        _lobbyCreatedCallable = Callable.From<long, long>(OnLobbyCreated);
        _lobbyJoinedCallable = Callable.From<long, long, bool, long>(OnLobbyJoined);
        _lobbyListCallable = Callable.From<Array<long>>(OnLobbyMatchList);
        ConnectIfPresent("lobby_created", _lobbyCreatedCallable);
        ConnectIfPresent("lobby_joined", _lobbyJoinedCallable);
        ConnectIfPresent("lobby_match_list", _lobbyListCallable);

        if (_steam.HasMethod("steamInitEx"))
        {
            var result = _steam.Call("steamInitEx", AppId, true);
            if (result.VariantType == Variant.Type.Dictionary)
            {
                var data = result.AsGodotDictionary();
                if (data.ContainsKey("status") && data["status"].AsInt32() != 0)
                {
                    Failed?.Invoke($"Steam initialization failed: {data.GetValueOrDefault("verbal", "unknown")}");
                    return;
                }
            }
        }
        Available = !_steam.HasMethod("loggedOn") || _steam.Call("loggedOn").AsBool();
        DebugLogger.Instance.Info(LogModule, $"Steam initialized; logged on={Available}");
        AvailabilityChanged?.Invoke(Available);
    }

    public override void _Process(double delta)
    {
        if (_steam?.HasMethod("run_callbacks") == true) _steam.Call("run_callbacks");
    }

    public void Host(int maxPlayers, string lobbyName)
    {
        if (!Available) { Failed?.Invoke("Steam is unavailable. Launch through Steam or use LAN."); return; }
        _pendingHost = true;
        _pendingLobbyName = lobbyName;
        _steam!.Call("createLobby", 2, maxPlayers);
    }

    public void RequestLobbies()
    {
        if (!Available) { LobbyListReceived?.Invoke(new Array<Dictionary>()); return; }
        _steam!.Call("addRequestLobbyListDistanceFilter", 3);
        _steam.Call("addRequestLobbyListStringFilter", GameKey, GameTag, 0);
        _steam.Call("addRequestLobbyListResultCountFilter", 50);
        _steam.Call("requestLobbyList");
    }

    public void JoinLobby(long lobbyId)
    {
        if (Available) _steam!.Call("joinLobby", lobbyId);
    }

    public string PersonaName() =>
        Available && _steam?.HasMethod("getPersonaName") == true
            ? _steam.Call("getPersonaName").AsString() : "";

    public void DisconnectTransport()
    {
        if (ActiveLobbyId != 0 && _steam != null)
        {
            if (HostedLobbyId != 0) _steam.Call("setLobbyJoinable", HostedLobbyId, false);
            _steam.Call("leaveLobby", ActiveLobbyId);
        }
        ActiveLobbyId = 0;
        HostedLobbyId = 0;
        if (_steamPeer?.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected)
            _steamPeer?.Close();
        _steamPeer = null;
        _steamPeerObject?.Dispose();
        _steamPeerObject = null;
    }

    public override void _ExitTree()
    {
        DisconnectTransport();
        DisconnectIfPresent("lobby_created", _lobbyCreatedCallable);
        DisconnectIfPresent("lobby_joined", _lobbyJoinedCallable);
        DisconnectIfPresent("lobby_match_list", _lobbyListCallable);
    }

    private void OnLobbyCreated(long status, long lobbyId)
    {
        if (status != 1) { Failed?.Invoke($"Steam lobby creation failed ({status})."); return; }
        HostedLobbyId = ActiveLobbyId = lobbyId;
        _steam!.Call("setLobbyJoinable", lobbyId, true);
        _steam.Call("setLobbyData", lobbyId, "name", _pendingLobbyName);
        _steam.Call("setLobbyData", lobbyId, GameKey, GameTag);
        _steam.Call("setLobbyData", lobbyId, "version",
            ProjectSettings.GetSetting("application/config/version", "dev").AsString());
        if (!CreatePeer()) return;
        _steamPeerObject!.Set("server_relay", true);
        var error = (Error)_steamPeerObject.Call("create_host", 0).AsInt32();
        if (error != Error.Ok) { Failed?.Invoke($"Steam host transport failed: {error}"); return; }
        Multiplayer.MultiplayerPeer = _steamPeer;
        LobbyReady?.Invoke();
    }

    private void OnLobbyJoined(long lobbyId, long permissions, bool locked, long response)
    {
        if (response != 1) { Failed?.Invoke($"Could not join Steam lobby ({response})."); return; }
        ActiveLobbyId = lobbyId;
        var ownerId = _steam!.Call("getLobbyOwner", lobbyId).AsInt64();
        var ownId = _steam.Call("getSteamID").AsInt64();
        if (ownerId == ownId && _pendingHost) return;
        if (!CreatePeer()) return;
        _steamPeerObject!.Set("server_relay", true);
        var error = (Error)_steamPeerObject.Call("create_client", ownerId, 0).AsInt32();
        if (error != Error.Ok) { Failed?.Invoke($"Steam connection failed: {error}"); return; }
        Multiplayer.MultiplayerPeer = _steamPeer;
    }

    private void OnLobbyMatchList(Array<long> ids)
    {
        var result = new Array<Dictionary>();
        foreach (var id in ids)
        {
            result.Add(new Dictionary
            {
                ["id"] = id,
                ["name"] = _steam!.Call("getLobbyData", id, "name").AsString(),
                ["players"] = _steam.Call("getNumLobbyMembers", id).AsInt32(),
                ["max"] = _steam.Call("getLobbyMemberLimit", id).AsInt32()
            });
        }
        LobbyListReceived?.Invoke(result);
    }

    private bool CreatePeer()
    {
        _steamPeerObject?.Dispose();
        _steamPeerObject = ClassDB.Instantiate("SteamMultiplayerPeer").AsGodotObject();
        _steamPeer = _steamPeerObject as MultiplayerPeer;
        if (_steamPeer != null) return true;
        Failed?.Invoke("GodotSteam did not provide a valid SteamMultiplayerPeer.");
        return false;
    }

    private void ConnectIfPresent(StringName signal, Callable callable)
    {
        if (_steam?.HasSignal(signal) == true && !_steam.IsConnected(signal, callable))
            _steam.Connect(signal, callable);
    }

    private void DisconnectIfPresent(StringName signal, Callable callable)
    {
        if (_steam?.IsConnected(signal, callable) == true) _steam.Disconnect(signal, callable);
    }
}
