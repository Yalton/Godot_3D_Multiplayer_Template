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

A rig is never built on a headless dedicated server (`AvatarFactory.Build` returns `null` there).
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
    private readonly System.Collections.Generic.Dictionary<AvatarSlot, Node3D> _attachments = new();

    public override void _Ready()
    {
        foreach (AvatarSlot slot in System.Enum.GetValues<AvatarSlot>())
        {
            var marker = new Node3D { Name = $"Slot_{slot}" };
            AddChild(marker);
            _attachments[slot] = marker;
        }
    }

    public override void SetLocomotion(LocomotionResult state) { }
    public override void PlayAction(string clip) { }
    public override void StopAction() { }
    public override bool ActionPlaying => false;
    public override Node3D GetAttachment(AvatarSlot slot) => _attachments[slot];
    public override void ApplyAppearance(AvatarAppearance appearance) { }
}
```

### Appearance customization

Each roster entry carries the canonical appearance as five integer keys:
`appearance_body_color`, `appearance_head`, `appearance_back`, `appearance_hips`, and
`appearance_hand`. These are catalogue IDs, not client-authored meshes or materials. Missing
or malformed values resolve to the default appearance when a roster is read.

A client may request its own appearance only while the session is in `Lobby`. The request sends
the five IDs as primitives; the server derives the sender from the RPC, verifies that sender is
registered, rate-limits the request, validates every ID against `CosmeticCatalogue`, and updates
only that sender's roster entry before broadcasting the roster. Invalid IDs and requests outside
the lobby leave the canonical roster unchanged. Do not add a client-side roster mutation or a
peer ID to this request: appearance follows the same server-authoritative pattern as AFK.

`BlockmanRig.ApplyAppearance` is deliberately procedural: it recolours the body and creates the
selected primitive cosmetic nodes under its attachment markers. It is not an asset-loading path.
A headless dedicated server still builds no avatar rig, so neither appearance meshes nor marker
nodes exist there and simulation remains independent of cosmetic rendering.

If you replace the rig, `ApplyAppearance(AvatarAppearance)` is your rendering responsibility.
`GetAttachment` must return stable `Node3D` markers for `AvatarSlot.Head`, `Back`, `Hips`,
`HandLeft`, and `HandRight`; the built-in rig uses those markers for its procedural cosmetics.
A custom rig may render its catalogue IDs differently, but it must own the mapping and preserve
the no-physics requirement described above.

## Replace the emotes and quick-chat lines

Emotes are looked up by id in `Scripts/Avatar/EmoteCatalogue.cs`; the lookup is also the
server's validation, so a submitted id absent from the table is rejected rather than trusted.
To add one, add a row there and a clip of the same name in `ProceduralAnimationLibrary`. To
replace the set wholesale, replace the table — a rig that defines different clip names owns
what those names mean.

A replacement rig that does not define a catalogue clip gets a specific, deliberate
degradation: `AvatarRig.PlayAction` (`BlockmanRig.PlayAction` in the built-in rig) ignores
clip names the rig has no animation for and logs a warning, so a custom rig with no
`emote_sit` plays nothing while `NetworkPlayer.ActiveEmote` still replicates normally — that
player is genuinely sitting to the server while standing still visually on every peer.
Consistent world state, missing cosmetic; state it plainly so nobody debugs it twice. The
local camera's automatic third-person swing while an emote plays also does not stick in this
case: it is bounded by a half-second timeout (`NetworkPlayer._emoteStartDeadline`) so a clip
that never starts still returns the camera on its own.

Quick-chat lines are ids, not free text; adding one is a row in
`Scripts/Communication/QuickChatLines.cs`. Every line must survive `ChatPolicy.Sanitize`
unchanged — `QuickChatLinesTests` asserts this for the whole table, so a line that would be
altered by sanitizing (leading/trailing whitespace, embedded line breaks) fails the build
rather than silently reaching chat trimmed.

Only a looping emote is state: `NetworkPlayer.ActiveEmote` holds its id, or `EmoteCatalogue.None`
if the active emote is one-shot or there isn't one — a one-shot is broadcast once and
forgotten, not stored. This follows the state-lifetime rule below: `ActiveEmote` lives on
`NetworkPlayer`, so it dies with the body, and `GameWorld.SpawnForLateJoin` is the single place
that reads it from outside that node, to replay it for a peer who joins mid-emote.

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

### Choose the input phase

Use `_UnhandledInput` for normal gameplay controls so focused UI and earlier consumers can
handle an event first. Use `_Input` only when a control must run before GUI or gameplay
consumers, such as a wheel that must see mouse motion before camera look or a panel that must
close while one of its controls has focus. An early-phase handler must respect
`LocalInputContext.Instance.GameplaySuppressed`; the exception is an already-open surface's own
close or release path. `GameplaySuppressed` also accounts for focused text entry and an open dev
console; `SetBlocked(owner, true)` adds modal ownership and changes mouse capture. Mark an event
handled only when the handler actually consumed it.

## Replicated state must match its owner's lifetime

When you add replicated state, the node that writes it must live at least as long as the
state's scope, or something longer-lived must reset it when the shorter-lived owner appears.
Otherwise a value written by a node that no longer exists just sits there unchanged, because
nothing observes a change to publish.

`PlayerIdentity.Afk` is the worked example: it lives on the session-scoped roster in
`SessionManager`, but it is only ever written by the match-scoped `NetworkPlayer` while idle
time is being tracked. A player flagged away in one match would stay flagged away in the
lobby and into the next match, because the fresh `NetworkPlayer` that replaces the old one on
the next spawn starts from its own `false` and has no reason to publish a change. This is
handled today in `NetworkPlayer._Ready`, which clears the roster flag
(`SessionManager.Instance.SetAfkServer(PeerId, false)`) on the server the moment a player node
spawns — the session-scoped value is reset by whichever node currently owns writing it,
rather than trusting whatever the previous owner last published.

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
