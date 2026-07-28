# 3D Multiplayer Template

A Godot 4.6 C# starting point for small server-authoritative 3D multiplayer games.
It runs with no third-party addons and supports:

- ENet host and direct-IP join
- LAN server discovery over UDP broadcast
- Bundled GodotSteam lobby host, browser, and join
- Steam overlay invites, friend join requests, cold-launch joins, and lobby privacy
- Unified LAN/Steam player identity with cached avatars and persistent mute/block state
- Lobby roster, ready check, display names, and host-only match start
- Explicit offline/connecting/lobby/loading/playing/results session states
- Build and network-protocol compatibility rejection before registration
- Synchronized threaded match-scene loading with peer readiness and late-join support
- Server-authoritative first-person simulation with prediction, reconciliation, and interpolation
- Positional push-to-talk voice chat (V), with per-player mute integration
- Server-relayed text chat (Enter) in lobby and matches
- Server-validated raycast interactions and grabbable objects (E)
- Host-simulated rigid bodies with 20 Hz snapshot interpolation
- Synchronized monotonic server clock, authoritative 60 Hz tick, deadlines, and named timers
- Rolling ping, jitter, packet-loss, and degraded/timeout connection monitoring
- Lobby quality indicators and an F3 network diagnostics overlay
- Persistent audio, display, mouse sensitivity, and FOV settings
- Runtime keyboard/mouse rebinding with conflict detection and persistence
- Per-module debug logging with an enabled editor dock
- In-game developer console with multiplayer diagnostics and session commands
- Pause/disconnect flow and host-disconnect recovery
- Command-line dedicated hosting

## Quick start

Open `project.godot` in Godot 4.6 and press F6/F5. For a local two-player test, run two
instances: host on one, then direct-connect to `127.0.0.1` from the other. On separate
machines, the host should appear under **LAN Games**; UDP `8911` and game UDP `8910` must
be allowed through the firewall.

Useful command-line modes:

```bash
godot --path . --server --port=8910 --name="Dedicated Server"
godot --path . --join=127.0.0.1 --name="Player Two"
```

Run the four-process multiplayer harness (host, two clients, and one deliberately incompatible
client) with:

```bash
bash Tests/run_multiplayer_smoke.sh
```

## Steam setup

GodotSteam 4.20 and its Steamworks runtime libraries are bundled under `addons/godotsteam`
for Linux, Windows, macOS, and Android ARM64. Set your real App ID in
`Scripts/Network/SteamTransport.cs`; App ID 480 is suitable only for development and shares
its lobby namespace with other games.

Launch the exported game through Steam. Steam controls are disabled with a tooltip when the
Steam client is unavailable or signed out. Public lobbies are tagged
`game=godot-3d-mp-template`, keeping Spacewar test lobbies out of the browser.

Steam hosts can switch a live lobby between Public, Friends Only, and Private/Invite Only.
The invite button opens Steam's friend picker. Both Steam's live `join_requested` callback and
the cold-launch `+connect_lobby <lobbyId>` argument use the same clean session-join path.

The bundled binaries come from the pinned GodotSteam fork used by Sundering (GodotSteam
4.20 / Steamworks SDK 1.64). See `addons/godotsteam/license.md` for its license.

## Architecture

- `Session` owns connection lifecycle, player metadata, lobby RPCs, and transport selection.
- `MatchSceneCoordinator` owns synchronized scene loading and the loading-to-playing gate.
- `NetworkBuildInfo` is the single protocol compatibility version; increment it after RPC changes.
- `PlayerIdentity` is the replicated transport-neutral identity record.
- `PlayerIdentityService` caches Steam avatars and persists local mute/block decisions.
- `SettingsManager` and `InputRemapManager` are reusable C# autoload addons.
- `DebugLogger` provides persistent per-module logging switches and an editor dock.
- `DevConsoleManager` provides a backtick console; use `help` for core and multiplayer commands.
- `EnetTransport.cs` owns direct IP and LAN discovery.
- `SteamTransport.cs` contains all optional GodotSteam calls.
- `Main.cs` is the UI/state shell.
- `GameWorld.cs` owns authoritative player spawn/despawn.
- `NetworkPlayer.cs` is the replaceable example character.
- `TextChatManager.cs` and `PositionalVoiceChat.cs` own session communication.
- `InteractionManager.cs`, `NetworkInteractable.cs`, and `NetworkPhysicsObject.cs` are the
  reusable world-interaction and synchronized-physics seams.
- `NetworkClock.cs` supplies synchronized time, ticks, deadlines, scheduled callbacks, and
  named round/match timers.
- `ConnectionQualityMonitor.cs` derives rolling health metrics from clock probes and publishes
  them to lobby indicators and the F3 overlay.

Game RPCs treat the server as authority. The example player sends input—not transforms—and
uses prediction/reconciliation for responsiveness. Never accept damage, inventory, scores, or
other outcomes directly from clients; accept intent, validate it on the server, then replicate
the result.

See [docs/EXTENDING.md](docs/EXTENDING.md) for the replacement seams and shipping checklist.
