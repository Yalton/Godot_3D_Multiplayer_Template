using DevConsoleKit;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.DebugTools;

public partial class TemplateCommands : BaseCommandHandler
{
    public override void _Ready()
    {
        _handlerName = "TemplateCommands";
        _commandCategory = "Multiplayer";
        base._Ready();
    }

    protected override void RegisterCommands()
    {
        RegisterCommand("net_status", NetStatus, "Shows transport, peer ID, role, and match state.");
        RegisterCommand("players", Players, "Lists the current network roster.");
        RegisterCommand("host_lan", HostLan, "Hosts an ENet session. Usage: host_lan [port]");
        RegisterCommand("join_ip", JoinIp, "Joins an ENet host. Usage: join_ip <address> [port]");
        RegisterCommand("disconnect", Disconnect, "Disconnects the current multiplayer session.");
        RegisterCommand("net_clock", NetClock, "Shows synchronized time, server tick, ping, jitter, and loss.");
    }

    private void NetStatus(string[] args)
    {
        var session = SessionManager.Instance;
        var peer = session.Multiplayer;
        OutputSystem("=== Network Status ===");
        Output($"Transport: {session.Transport}");
        Output($"Connected: {peer.HasMultiplayerPeer()}");
        Output($"Peer ID: {(peer.HasMultiplayerPeer() ? peer.GetUniqueId() : 0)}");
        Output($"Role: {(peer.HasMultiplayerPeer() && peer.IsServer() ? "Host" : "Client")}");
        Output($"Players: {session.Players.Count}/{session.MaxPlayers}");
        Output($"Match active: {session.MatchActive}");
    }

    private void Players(string[] args)
    {
        if (SessionManager.Instance.Players.Count == 0)
        {
            OutputWarning("No active roster.");
            return;
        }
        foreach (var (id, data) in SessionManager.Instance.Players)
            Output($"{id}: {data.GetValueOrDefault("name", "Player").AsString()} " +
                   $"[{(data.GetValueOrDefault("ready", false).AsBool() ? "ready" : "not ready")}]");
    }

    private void NetClock(string[] args)
    {
        var clock = NetworkClock.Instance;
        var quality = ConnectionQualityMonitor.Instance.LocalQuality;
        OutputSystem("=== Network Clock & Quality ===");
        Output($"Synchronized: {clock.IsSynchronized}");
        Output($"Server time: {clock.ServerTimeSeconds:F3}s");
        Output($"Authoritative tick: {clock.AuthoritativeTick} @ {NetworkClock.TickRate} Hz");
        Output($"Ping: {quality.PingMs:F1} ms");
        Output($"Jitter: {quality.JitterMs:F1} ms");
        Output($"Packet loss: {quality.PacketLossPercent:F1}%");
        Output($"Health: {quality.Health}");
    }

    private void HostLan(string[] args)
    {
        var port = SessionManager.DefaultPort;
        if (args.Length > 0 && !TryParseInt(args[0], out port, "port")) return;
        SessionManager.Instance.HostEnet(port);
        OutputSystem($"Hosting ENet on UDP {port}.");
    }

    private void JoinIp(string[] args)
    {
        if (!ValidateMinArgCount(args, 1, "join_ip <address> [port]")) return;
        var port = SessionManager.DefaultPort;
        if (args.Length > 1 && !TryParseInt(args[1], out port, "port")) return;
        SessionManager.Instance.JoinEnet(args[0], port);
        OutputSystem($"Connecting to {args[0]}:{port}.");
    }

    private void Disconnect(string[] args)
    {
        SessionManager.Instance.DisconnectSession();
        OutputSystem("Disconnected.");
    }
}
