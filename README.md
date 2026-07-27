# 3D Multiplayer Template

A Godot 4.6 C# starting point for small server-authoritative 3D multiplayer games.
It runs with no third-party addons and supports:

- ENet host and direct-IP join
- LAN server discovery over UDP broadcast
- Bundled GodotSteam lobby host, browser, and join
- Lobby roster, ready check, display names, and host-only match start
- Replicated player spawning and first-person movement
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

## Steam setup

GodotSteam 4.20 and its Steamworks runtime libraries are bundled under `addons/godotsteam`
for Linux, Windows, macOS, and Android ARM64. Set your real App ID in
`Scripts/Network/SteamTransport.cs`; App ID 480 is suitable only for development and shares
its lobby namespace with other games.

Launch the exported game through Steam. Steam controls are disabled with a tooltip when the
Steam client is unavailable or signed out. Public lobbies are tagged
`game=godot-3d-mp-template`, keeping Spacewar test lobbies out of the browser.

The bundled binaries come from the pinned GodotSteam fork used by Sundering (GodotSteam
4.20 / Steamworks SDK 1.64). See `addons/godotsteam/license.md` for its license.

## Architecture

- `Session` owns connection lifecycle, player metadata, lobby RPCs, and transport selection.
- `SettingsManager` and `InputRemapManager` are reusable C# autoload addons.
- `DebugLogger` provides persistent per-module logging switches and an editor dock.
- `DevConsoleManager` provides a backtick console; use `help` for core and multiplayer commands.
- `EnetTransport.cs` owns direct IP and LAN discovery.
- `SteamTransport.cs` contains all optional GodotSteam calls.
- `Main.cs` is the UI/state shell.
- `GameWorld.cs` owns authoritative player spawn/despawn.
- `NetworkPlayer.cs` is the replaceable example character.

Game RPCs should treat the server as authority. Never accept damage, inventory, scores, or
other outcomes directly from clients; accept intent, validate it on the server, then replicate
the result.

See [docs/EXTENDING.md](docs/EXTENDING.md) for the replacement seams and shipping checklist.
