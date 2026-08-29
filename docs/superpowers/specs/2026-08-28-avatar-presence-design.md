# Avatar and Presence (Friendslop P1)

## Context

The template is a feature-complete server-authoritative 3D multiplayer foundation. It is a
solid base for a competitive or systems-driven game, but it lacks the social-expression
features that define casual "play with friends" games: visible characters, emotes, cosmetic
identity, a rich voice layer, and in-match social controls.

This document specifies the first of five phases that add those features.

### Phase decomposition

The full expansion is too large for a single spec. It is split along dependency lines. Each
phase ships standalone, leaves the template usable, and gets its own spec, plan, and
implementation cycle.

| Phase | Scope | Depends on |
| --- | --- | --- |
| P1 | Avatar and presence: rigged character, networked animation state, third-person camera, Tab player list, speaking indicator, AFK detection, host kick | — |
| P2 | Expression and identity: emote wheel, character customization, profile persistence, quick-chat wheel | P1 |
| P3 | Voice overhaul: Opus codec, voice activity detection, per-player volume mixer, proximity/global channels, microphone settings UI | P1 (player list) |
| P4 | Social physics: player push and ragdoll, carrying players, prop spawner | P1 |
| P5 | World toys and polish: ping markers, spray tags, boombox, photo mode, Steam rich presence and party | P1 |

This document covers P1 only.

## Goals

- Players have a visible, animated body instead of a capsule primitive.
- The animation and cosmetic layer is swappable for a user-supplied model.
- Players can see their own character, which is a precondition for cosmetics mattering.
- In-match social state (who is speaking, who is idle, who is lagging) is visible and
  actionable.
- The template ships no binary art assets and no new third-party dependencies.

## Non-goals

- Emotes and cosmetic slots. P1 provides the seams; P2 fills them.
- Voice codec, voice activity detection, and channel routing. P3.
- Any change to movement, prediction, or reconciliation behavior.

## Decisions

**Avatar source: code-built rig, zero assets.** The default character is built in C# from a
`Skeleton3D` and box meshes, with animations generated procedurally. This keeps the repository
asset-free and license-clean, matches the template's existing all-code construction style, and
makes the blocky result read as an intentional placeholder. Users replace it with their own
model through a documented contract.

**Animation authority is split, and the split is explicit.** Locomotion animation is local,
cosmetic, and derived from data every peer already has. Discrete presence state is
server-authoritative and replicated. Deriving locomotion locally costs no bandwidth and avoids
the round trip that would otherwise delay a player's own walk animation behind their own
movement. The boundary must be respected: if a future state affects gameplay outcomes, it is
replicated, not derived.

**First-person remains the default view, with a third-person toggle.** The example character
stays a clean first-person reference. Third-person exists so players can see their own
character and, in P2, their own emote.

## Architecture

### New: avatar layer (`Scripts/Avatar/`)

**`AvatarRig.cs`** — abstract `Node3D`, the replacement contract:

- `SetLocomotion(float planarSpeed, bool grounded, float verticalVelocity)`
- `PlayAction(string clip)`, `StopAction()`, `bool ActionPlaying`
- `Node3D GetAttachment(AvatarSlot slot)` for `Head`, `Back`, `Hips`, `HandLeft`, `HandRight`
- `ApplyAppearance(AvatarAppearance appearance)` — P1 applies only the per-peer hue currently
  produced by `GameWorld.MakePlayer`; P2 extends the record with cosmetic slots

**`BlockmanRig.cs`** — the default implementation. A `Skeleton3D` with bones for hips, spine,
head, upper and lower arms, and thighs and shins. Each bone carries a `BoneAttachment3D` with
a `BoxMesh` child, so the body is rigid boxes following bones and no skin weights need to be
authored in code. Each part gets its own `StandardMaterial3D` so P2 can recolor per slot.
Total height is 1.8 to match the existing `CapsuleShape3D` (radius 0.45, height 1.8, offset
y 0.9).

**`ProceduralAnimationLibrary.cs`** — constructs `Animation` resources in C# for `idle`,
`walk`, `run`, `jump`, `fall`, and `land`, registered in an `AnimationLibrary` and driven by an
`AnimationTree` state machine. P2 adds emote clips to the same library.

**`LocomotionSolver.cs`** — a pure, Godot-free class. Given planar speed, grounded state,
vertical velocity, and how long the character has been airborne, it returns a
`LocomotionState` (`Idle`, `Walk`, `Run`, `Jump`, `Fall`, `Land`) and a 0..1 blend value.
Thresholds are keyed off `NetworkPlayer.Speed` (6 m/s): below 0.15 m/s is idle, up to 3 m/s is
walk, above that is run. Airborne with vertical velocity above 0.5 is `Jump`, below -0.5 is
`Fall`. `Land` is a one-shot emitted when ground is regained after at least 0.25 s airborne.

**`AvatarFactory.cs`** — resolves which rig to build. Reads the project setting
`template/avatar/scene_path`; when empty, builds `BlockmanRig`. When set, instantiates the
scene and verifies its root derives from `AvatarRig`. On any failure it logs through
`DebugLogger` and falls back to the blockman. It builds nothing when
`DisplayServer.GetName() == "headless"`.

### Changed: player construction and camera

`GameWorld.MakePlayer()` stops building the capsule `MeshInstance3D` and the inline `Label3D`.
It builds collision, the `Head` pivot, and the camera as before, then adds
`AvatarFactory.Build(peerId)` and a `PlayerNameplate`.

**`Scripts/Game/PlayerCamera.cs`** is extracted from `NetworkPlayer`, which is already 258
lines and would otherwise absorb spring-arm handling, mode state, and FOV management on top of
movement, prediction, reconciliation, and interaction. `PlayerCamera` owns:

- the `Head` pivot, which stays a plain `Node3D` at y 1.55 and is deliberately **not** a
  `BoneAttachment3D` — parenting the camera to an animated bone makes the first-person view
  shake
- the `Camera3D` and a `SpringArm3D` of length 3.5 with a collision mask so the third-person
  camera pulls in at walls
- mode switching on the new `toggle_camera` action, default `C`, registered in `project.godot`
  so `InputRemapManager` exposes it for rebinding
- field of view from `SettingsManager`, and persistence of the chosen mode as `camera_mode`
- local rig visibility: hidden in first person, shown in third person
- `PushTemporaryThirdPerson(owner)` / `PopTemporary(owner)`, shipped unused in P1 and called by
  P2 emotes, using the same owner-token shape as `LocalInputContext.SetBlocked`

**Aim origin decouples from the camera.** `NetworkPlayer.InteractionTarget()` currently
raycasts from `_camera.GlobalPosition` along `-_camera.GlobalBasis.Z`. In third person that
origin sits behind the character and the ray's first hit is the player's own back. The raycast
moves to the `Head` pivot and head-forward in both modes. First-person behavior is unchanged
because the head and camera are colocated there. `HoldPosition` already derives from the head
and needs no change. `EyePosition` is redefined as the head pivot position.

**`Scripts/Game/PlayerNameplate.cs`** replaces the inline `Label3D`: name, a speaking icon fed
by `PositionalVoiceChat.IsSpeaking`, a dimmed AFK tag driven by the roster, distance-based
alpha fade, and hidden entirely for the local player.

### Animation state flow

`NetworkPlayer._PhysicsProcess` calls `LocomotionSolver` for every instance and pushes the
result into `AvatarRig.SetLocomotion`. The local player uses its predicted `Velocity`; remote
players use `_targetVelocity`, which `ReceiveSnapshot` already carries. No new RPC and no
additional bandwidth.

### Presence: AFK

AFK is derived where input already arrives. The server branch of `NetworkPlayer` stamps a
last-input timestamp whenever `SubmitInput` carries a nonzero move vector, a jump, or a yaw
delta above a small epsilon.

**`Scripts/Game/AfkPolicy.cs`** is a pure class implementing the rule as
`(secondsSinceInput, threshold, currentlyAfk) -> bool`, so the timing and hysteresis are unit
testable. The default threshold is 90 seconds.

The resulting flag rides the existing roster rather than a parallel channel. `PlayerIdentity`
gains a `bool Afk` field with matching dictionary serialization, and `SessionManager` gains a
server-only `SetAfkServer(peerId, afk)` that mirrors the existing `SetReadyServer`: mutate the
roster entry, `Rpc(SyncRoster)`, and raise `RosterChanged`. Updates are reliable and sent only
on change, and are guarded on roster membership because a peer can disconnect mid-timer.

There is no automatic kick by default. A dedicated server may set `afk_kick_seconds` in
`server.cfg`; when unset, AFK is a badge only.

### Presence: speaking

Speaking state is deliberately kept off the roster, which replicates the whole player
dictionary on every change and would be a poor fit for a flag that toggles several times per
second. `PositionalVoiceChat` already knows exactly which peer it just played a packet for. It
exposes `IsSpeaking(peerId)` backed by a per-peer decay timer of roughly 250 ms. This is a
receive-side local observation and requires no protocol change.

### In-match player list

**`Scripts/UI/PlayerListPanel.cs`** with a shared **`Scripts/UI/PlayerRow.cs`**. `Main.cs` is
497 lines and builds the lobby roster rows inline; the row widget is extracted so the lobby
list and the in-match panel render from one implementation.

Each row shows the Steam avatar from `PlayerIdentityService` (falling back to the per-peer hue
swatch), the display name, host and AFK badges, the speaking icon, ping and a quality dot from
`ConnectionQualityMonitor`, a mute toggle, a volume slider from 0 to 200 percent, a block
toggle, and a host-only kick button.

The panel toggles on a new `player_list` action, default `Tab`, handled in `_UnhandledInput`
with `SetInputAsHandled` so it does not fall through to Godot's `ui_focus_next`. Opening
registers `LocalInputContext.SetBlocked(panel, true)`, which frees the mouse and suppresses
gameplay input, matching the existing modal pattern. A hold-to-view panel was rejected: it
would require freeing the mouse mid-stride in order to click a slider.

### Per-peer voice volume

`PositionalVoiceChat.SetPeerVolume(peerId, linear)` sets `volume_db` on that peer's
`AudioStreamPlayer3D`. The value persists in `PlayerIdentityService` keyed by
`StableSocialKey`, alongside the existing mute and block state, so it survives sessions and
works for both LAN and Steam identities. `PlayerIdentityService.SetSocialState` currently
handles only booleans and is generalized to store floats as well. P3 replaces the audio
backend with its mixer; the panel's call site is unaffected.

### Host kick

The only peer disconnection today is the registration rejection at `SessionManager.cs:259`.
P1 adds `RequestKick(peerId, reason)` as an `AnyPeer` RPC validated by `RpcSecurity` to
require the host as sender, rate limited like other request RPCs. It notifies the target with
a reason before calling `DisconnectPeer`. This makes the AFK badge actionable and is the same
path a dedicated server's `afk_kick_seconds` uses.

### Protocol

A single `NetworkBuildInfo.ProtocolVersion` increment covers the roster's new `afk` field and
the new kick RPC.

## Error handling

- Unset, unloadable, or wrongly-typed `template/avatar/scene_path`: log through `DebugLogger`,
  fall back to `BlockmanRig`. Never fatal.
- A rig missing a named animation clip: log once, hold `idle`.
- `RequestKick` from a non-host: `RpcSecurity.Reject`, rate limited.
- AFK or kick targeting a peer that has already left: guarded by roster membership checks.
- Missing Steam avatar: per-peer hue swatch.
- Third-person spring arm with no colliding geometry: default length, no special case.

## Headless behavior

`AvatarFactory` builds nothing on headless. The server needs collision and movement, not box
meshes or an `AnimationTree`, and `BoneAttachment3D` never participates in physics, so server
simulation is bit-identical with or without a visual rig. `PlayerCamera`, `PlayerNameplate`,
and `PlayerListPanel` are client-only for the same reason. The guard follows the existing
pattern in `PositionalVoiceChat._Ready`.

## Testing

New xUnit cases in `MultiplayerTemplate.Tests`, all against pure classes:

- `LocomotionSolver` state and blend boundaries, including the `Land` one-shot and the
  airborne-duration threshold
- `AfkPolicy` transitions and hysteresis
- Kick authorization: host accepted, non-host rejected
- `PlayerIdentity` dictionary round-trip including `Afk`, and defaulting for older payloads
- Per-peer volume persistence keyed by `StableSocialKey` across LAN and Steam identities
- Speaking-state decay timing

New scenarios in `Tests/NetworkSmoke.cs`:

- An avatar node exists for every player on every peer after spawn
- The AFK flag propagates from host to clients and clears on input
- A host kick removes the peer and delivers a reason
- A client on the previous protocol version is still rejected

`bash Tests/run_all_tests.sh` remains the single entry point.

## Documentation

- README: feature list entries for the avatar layer, third-person toggle, player list, AFK,
  and host kick.
- `docs/EXTENDING.md`: a "Replace the avatar" section covering the `AvatarRig` contract, the
  required animation clip names, the attachment slots, and the
  `template/avatar/scene_path` setting.
- `stretch_features.md`: mark P1 complete and record P2 through P5 as planned phases.
