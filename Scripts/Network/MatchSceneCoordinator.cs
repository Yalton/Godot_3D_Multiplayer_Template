using Godot;
using DebugLoggerKit;
using MultiplayerTemplate.Game;

namespace MultiplayerTemplate.Network;

public partial class MatchSceneCoordinator : Node
{
    public static MatchSceneCoordinator Instance { get; private set; } = null!;

    private const string LogModule = "SceneSync";
    public const string DefaultMatchScene = "res://scenes/gameplay/example_arena.tscn";

    private readonly HashSet<long> _waitingFor = new();
    private string _activeScenePath = "";
    private int _loadGeneration;
    private Node? _activeScene;
    private Node? _matchContainer;

    public int LoadedSceneCount => _matchContainer?.GetChildCount() ?? 0;

    public override void _Ready()
    {
        Instance = this;
        DebugLogger.Instance.RegisterModule(LogModule, true);
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    public override void _ExitTree() => Multiplayer.PeerDisconnected -= OnPeerDisconnected;

    public void SetMatchContainer(Node matchContainer) => _matchContainer = matchContainer;

    public void BeginMatchLoad(string scenePath = DefaultMatchScene)
    {
        var session = SessionManager.Instance;
        if (!Multiplayer.IsServer() || session.State != SessionManager.SessionState.Lobby) return;
        _activeScenePath = scenePath;
        _loadGeneration++;
        _waitingFor.Clear();
        foreach (var peerId in session.Players.Keys) _waitingFor.Add(peerId);
        session.SetStateServer(SessionManager.SessionState.Loading);
        DebugLogger.Instance.Info(LogModule,
            $"Loading {_activeScenePath}; waiting for {_waitingFor.Count} peers");
        Rpc(nameof(PrepareMatchScene), _activeScenePath, _loadGeneration);
    }

    public void HandleLateJoin(long peerId)
    {
        if (!Multiplayer.IsServer() || string.IsNullOrEmpty(_activeScenePath)) return;
        if (SessionManager.Instance.State is not
            (SessionManager.SessionState.Loading or SessionManager.SessionState.Playing)) return;
        _waitingFor.Add(peerId);
        RpcId(peerId, nameof(PrepareMatchScene), _activeScenePath, _loadGeneration);
    }

    public void ReturnToLobby()
    {
        if (!Multiplayer.IsServer()) return;
        _activeScenePath = "";
        _waitingFor.Clear();
        Rpc(nameof(UnloadMatchScene));
        SessionManager.Instance.ReturnToLobbyLocally();
        SessionManager.Instance.SetStateServer(SessionManager.SessionState.Lobby);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void PrepareMatchScene(string scenePath, int generation)
    {
        if (generation < _loadGeneration) return;
        _loadGeneration = generation;
        _activeScenePath = scenePath;
        SessionManager.Instance.ApplyLoadingStateLocally();
        UnloadLocalScene();

        var error = ResourceLoader.LoadThreadedRequest(scenePath);
        if (error != Error.Ok)
        {
            GD.PushError($"Could not begin loading multiplayer scene {scenePath}: {error}");
            return;
        }

        var progress = new Godot.Collections.Array();
        while (ResourceLoader.LoadThreadedGetStatus(scenePath, progress) ==
               ResourceLoader.ThreadLoadStatus.InProgress)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (ResourceLoader.LoadThreadedGetStatus(scenePath) != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            GD.PushError($"Multiplayer scene failed to load: {scenePath}");
            return;
        }

        var packed = ResourceLoader.LoadThreadedGet(scenePath) as PackedScene;
        _activeScene = packed?.Instantiate();
        if (_activeScene == null || _matchContainer == null)
        {
            GD.PushError("Loaded match scene or MatchContainer is missing.");
            return;
        }
        _matchContainer.AddChild(_activeScene);
        if (Multiplayer.IsServer()) MarkPeerLoaded(Multiplayer.GetUniqueId(), generation);
        else RpcId(1, nameof(ReportSceneLoaded), generation);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReportSceneLoaded(int generation)
    {
        if (!RpcSecurity.RequireServer(this, nameof(ReportSceneLoaded)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(ReportSceneLoaded), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(ReportSceneLoaded),
                SessionManager.SessionState.Loading, SessionManager.SessionState.Playing) ||
            generation != _loadGeneration) return;
        MarkPeerLoaded(sender, generation);
    }

    private void MarkPeerLoaded(long peerId, int generation)
    {
        _waitingFor.Remove(peerId);
        DebugLogger.Instance.Info(LogModule,
            $"Peer {peerId} loaded generation {generation}; {_waitingFor.Count} remaining");
        if (_waitingFor.Count != 0) return;
        if (SessionManager.Instance.State == SessionManager.SessionState.Loading)
        {
            Rpc(nameof(ActivateMatch), generation);
            SessionManager.Instance.SetStateServer(SessionManager.SessionState.Playing);
        }
        else if (SessionManager.Instance.State == SessionManager.SessionState.Playing)
        {
            RpcId(peerId, nameof(ActivateMatch), generation);
            var world = GetTree().GetFirstNodeInGroup("game_world") as GameWorld;
            world?.SpawnForLateJoin(peerId);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!Multiplayer.IsServer() || !_waitingFor.Remove(peerId)) return;
        DebugLogger.Instance.Info(LogModule,
            $"Peer {peerId} disconnected during load; {_waitingFor.Count} remaining");
        if (_waitingFor.Count == 0 &&
            SessionManager.Instance.State == SessionManager.SessionState.Loading)
        {
            Rpc(nameof(ActivateMatch), _loadGeneration);
            SessionManager.Instance.SetStateServer(SessionManager.SessionState.Playing);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ActivateMatch(int generation)
    {
        if (generation != _loadGeneration) return;
        SessionManager.Instance.ActivateMatchLocally();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UnloadMatchScene() => UnloadLocalScene();

    private void UnloadLocalScene()
    {
        if (_activeScene != null && GodotObject.IsInstanceValid(_activeScene))
            _activeScene.QueueFree();
        _activeScene = null;
    }
}
