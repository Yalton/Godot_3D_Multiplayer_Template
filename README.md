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
- Swappable code-built avatar rig with procedural locomotion animation
- First/third-person camera toggle (C) with a head-anchored aim origin
- Positional push-to-talk voice chat (V), with per-player mute integration
- Server-relayed text chat (Enter) in lobby and matches
- In-match player list (Tab) with ping, speaking, mute, block, per-player volume, and host kick
- Server-authoritative AFK detection with an optional dedicated-server idle kick
- Server-validated raycast interactions and grabbable objects (E)
- Held emote wheel (B) with one-shot and looping emotes, cancelled by movement
- Held quick-chat wheel (Z) sending canned lines by id, never free text
- Host-simulated rigid bodies with 20 Hz snapshot interpolation
- Synchronized monotonic server clock, authoritative 60 Hz tick, deadlines, and named timers
- Rolling ping, jitter, packet-loss, and degraded/timeout connection monitoring
- Lobby quality indicators and an F3 network diagnostics overlay
- Persistent audio, display, mouse sensitivity, and FOV settings
- Runtime keyboard/mouse rebinding with conflict detection and persistence
- Per-module debug logging with an enabled editor dock
- In-game developer console with multiplayer diagnostics and session commands
- Pause/disconnect flow and host-disconnect recovery
- Headless-safe dedicated hosting with config/argument overrides
- Reusable sender, state, distance, line-of-sight, cooldown, and RPC rate-limit helpers
- Centralized gameplay-input suppression for menus, chat, rebinding, and the developer console

## Quick start

Open `project.godot` in Godot 4.6 and press F6/F5. For a local two-player test, run two
instances: host on one, then direct-connect to `127.0.0.1` from the other. On separate
machines, the host should appear under **LAN Games**; UDP `8911` and game UDP `8910` must
be allowed through the firewall.

Useful command-line modes:

```bash
godot --headless --path . --server --port=8910 --server-name="Dedicated Server"
godot --path . --join=127.0.0.1 --name="Player Two"
```

Copy `server.cfg.example` to `server.cfg` for dedicated-server name and player-limit defaults.
Command-line values such as `--server-name`, `--max-players`, and `--port` override them.
A headless dedicated server starts the match automatically once every connected client is
ready, and hosts can also drive the flow from the dev console with `start_match` and
`end_match`.

Run the complete xUnit, coverage, and multiplayer integration suite with:

```bash
bash Tests/run_all_tests.sh
```

See [docs/TESTING.md](docs/TESTING.md) for individual commands, coverage interpretation, and
the scenarios under test.

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
- `AvatarFactory.cs` builds the configured replacement rig, falling back to the built-in body.
- `PlayerCamera.cs` owns the first/third-person toggle and the head-anchored aim origin.
- `PlayerNameplate.cs` renders a remote player's floating name, speaking, and AFK indicators.
- `TextChatManager.cs` and `PositionalVoiceChat.cs` own session communication.
- `PlayerListPanel.cs` is the in-match roster with presence, mute/block, volume, and kick controls.
- `EmoteCatalogue.cs` and `QuickChatLines.cs` are id-keyed tables whose lookup doubles as the
  server's validation, rejecting any id absent from the table.
- `InteractionManager.cs`, `NetworkInteractable.cs`, and `NetworkPhysicsObject.cs` are the
  reusable world-interaction and synchronized-physics seams.
- `NetworkClock.cs` supplies synchronized time, ticks, deadlines, scheduled callbacks, and
  named round/match timers.
- `ConnectionQualityMonitor.cs` derives rolling health metrics from clock probes and publishes
  them to lobby indicators and the F3 overlay.
- `RpcSecurity.cs` provides composable validation, per-peer rate limiting, and cooldowns.
- `LocalInputContext.cs` owns local gameplay-input and mouse-capture policy.

Game RPCs treat the server as authority. The example player sends input—not transforms—and
uses prediction/reconciliation for responsiveness. Never accept damage, inventory, scores, or
other outcomes directly from clients; accept intent, validate it on the server, then replicate
the result.

See [docs/EXTENDING.md](docs/EXTENDING.md) for the replacement seams and shipping checklist.

## Security notes

Known trust limitations to keep in mind when shipping:

- Steam identity is self-reported at registration; there is no auth-ticket validation, so a
  modified client can claim any Steam ID. Persistent mute/block keys derived from it (and the
  LAN fallback, which keys off the lowercased display name) are therefore spoofable. Add
  Steamworks auth-session tickets before trusting identity for anything consequential.
- The dev console is disabled in exports unless the export preset sets the
  `enable_console_dev` feature tag; admin-only commands additionally require
  `DevConsoleManager.SetAdminStatus(true)`, which shipped builds must gate themselves.
- Rate limits and validation in `RpcSecurity` bound abuse but are tuned for small friendly
  sessions, not hostile public servers.

## Core scope

The generic multiplayer foundation is feature-complete. Reconnect/slot reclamation, host
migration, generalized game-mode settings, progression profiles, Workshop integration,
achievements, cloud saves, and replay systems are deliberately extension points because their
correct behavior depends on the game and deployment model.

## Known issues

Pre-existing, not caused by this phase's work, not yet fixed:

- A windowed client can abort with a segmentation fault during engine teardown, after gameplay
  and tests have completed.
- `NetworkPhysicsObject` snapshot RPCs can reach a peer before its match scene has finished
  mounting, logging "Node not found" on a slower-loading client.
