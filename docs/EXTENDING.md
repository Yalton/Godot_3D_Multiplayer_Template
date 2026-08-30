# Extending the template

## Replace the example game

Keep `Session` alive and replace `GameWorld.cs` / `NetworkPlayer.cs`. The stable session
surface is:

- `SessionManager.Instance.Players`: `{ peer_id: {name, ready, afk} }`
- `HostEnet()`, `JoinEnet()`, `HostSteam()`, `JoinSteamLobby()`
- `SetReady()`, `StartMatch()`, and `DisconnectSession()`
- `RosterChanged`, `MatchStarted`, and `ConnectionStateChanged`
- `StateChanged` and the `Offline → Connecting → Lobby → Loading → Playing` lifecycle

The server is always multiplayer peer `1`. `GameWorld` tracks players by peer ID in a typed
dictionary; node names are not an identity contract. `PeerId` identifies which client may
submit input.

## Replace the avatar

Set the project setting `template/avatar/scene_path` to a `PackedScene` whose root derives
from `AvatarRig` (`Scripts/Avatar/AvatarRig.cs`). Leave it empty to use the built-in
code-built `BlockmanRig`. A configured path that is empty, does not resolve to a resource,
does not load as a `PackedScene`, or has a root that does not derive from `AvatarRig` all log
a warning and fall back to the built-in rig rather than failing to build a character.

A replacement rig must implement:

- `SetLocomotion(LocomotionResult state)` — cosmetic, called every physics frame on every peer.
- `PlayAction(string clip)` / `StopAction()` / `ActionPlaying` — one-shot clips over locomotion.
- `GetAttachment(AvatarSlot slot)` — returns the attach point for a prop or cosmetic.
- `ApplyAppearance(AvatarAppearance appearance)` — applies the replicated cosmetic description.

The built-in rig defines clips `idle`, `walk`, `run`, `jump`, `fall`, `land`, and attachment
slots `Head`, `Back`, `Hips`, `HandLeft`, `HandRight`. A replacement is free to use different
clip names and slot contents internally, but code that calls `PlayAction` or `GetAttachment`
by name should target names your rig actually defines.

Locomotion animation is cosmetic: `NetworkPlayer` derives it locally on every peer from
movement data it already has (velocity, grounded state), so it never needs to be replicated.
Discrete presence state such as AFK is different — it is decided once on the server and
replicated to every peer over the roster RPC. Any new state you add that affects gameplay
outcomes must follow the AFK pattern (server-authoritative, replicated), never the locomotion
pattern (locally derived), or clients can disagree on what happened.

A rig is never built on a headless server (`AvatarFactory.Build` returns `null` there).
`BoneAttachment3D` and the animation nodes it drives never participate in physics, so
simulation is identical with or without a rig.

A rig is added as a child of the player's `CharacterBody3D` at local origin
(`Scripts/Game/GameWorld.cs`), so a replacement must be authored **feet at origin, roughly
1.8 m tall**, to line up with the `CapsuleShape3D` of radius 0.45 and height 1.8 centred at
y=0.9. A model with a centred origin sinks into the floor.

A replacement rig must carry **no collision shape and no physics body**. The note above that
simulation is identical with or without a rig is a fact about the built-in `BlockmanRig`; for
a replacement it is a requirement, because added physics changes the simulation and desyncs
client-side prediction.

`template/avatar/scene_path` lives in Project Settings → Advanced, or directly in the
`[template]` block of `project.godot`:

```ini
[template]

avatar/scene_path="res://path/to/my_rig.tscn"
```

A minimal subclass looks like:

```csharp
public partial class MyRig : AvatarRig
{
    public override void SetLocomotion(LocomotionResult state) { }
    public override void PlayAction(string clip) { }
    public override void StopAction() { }
    public override bool ActionPlaying => false;
    public override Node3D GetAttachment(AvatarSlot slot) => this;
    public override void ApplyAppearance(AvatarAppearance appearance) { }
}
```

## Replace the whole player

To swap the entire player node without touching code, set `GameWorld.PlayerScene` (exported
in the inspector on the match scene's root) to your own `PackedScene`. Its root must be a
`NetworkPlayer` (or subclass); movement tuning (`Speed`, `JumpVelocity`) is exported there.
`GameWorld` looks for a `PlayerCamera` and a `PlayerNameplate` among the root's descendants
and constructs whichever one is missing, at the same local offsets the built-in player uses
(camera pivot at y=1.55, nameplate at y=2.25). If no `AvatarRig` descendant is found, the
configured (or built-in) avatar is attached automatically, same as the built-in player.

`PlayerScene` replaces the entire player; `template/avatar/scene_path` replaces only the
character body.

## Load a match scene

Put the gameplay root in its own `PackedScene`, with deterministic node paths on every peer:

```csharp
MatchSceneCoordinator.Instance.BeginMatchLoad("res://games/my_match.tscn");
```

The coordinator threads the resource load on every peer, waits for readiness acknowledgements,
then advances the session to `Playing`. New clients joining an active session load the current
scene before being spawned.

## Protocol compatibility

Increment `NetworkBuildInfo.ProtocolVersion` whenever RPC signatures or replicated payloads
change. Clients submit their game and protocol versions before roster registration, and the
host rejects incompatible builds with a readable reason.

## Add authoritative gameplay

Use this pattern:

1. A client sends an intent RPC with `[Rpc(MultiplayerApi.RpcMode.AnyPeer)]`.
2. The server derives identity from `Multiplayer.GetRemoteSenderId()`.
3. The server validates ownership, cooldown, distance, and current match phase with
   `RpcSecurity`, `RpcRateLimiter`, and `RpcCooldowns`.
4. The server mutates canonical state.
5. The server broadcasts the resulting state.

Do not trust a peer ID included in client payloads. Derive identity from the RPC sender.

For local controls, use `LocalInputContext.Instance.GameplaySuppressed` instead of checking
individual menus. Register modal UI with `SetBlocked(owner, true)` while it is open and clear
the same owner when it closes.

## Dedicated servers

Start ENet hosting without client UI or audio capture:

```bash
godot --headless --path . --server --port=8910 --config=res://server.cfg
```

Copy `server.cfg.example` to `server.cfg`. Command-line `--server-name`,
`--max-players`, and `--port` values override config defaults.

## Shipping checklist

- Replace the Steam development App ID and unique lobby tag.
- Pin and test one GodotSteam version on every export platform.
- Test 3+ peers; two-peer tests do not expose relay failures.
- Add protocol/build compatibility to lobby data before public releases.
- Add NAT traversal or a relay service if direct internet ENet is required.
- Rate-limit gameplay, chat, and lobby RPCs.
- Decide reconnect and host migration policy. This template returns clients to the menu when
  the host leaves and intentionally does not attempt host migration.
- Run exported builds through Steam, not only editor instances.

## Automated multiplayer harness

`Tests/NetworkSmoke.cs` covers one host, two valid clients, one incompatible client, ready-up,
synchronized map loading, transition to `Playing`, clock sync, security primitives, host-loss
recovery, and headless client-service suppression. It is designed to be launched as separate
Godot processes; see the command in the project README.

Pure policies are covered by the xUnit project in `MultiplayerTemplate.Tests`, with Coverlet
Cobertura/JSON reports. Run both test layers through `bash Tests/run_all_tests.sh`; details are
in `docs/TESTING.md`.
