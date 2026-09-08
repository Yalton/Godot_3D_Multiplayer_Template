using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Network;
using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Game;

public partial class GameWorld : Node3D
{
    /// <summary>
    /// Optional replacement for the whole player node (not just the character body).
    /// The scene root must be a NetworkPlayer. If a PlayerCamera or PlayerNameplate is
    /// not found among its descendants, one is constructed at the same local offsets as
    /// the built-in rig. If no AvatarRig descendant is found, the configured (or
    /// built-in) avatar is attached automatically. Leave unset to use the built-in rig.
    /// </summary>
    [Export] public PackedScene? PlayerScene { get; set; }

    private readonly System.Collections.Generic.Dictionary<long, NetworkPlayer> _players = new();

    public IReadOnlyDictionary<long, VoicePoint> VoicePoints()
    {
        var points = new Dictionary<long, VoicePoint>();
        foreach (var (peerId, player) in _players)
        {
            var position = player.GlobalPosition;
            points[peerId] = new VoicePoint(position.X, position.Y, position.Z);
        }
        return points;
    }

    public override void _Ready()
    {
        AddToGroup("game_world");
        SessionManager.Instance.MatchStarted += OnMatchStarted;
        SessionManager.Instance.MatchEnded += ClearPlayers;
        SessionManager.Instance.RosterChanged += OnRosterChanged;
        Multiplayer.PeerDisconnected += DespawnPlayer;
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.MatchStarted -= OnMatchStarted;
            SessionManager.Instance.MatchEnded -= ClearPlayers;
            SessionManager.Instance.RosterChanged -= OnRosterChanged;
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
            Rpc(nameof(SpawnPlayer), peerId, SpawnPosition(peerId), Avatar.EmoteCatalogue.None);
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
            {
                var existingEmote = _players.TryGetValue(existingPeerId, out var existingPlayer)
                    ? existingPlayer.ActiveEmote
                    : Avatar.EmoteCatalogue.None;
                RpcId(joiningPeerId, nameof(SpawnPlayer), existingPeerId, SpawnPosition(existingPeerId), existingEmote);
            }
        }
        Rpc(nameof(SpawnPlayer), joiningPeerId, SpawnPosition(joiningPeerId), Avatar.EmoteCatalogue.None);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayer(long peerId, Vector3 position, int activeEmote)
    {
        if (_players.ContainsKey(peerId)) return;
        var player = MakePlayer(peerId);
        _players[peerId] = player;
        AddChild(player);
        player.GlobalPosition = position;
        ApplyRosterAppearance(peerId);
        if (activeEmote != Avatar.EmoteCatalogue.None) player.PlayEmoteLocally(activeEmote);
    }

    private void OnRosterChanged(Godot.Collections.Dictionary<long, Godot.Collections.Dictionary> roster)
    {
        foreach (var peerId in roster.Keys) ApplyRosterAppearance(peerId);
    }

    private void ApplyRosterAppearance(long peerId)
    {
        if (!_players.TryGetValue(peerId, out var player) || player.Avatar is not { } rig ||
            !SessionManager.Instance.Players.TryGetValue(peerId, out var data)) return;
        var appearance = PlayerIdentity.FromDictionary(peerId, data).Appearance;
        if (rig.IsNodeReady()) rig.ApplyAppearance(appearance);
        else rig.Ready += () => rig.ApplyAppearance(appearance);
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
            if (instance is NetworkPlayer custom)
            {
                var customView = FindDescendant<PlayerCamera>(custom);
                if (customView == null)
                {
                    customView = new PlayerCamera { Position = new Vector3(0, 1.55f, 0) };
                    custom.AddChild(customView);
                }
                var customNameLabel = FindDescendant<PlayerNameplate>(custom);
                if (customNameLabel == null)
                {
                    customNameLabel = new PlayerNameplate { Position = new Vector3(0, 2.25f, 0) };
                    custom.AddChild(customNameLabel);
                }
                custom.AttachAvatar(FindDescendant<AvatarRig>(custom) ?? AvatarFactory.Build(peerId));
                custom.Setup(peerId, customView, customNameLabel);
                return custom;
            }
            GD.PushError("PlayerScene needs a NetworkPlayer root; falling back to the built-in rig.");
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
        player.AttachAvatar(AvatarFactory.Build(peerId));
        var view = new PlayerCamera { Position = new Vector3(0, 1.55f, 0) };
        player.AddChild(view);
        var nameLabel = new PlayerNameplate { Position = new Vector3(0, 2.25f, 0) };
        player.AddChild(nameLabel);
        player.Setup(peerId, view, nameLabel);
        return player;
    }

    // Recursive lookup used to adapt a custom PlayerScene: it may already provide its
    // own PlayerCamera / PlayerNameplate / AvatarRig somewhere under the root, in which
    // case we must reuse it rather than build a duplicate.
    private static T? FindDescendant<T>(Node root) where T : class
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
