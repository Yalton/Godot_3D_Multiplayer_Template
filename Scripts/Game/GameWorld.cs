using Godot;
using MultiplayerTemplate.Network;
using SettingsKit;

namespace MultiplayerTemplate.Game;

public partial class GameWorld : Node3D
{
    /// <summary>
    /// Optional replacement for the built-in capsule rig. The scene root must be a
    /// NetworkPlayer with children "Head" (Node3D), "Head/Camera3D", and "NameLabel"
    /// (Label3D). Leave unset to use the built-in rig.
    /// </summary>
    [Export] public PackedScene? PlayerScene { get; set; }

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
        {
            if (SkipSpawn(peerId)) continue;
            Rpc(nameof(SpawnPlayer), peerId, SpawnPosition(peerId));
        }
    }

    // A headless dedicated host stays in the roster but has no one at the keyboard;
    // spawning it would leave a motionless capsule in every match.
    private static bool SkipSpawn(long peerId) =>
        peerId == 1 && SessionManager.Instance.IsDedicatedServer;

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
            if (existingPeerId != joiningPeerId && !SkipSpawn(existingPeerId))
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

    private NetworkPlayer MakePlayer(long peerId)
    {
        if (PlayerScene != null)
        {
            var instance = PlayerScene.Instantiate();
            if (instance is NetworkPlayer custom &&
                custom.GetNodeOrNull<Node3D>("Head") is { } customHead &&
                customHead.GetNodeOrNull<Camera3D>("Camera3D") is { } customCamera &&
                custom.GetNodeOrNull<Label3D>("NameLabel") is { } customLabel)
            {
                custom.Setup(peerId, customHead, customCamera, customLabel);
                return custom;
            }
            GD.PushError("PlayerScene needs a NetworkPlayer root with Head/Camera3D and " +
                         "NameLabel children; falling back to the built-in rig.");
            instance.QueueFree();
        }
        var player = new NetworkPlayer
        {
            // Match the named layers in project.godot: live on "Players" (2), collide
            // with "World" (1), other players (2), and "Interactables" (4). The default
            // layer/mask of 1 walks straight through every crate and switch.
            CollisionLayer = 0b010,
            CollisionMask = 0b111
        };
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
        var camera = new Camera3D { Fov = SettingsManager.Instance.GetFov() };
        head.AddChild(camera);
        var nameLabel = new Label3D
        {
            Position = new Vector3(0, 2.25f, 0),
            Billboard = Godot.BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 32
        };
        player.AddChild(nameLabel);
        player.Setup(peerId, head, camera, nameLabel);
        return player;
    }
}
