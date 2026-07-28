using Godot;
using MultiplayerTemplate.Network;

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

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--smoke-role=")) _role = arg["--smoke-role=".Length..];
            if (arg.StartsWith("--smoke-name=")) _clientName = arg["--smoke-name=".Length..];
        }
        SessionManager.Instance.RosterChanged += OnRoster;
        SessionManager.Instance.StateChanged += (_, current) =>
            _sawPlaying |= current == SessionManager.SessionState.Playing;
        SessionManager.Instance.ConnectionStateChanged += (_, message) =>
            _sawCompatibilityRejection |= message.Contains("Incompatible build");
        NetworkClock.Instance.SampleReceived += sample =>
        {
            _clockSynchronized |= NetworkClock.Instance.IsSynchronized;
            _qualitySampled |= sample.RttMs >= 0;
        };
        if (_role == "server")
        {
            SessionManager.Instance.PlayerName = "SmokeHost";
            SessionManager.Instance.HostEnet(Port);
            FinishAfter(7, FinishServer);
        }
        else if (_role == "client")
        {
            SessionManager.Instance.PlayerName = _clientName;
            SessionManager.Instance.JoinEnet("127.0.0.1", Port);
            FinishAfter(5, FinishClient);
        }
        else if (_role == "incompatible")
        {
            SessionManager.Instance.PlayerName = "WrongProtocol";
            SessionManager.Instance.ProtocolVersionOverrideForTesting =
                NetworkBuildInfo.ProtocolVersion + 1;
            SessionManager.Instance.JoinEnet("127.0.0.1", Port);
            FinishAfter(2, FinishIncompatible);
        }
        else
        {
            GD.PushError("Pass --smoke-role=server or --smoke-role=client");
            GetTree().Quit(2);
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
        var success = _sawThreePeers && _sawPlaying && SessionManager.Instance.Players.Count == 1;
        GD.Print(success
            ? "SMOKE SERVER PASS (3 peers, synchronized load, playing)"
            : $"SMOKE SERVER FAIL roster={SessionManager.Instance.Players}, three={_sawThreePeers}, playing={_sawPlaying}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishClient()
    {
        var loadedScene = GetTree().CurrentScene?.GetNodeOrNull("MatchContainer")?.GetChildCount() == 1;
        var success = _sawThreePeers && _sawPlaying && loadedScene &&
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
}
