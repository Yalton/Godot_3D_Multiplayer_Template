using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Network;
using SettingsKit;

namespace MultiplayerTemplate.Game;

public partial class GameWorld : Node3D
{
    private readonly System.Collections.Generic.Dictionary<long, NetworkPlayer> _players = new();

    public override void _Ready()
    {
        AddToGroup("game_world");
        SessionManager.Instance.MatchStarted += OnMatchStarted;
        SessionManager.Instance.MatchEnded += ClearPlayers;
        Multiplayer.PeerDisconnected += DespawnPlayer;
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.MatchStarted -= OnMatchStarted;
            SessionManager.Instance.MatchEnded -= ClearPlayers;
        }
        Multiplayer.PeerDisconnected -= DespawnPlayer;
    }

    private void OnMatchStarted()
    {
        Visible = true;
        if (!Multiplayer.IsServer()) return;
        foreach (var peerId in SessionManager.Instance.Players.Keys)
            Rpc(nameof(SpawnPlayer), peerId, SpawnPosition(peerId));
    }

    private void ClearPlayers()
    {
        foreach (var player in _players.Values) player.QueueFree();
        _players.Clear();
        Visible = false;
    }

    private void DespawnPlayer(long peerId)
    {
        if (!_players.Remove(peerId, out var player)) return;
        player.QueueFree();
    }

    public void SpawnForLateJoin(long joiningPeerId)
    {
        if (!Multiplayer.IsServer()) return;
        foreach (var existingPeerId in SessionManager.Instance.Players.Keys)
        {
            if (existingPeerId != joiningPeerId)
                RpcId(joiningPeerId, nameof(SpawnPlayer), existingPeerId, SpawnPosition(existingPeerId));
        }
        Rpc(nameof(SpawnPlayer), joiningPeerId, SpawnPosition(joiningPeerId));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayer(long peerId, Vector3 position)
    {
        if (_players.ContainsKey(peerId)) return;
        var player = MakePlayer(peerId);
        _players[peerId] = player;
        AddChild(player);
        player.GlobalPosition = position;
    }

    private Vector3 SpawnPosition(long peerId)
    {
        var keys = SessionManager.Instance.Players.Keys;
        var index = 0;
        foreach (var key in keys)
        {
            if (key == peerId) break;
            index++;
        }
        var angle = index * Mathf.Tau / Mathf.Max(keys.Count, 1);
        return new Vector3(Mathf.Cos(angle) * 4f, 1f, Mathf.Sin(angle) * 4f);
    }

    private static NetworkPlayer MakePlayer(long peerId)
    {
        var player = new NetworkPlayer();
        var collision = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.45f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0)
        };
        player.AddChild(collision);
        player.AttachAvatar(AvatarFactory.Build(peerId));
        var view = new PlayerCamera { Position = new Vector3(0, 1.55f, 0) };
        player.AddChild(view);
        var nameLabel = new Label3D
        {
            Position = new Vector3(0, 2.25f, 0),
            Billboard = Godot.BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 32
        };
        player.AddChild(nameLabel);
        player.Setup(peerId, view, nameLabel);
        return player;
    }
}
