using Godot;
using DebugLoggerKit;
using MultiplayerTemplate.Game;
using MultiplayerTemplate.Interaction;

namespace MultiplayerTemplate.Network;

public partial class MatchSceneCoordinator : Node
{
    public static MatchSceneCoordinator Instance { get; private set; } = null!;

    private const string LogModule = "SceneSync";
    public const string DefaultMatchScene = "res://scenes/gameplay/example_arena.tscn";
    private const double LoadTimeoutSeconds = 30;

    private readonly HashSet<long> _waitingFor = new();
    private string _activeScenePath = "";
    private int _loadGeneration;
    // Local monotonic counter that invalidates suspended load coroutines. Distinct from
    // _loadGeneration, which is server-driven and resets when joining a new session.
    private int _loadEpoch;
    private Node? _activeScene;
    private Node? _matchContainer;

    public int LoadedSceneCount => _matchContainer?.GetChildCount() ?? 0;

    // Meaningful on the server only: _waitingFor is never populated on clients, so this
    // would report every peer as "ready" there. Match-scene RPC senders use this to avoid
    // targeting a peer that has not yet mounted the scene and so has no such node.
    // A peer appears in Multiplayer.GetPeers() as soon as its transport connects, but its
    // RegisterPlayer RPC (which adds it to SessionManager.Players and calls HandleLateJoin
    // in the same call) may not have arrived yet. A peer absent from the roster is
    // definitionally holding no scene, so it must not read as ready either.
    // HandleLateJoin only adds the peer to _waitingFor during Loading/Playing; it is a
    // no-op during Lobby, so a peer registered while the session is in Lobby reads as
    // ready with no scene mounted. Harmless today only because no match-scene RPC sender
    // runs during Lobby.
    public bool IsSceneReady(long peerId) =>
        SessionManager.Instance.Players.ContainsKey(peerId) && !_waitingFor.Contains(peerId);

    public override void _Ready()
    {
        Instance = this;
        DebugLogger.Instance.RegisterModule(LogModule, true);
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        SessionManager.Instance.StateChanged += OnSessionStateChanged;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        SessionManager.Instance.StateChanged -= OnSessionStateChanged;
    }

    // Leaving a session must tear down the mounted match scene: nothing else does, and
    // bumping the epoch also cancels any load coroutine still in flight.
    private void OnSessionStateChanged(SessionManager.SessionState previous, SessionManager.SessionState current)
    {
        if (current != SessionManager.SessionState.Offline) return;
        _loadEpoch++;
        _loadGeneration = 0;
        _activeScenePath = "";
        _waitingFor.Clear();
        UnloadLocalScene();
    }

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
        _ = KickStalledLoaders(_loadGeneration);
    }

    // A client that stalls or errors while loading would otherwise block match start for
    // everyone, forever — it has no failure-report path. The host itself is exempt: it
    // cannot kick itself, and its own load failure is visible locally.
    private async Task KickStalledLoaders(int generation)
    {
        await ToSignal(GetTree().CreateTimer(LoadTimeoutSeconds), SceneTreeTimer.SignalName.Timeout);
        if (!Multiplayer.IsServer() || generation != _loadGeneration ||
            SessionManager.Instance.State != SessionManager.SessionState.Loading) return;
        foreach (var peerId in _waitingFor.ToArray())
        {
            if (peerId == 1) continue;
            DebugLogger.Instance.Warning(LogModule,
                $"Peer {peerId} did not load within {LoadTimeoutSeconds}s; disconnecting");
            Multiplayer.MultiplayerPeer?.DisconnectPeer((int)peerId);
        }
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
        _waitingFor.Clear();
        Rpc(nameof(UnloadMatchScene));
        SessionManager.Instance.SetStateServer(SessionManager.SessionState.Lobby);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void PrepareMatchScene(string scenePath, int generation)
    {
        if (generation < _loadGeneration) return;
        _loadGeneration = generation;
        _activeScenePath = scenePath;
        var epoch = ++_loadEpoch;
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

        // The session may have moved on while this coroutine was suspended (return to
        // lobby, disconnect, or a newer load). Mounting the stale scene would leave it
        // on top of whatever state the session is in now.
        if (epoch != _loadEpoch) return;

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
            !RpcSecurity.RequireState(SessionManager.Instance.State, sender, nameof(ReportSceneLoaded),
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
            SyncWorldStateTo(peerId);
        }
    }

    // Interactable and physics state propagates through one-shot RPCs, so a late joiner
    // starts from scene defaults and must be brought up to date explicitly.
    private void SyncWorldStateTo(long peerId)
    {
        foreach (var node in GetTree().GetNodesInGroup("interactable"))
            if (node is NetworkInteractable interactable) interactable.SyncStateTo(peerId);
        foreach (var node in GetTree().GetNodesInGroup("network_physics"))
            if (node is NetworkPhysicsObject physics) physics.SyncStateTo(peerId);
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
    private void UnloadMatchScene()
    {
        // Bumping the epoch cancels any load coroutine still in flight so it cannot
        // mount its scene after we have returned to the lobby.
        _loadEpoch++;
        _activeScenePath = "";
        UnloadLocalScene();
        SessionManager.Instance.ReturnToLobbyLocally();
    }

    private void UnloadLocalScene()
    {
        if (_activeScene != null && GodotObject.IsInstanceValid(_activeScene))
            _activeScene.QueueFree();
        _activeScene = null;
    }
}
