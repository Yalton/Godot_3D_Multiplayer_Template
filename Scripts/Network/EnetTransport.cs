using Godot;
using Godot.Collections;
using DebugLoggerKit;

namespace MultiplayerTemplate.Network;

public partial class EnetTransport : Node
{
    private const string LogModule = "ENet";
    public event Action<Array<Dictionary>>? LanServersChanged;

    public const int DefaultPort = 8910;
    public const int DiscoveryPort = 8911;
    private const string Magic = "GODOT_3D_MP_TEMPLATE_V1";

    private readonly PacketPeerUdp _listener = new();
    private readonly PacketPeerUdp _broadcaster = new();
    private readonly System.Collections.Generic.Dictionary<string, Dictionary> _servers = new();
    private bool _searching;
    private bool _advertising;
    private double _advertiseAccumulator;
    private int _gamePort = DefaultPort;

    public Error Host(int port, int maxPlayers)
    {
        DebugLogger.Instance.RegisterModule(LogModule, true);
        _gamePort = port;
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, maxPlayers);
        if (error == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
            StartAdvertising();
        }
        else DebugLogger.Instance.Error(LogModule, $"CreateServer failed: {error}");
        return error;
    }

    public Error Join(string address, int port)
    {
        DebugLogger.Instance.RegisterModule(LogModule, true);
        StopDiscovery();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address.Trim(), port);
        if (error == Error.Ok) Multiplayer.MultiplayerPeer = peer;
        return error;
    }

    public void SearchLan()
    {
        DebugLogger.Instance.Info(LogModule, $"Searching for LAN games on UDP {DiscoveryPort}");
        _servers.Clear();
        _searching = true;
        _listener.Close();
        var error = _listener.Bind(DiscoveryPort, "*");
        if (error != Error.Ok)
            GD.PushWarning($"LAN discovery could not bind UDP {DiscoveryPort}: {error}");
        SetProcess(true);
    }

    public void StopDiscovery()
    {
        _searching = false;
        _advertising = false;
        _listener.Close();
        _broadcaster.Close();
        _servers.Clear();
    }

    private void StartAdvertising()
    {
        _advertising = true;
        _broadcaster.Close();
        _broadcaster.SetBroadcastEnabled(true);
        _broadcaster.Bind(0, "*");
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_advertising)
        {
            _advertiseAccumulator += delta;
            if (_advertiseAccumulator >= 1.0)
            {
                _advertiseAccumulator = 0;
                var session = SessionManager.Instance;
                var safeName = session.ServerName.Replace("|", "");
                var payload = $"{Magic}|{safeName}|{_gamePort}|{session.Players.Count}|{session.MaxPlayers}";
                _broadcaster.SetDestAddress("255.255.255.255", DiscoveryPort);
                _broadcaster.PutPacket(payload.ToUtf8Buffer());
            }
        }
        if (_searching) PollDiscovery();
    }

    private void PollDiscovery()
    {
        var changed = false;
        while (_listener.GetAvailablePacketCount() > 0)
        {
            var payload = _listener.GetPacket().GetStringFromUtf8().Split('|');
            if (payload.Length != 5 || payload[0] != Magic) continue;
            var address = _listener.GetPacketIP();
            var port = int.TryParse(payload[2], out var parsedPort) ? parsedPort : DefaultPort;
            var key = $"{address}:{port}";
            _servers[key] = new Dictionary
            {
                ["name"] = payload[1], ["address"] = address, ["port"] = port,
                ["players"] = int.TryParse(payload[3], out var count) ? count : 0,
                ["max"] = int.TryParse(payload[4], out var max) ? max : 0,
                ["seen"] = (long)Time.GetTicksMsec()
            };
            changed = true;
        }
        var now = (long)Time.GetTicksMsec();
        foreach (var key in _servers.Keys.ToArray())
        {
            if (now - (long)_servers[key]["seen"] <= 3500) continue;
            _servers.Remove(key);
            changed = true;
        }
        if (!changed) return;
        var result = new Array<Dictionary>();
        foreach (var server in _servers.Values) result.Add(server);
        LanServersChanged?.Invoke(result);
    }
}
