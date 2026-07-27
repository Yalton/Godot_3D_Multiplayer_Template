using Godot;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public partial class NetworkSmoke : Node
{
    private const int Port = 18910;
    private string _role = "";
    private bool _connectedRoster;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--smoke-role=")) _role = arg["--smoke-role=".Length..];
        if (_role == "server")
        {
            SessionManager.Instance.PlayerName = "SmokeHost";
            SessionManager.Instance.HostEnet(Port);
            FinishAfter(4, FinishServer);
        }
        else if (_role == "client")
        {
            SessionManager.Instance.PlayerName = "SmokeClient";
            SessionManager.Instance.RosterChanged += players => _connectedRoster |= players.Count == 2;
            SessionManager.Instance.JoinEnet("127.0.0.1", Port);
            FinishAfter(3, FinishClient);
        }
        else
        {
            GD.PushError("Pass --smoke-role=server or --smoke-role=client");
            GetTree().Quit(2);
        }
    }

    private async void FinishAfter(double seconds, Action callback)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        callback();
    }

    private void FinishServer()
    {
        var success = SessionManager.Instance.Players.Count == 1;
        GD.Print(success ? "SMOKE SERVER PASS" : $"SMOKE SERVER FAIL roster={SessionManager.Instance.Players}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishClient()
    {
        GD.Print(_connectedRoster ? "SMOKE CLIENT PASS" : $"SMOKE CLIENT FAIL roster={SessionManager.Instance.Players}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(_connectedRoster ? 0 : 1);
    }
}
