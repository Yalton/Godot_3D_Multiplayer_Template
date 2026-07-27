using Godot;
using MultiplayerTemplate.Network;
using SettingsKit;

namespace MultiplayerTemplate.Game;

public partial class GameWorld : Node3D
{
    private Node3D _playersRoot = null!;

    public override void _Ready()
    {
        _playersRoot = GetNode<Node3D>("Players");
        SessionManager.Instance.MatchStarted += OnMatchStarted;
        SessionManager.Instance.MatchEnded += ClearPlayers;
        Multiplayer.PeerDisconnected += DespawnPlayer;
        Visible = false;
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
        foreach (var child in _playersRoot.GetChildren()) child.QueueFree();
        Visible = false;
    }

    private void DespawnPlayer(long peerId) => _playersRoot.GetNodeOrNull(peerId.ToString())?.QueueFree();

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayer(long peerId, Vector3 position)
    {
        if (_playersRoot.HasNode(peerId.ToString())) return;
        var player = MakePlayer(peerId);
        player.Setup(peerId);
        _playersRoot.AddChild(player);
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
        var material = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHsv(Mathf.PosMod(peerId * 0.19f, 1f), 0.7f, 0.9f)
        };
        var mesh = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.45f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
            MaterialOverride = material
        };
        player.AddChild(mesh);
        var head = new Node3D { Name = "Head", Position = new Vector3(0, 1.55f, 0) };
        player.AddChild(head);
        head.AddChild(new Camera3D { Name = "Camera3D", Fov = SettingsManager.Instance.GetFov() });
        player.AddChild(new Label3D
        {
            Name = "NameLabel",
            Position = new Vector3(0, 2.25f, 0),
            Billboard = Godot.BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 32
        });
        return player;
    }
}
