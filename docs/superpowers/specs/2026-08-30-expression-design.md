# Expression (Friendslop P2a)

## Context

P1 added a visible, animated body and in-match presence. It deliberately shipped three seams
unused, for this phase: `AvatarRig.PlayAction` / `StopAction` / `ActionPlaying`, the
`AvatarSlot` attachment markers, and `PlayerCamera.PushTemporaryThirdPerson` /
`PopTemporary`.

The original P2 scope — emotes, character customization, profile persistence, and a
quick-chat wheel — is three subsystems. It is split here along the seam that separates
transient expression from durable identity:

| Phase | Scope |
| --- | --- |
| **P2a (this document)** | One reusable radial wheel, emotes through `PlayAction`, quick-chat lines through the existing chat path, the emote camera, and server validation for both |
| P2b | `AvatarAppearance` and `PlayerIdentity` cosmetics, the cosmetic catalogue and slot attachments, the lobby customization screen, and local profile persistence |

Each ships standalone. P2b will replicate appearance by extending the roster record, exactly
as `Afk` was added in P1; that decision is recorded here because it constrains nothing in
P2a and should not be relitigated.

## Goals

- Players can emote, including emotes that persist until they move.
- Players can send canned lines without typing.
- Both are server-validated and rate limited, like every other client request in this
  template.
- The template still ships no binary art assets.

## Non-goals

- Character customization and cosmetic slots. P2b.
- Free-text chat changes. The existing `TextChatManager` path is reused unmodified.
- Emote-driven gameplay effects. Emotes are cosmetic.

## Decisions

**Looping emotes are replicated state; one-shots are events.** A wave needs no state. Sitting
and dancing are most of what makes this category worth building, and they require other peers
— including late joiners — to know you are still doing it.

**That state lives on `NetworkPlayer`.** Four separate P1 bugs shared one shape: state whose
lifetime did not match the node owning it. A looping emote belongs to a body. Putting it on
the body makes the lifetime rule structural rather than remembered — when the node is freed at
match end, respawn, disconnect, or kick, the state goes with it. There is no map to prune and
no reset to remember. The cost, accepted deliberately, is that emote logic spans
`NetworkPlayer` and `GameWorld` rather than sitting in one service.

**The wheel keeps the mouse captured.** It is held, not toggled, and reads relative motion, so
a player can walk while choosing. It therefore does not go through
`LocalInputContext.SetBlocked`, which frees the mouse — correct for the Tab panel, wrong here.

## Architecture

### Emote catalogue

**`Scripts/Avatar/EmoteCatalogue.cs`** — pure and engine-free, so it is unit testable
alongside `LocomotionSolver` and `AfkPolicy`. It must not reference the `Godot` namespace.

- `readonly record struct EmoteDefinition(int Id, string Clip, string DisplayName, bool Looping)`
- a static table of definitions
- `static bool TryGet(int id, out EmoteDefinition definition)`

That lookup is the server's validation. An id absent from the table is rejected, so a client
cannot index anything with an arbitrary integer.

Initial set: `wave`, `point`, `clap`, `cheer` as one-shots; `sit` and `dance` as loops.

### Clips

`Scripts/Avatar/ProceduralAnimationLibrary.cs` gains one clip per catalogue entry, authored as
bone rotation tracks in C# exactly as the locomotion clips are, against the bone names that
file already addresses. One-shots use `Animation.LoopModeEnum.None`; loops use
`LoopModeEnum.Linear`.

The looping behaviour needs no new rig API. `BlockmanRig` layers actions through an
`AnimationNodeOneShot`, which ends when its sub-animation finishes. A clip that loops never
finishes, so it plays until `StopAction()` aborts it. `PlayAction` and `StopAction` therefore
already express both kinds.

### State and replication

`NetworkPlayer` gains `public int ActiveEmote { get; private set; }`, zero meaning none. Only
looping emotes set it.

Request path, following the established pattern:

1. The local player calls `RequestEmote(int id)`, which sends `RpcId(1, SubmitEmote, id)`.
2. The server handler runs `RpcSecurity.RequireServer`, `RequireSender(sender, PeerId)`, and
   `RequireState(Playing)`.
3. An `RpcRateLimiter` gate follows — a wheel is trivially spammable. Four requests per two
   seconds.
4. `EmoteCatalogue.TryGet` validates the id.
5. Only then does the server act, broadcasting a reliable `Rpc(ReceiveEmote, id)`.

One-shots are broadcast and forgotten. Looping emotes additionally set `ActiveEmote` on the
server before broadcasting.

**Cancellation is server-side**, where input already arrives. `SubmitInput` clears a looping
emote on a non-zero move vector or a jump, and broadcasts the clear. `NoteActivity` already
inspects exactly those two things for AFK, so this rides the same check rather than adding a
second notion of "the player did something".

**Late join** extends the existing spawn path. `GameWorld.SpawnPlayer` gains an emote-id
argument, so a joining client learns who is currently sitting as part of being told that
player exists. This is the only path that reads the state from outside the node, and so the
only one that needs explicit handling.

### The wheel

**`Scripts/UI/RadialWheel.cs`** — one reusable `Control`, used twice, which knows nothing
about emotes or chat. It is given `(label, id)` entries and reports the committed id.

- Held, not toggled: press and hold opens, mouse direction selects a wedge, release commits.
- Releasing inside a centre dead zone cancels.
- It handles mouse motion in **`_Input`** and calls `SetInputAsHandled()` while open, so
  camera look is suppressed for exactly as long as the wheel is held.
  `NetworkPlayer._UnhandledInput` runs after the GUI phase and cannot win that contest —
  the same ordering problem the in-match player list hit in P1.
- The mouse stays captured throughout. The wheel does not call
  `LocalInputContext.SetBlocked`.

Client-only: never constructed on a headless server, following the guard in
`PositionalVoiceChat._Ready`.

### Quick chat

The same widget with a different entry list, committing a canned-line id. A line is sent as an
id through a validated, rate-limited server RPC and rendered through `TextChatManager`, so no
client-supplied string becomes displayable text and the existing sanitisation and limits keep
applying. An id absent from the line table is rejected exactly as an unknown emote id is.

### Camera

Playing an emote locally calls `PlayerCamera.PushTemporaryThirdPerson("emote")`; ending it
pops the same owner, restoring whichever mode the player had chosen rather than forcing first
person. A one-shot pops when `ActionPlaying` goes false; a loop pops on cancel.

Movement never locks. A looping emote is cancelled by movement, so there is no state in which
a player is stuck in an animation, and no new way to grief someone by freezing them.

### Input bindings

`emote_wheel` on `B` and `quick_chat_wheel` on `Z`, added to `project.godot` **and** to
`addons/InputRemapManager/InputRemapManager.tscn`'s `RebindableActionsOverride` and
`DisplayNameOverrides`. That second file is not optional: an action missing from it works but
never appears in the in-game Controls screen, which the README advertises. P1 had to return
for this twice.

### Protocol

One `NetworkBuildInfo.ProtocolVersion` bump for the phase, to 8, covering the new emote and
quick-chat RPCs and the extra `SpawnPlayer` argument.

## Error handling

- Unknown emote or quick-chat id: `RpcSecurity.Reject`, throttled per peer the way the
  non-finite input log is, because a wheel is spammable and warnings are no longer suppressed.
- Emote request from a peer whose player node no longer exists: dropped by the sender check.
- A rig missing a clip: logs once and plays nothing.
- Rate limit exceeded: rejected and logged, sender not disconnected.

## Replacement rigs

`PlayAction` ignores clip names a rig does not define, so a user's own rig with no `sit` clip
plays nothing while the state still replicates — that player is genuinely sitting to the
server while standing still visually. That is the correct failure (consistent world state,
missing cosmetic) but it must be documented: a replacement rig should either define the
catalogue's clip names or replace the catalogue table, and either way it owns what those names
mean.

## Headless behaviour

`RadialWheel` and any emote UI are never built on a dedicated server. The server still tracks
looping-emote state, because it is authoritative, but plays no animation — `AvatarFactory`
returns no rig there, and `PlayAction` is only ever called on a rig that exists.

## Testing

New xUnit cases, against pure classes only, since constructing a Godot type crashes the test
host and aborts the run:

- `EmoteCatalogue`: every catalogue id resolves; unknown and negative ids are rejected; every
  clip name in the table matches a name constant the animation library defines.
- The quick-chat line table, the same way.
- The wheel's direction-to-wedge maths — pure trigonometry, pinned at the wedge boundaries and
  inside the dead zone.

New `Tests/NetworkSmoke.cs` scenarios:

- A looping emote replicates from one client to another.
- Movement cancels it, and the cancel replicates.
- A late joiner sees an in-progress looping emote through the spawn path.
- An out-of-range emote id is refused, and the sender stays connected.

## Documentation

- `docs/EXTENDING.md`: how to add an emote (a catalogue row and a clip), how to replace the
  catalogue, and the replacement-rig degradation note above.
- `README.md`: the two new bindings in the feature list.
- `stretch_features.md`: P2a implemented, P2b next.
