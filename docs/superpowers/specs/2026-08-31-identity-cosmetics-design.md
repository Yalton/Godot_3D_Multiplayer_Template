# Identity & Cosmetics (Friendslop P2b)

## Context

P2a split transient expression from durable identity. This phase delivers the durable half:
players select a small, memorable appearance in the lobby, that selection survives new
sessions on their client, and every peer sees the server-approved result. It extends the seams
already shipped for this purpose: `AvatarAppearance`, `AvatarRig.ApplyAppearance`, and the
`AvatarSlot` attachment markers.

Appearance is roster state. `PlayerIdentity` is extended and the existing roster sync carries
the result, exactly as it does for the AFK flag. This is a fixed decision from P2a.

## Goals

- Let a player choose a body-colour swatch plus a head, back, hips, and hand cosmetic.
- Let the player preview and change that appearance only in the lobby.
- Persist the last local selection and automatically reuse it in subsequent sessions.
- Validate all client-provided cosmetic selections on the server and replicate only canonical
  appearance data.
- Render cosmetics procedurally, with no binary assets, NuGet packages, or addons.

## Non-goals

- Inventories, unlocks, purchases, rarity, or progression.
- In-match appearance changes.
- Arbitrary colour-picker values or user-supplied art.
- A new roster or replication system.
- Changes to player avatars on a headless dedicated server.

## Cosmetic catalogue

`CosmeticCatalogue` is a pure, engine-free policy class. It owns named, immutable definitions
for a deliberately small initial set:

- body-colour swatches;
- one head item;
- one back item;
- one hips item; and
- one hand item, applied symmetrically to both hands.

Every slot has a `none` choice. Appearance stores only stable integer IDs, never scene paths,
resource references, arbitrary colours, or client-authored node data. The catalogue resolves an
ID only for its intended slot and exposes safe defaults. This gives the server one deterministic
validation boundary and lets pure xUnit tests cover the policy without loading Godot.

## Appearance data and persistence

`AvatarAppearance` becomes an immutable value record containing the five selected catalogue
IDs: body colour, head, back, hips, and hand. It provides a default appearance and conversion to
and from the roster dictionary representation. Missing, malformed, unknown, or wrong-slot values
resolve to defaults; they never survive as canonical state.

`PlayerIdentity` carries `AvatarAppearance` and includes it in `ToDictionary` and
`FromDictionary`. Existing peers that receive old or partial roster dictionaries therefore get
defaults, retaining compatibility with stored or test-created data.

A small local profile service persists only the local player's requested appearance in the same
configuration style already used for local social preferences. It loads defaults on absent or
invalid config and saves immediately after a successful local selection. The profile is intent,
not authority: the roster value returned by the server remains the one rendered in sessions.

## Server authority and replication

Initial registration appends the five selected appearance IDs to the existing identity request.
The server reconstructs, validates, and normalizes `AvatarAppearance` through `CosmeticCatalogue` before constructing
`PlayerIdentity`, then syncs the ordinary roster. The protocol version increments because the
registration RPC signature changes.

While in `Lobby`, a client may request an appearance update. The RPC carries only the five
integer IDs (rather than a custom C# value type) and is handled by peer 1. It follows the repository contract:

1. Derive the sender from `GetRemoteSenderId()`.
2. Require the server, a known sender, and the lobby state through `RpcSecurity`.
3. Apply an `RpcRateLimiter`; repeated rejected warnings remain structurally throttled by
   `RpcSecurity.Reject`.
4. Validate and normalize through `CosmeticCatalogue`.
5. Replace only that sender's appearance in the canonical roster and broadcast `SyncRoster`.

The server never accepts a target peer ID, a node path, a material, or any art payload from a
client. Requests outside the lobby are rejected and do not mutate profile or roster state.

## Avatar rendering

`AvatarRig.ApplyAppearance` is the only rendering boundary. `BlockmanRig` continues to apply
the body swatch to its materials, then clears its previous cosmetic nodes and builds the selected
procedural geometry under the existing `AvatarSlot` markers. The hand item is instantiated under
both hand markers. Each cosmetic uses simple C#-constructed primitives/materials; no art files
are introduced.

Avatar spawning and roster changes apply the canonical roster appearance for both local and
remote bodies. On a headless dedicated server, `AvatarFactory` still returns no rig, so this
visual work remains a no-op.

## Lobby customization UI

The lobby exposes a modal customization panel with simple per-slot controls and a preview of the
local avatar. Opening it claims a `LocalInputContext` blocker and frees the mouse; closing it
releases that same blocker. It is unavailable outside the lobby.

Selecting an option immediately updates the local preview. The client saves its requested
selection locally and sends the update intent; normal roster sync reconciles the preview to the
server-approved result. If a connection ends before confirmation, the next session still begins
with the saved local selection and the server validates it again.

## Tests and verification

- Pure xUnit tests cover catalogue slot validation, defaults, immutability, and profile
  load/save behaviour.
- `PlayerIdentity` tests cover appearance dictionary round-tripping and legacy/malformed data
  resolving to defaults.
- Engine smoke scenarios cover initial appearance replication, lobby updates, server rejection
  of out-of-phase or invalid requests, and a late joiner receiving canonical cosmetics.
- Tests must not construct Godot types in xUnit. Engine-touching coverage belongs in
  `Tests/NetworkSmoke.cs`.
- Run the focused unit tests, the full unit suite, the multiplayer smoke harness, and
  `Tests/run_all_tests.sh` from the physical worktree path.

## Acceptance criteria

- A player can choose and preview the five appearance fields in the lobby.
- The choice persists locally and is reused on the next registration.
- Every peer, including a late joiner, sees the server-approved appearance.
- Invalid, malformed, cross-slot, too-frequent, and out-of-lobby update requests leave roster
  state unchanged.
- Dedicated servers create no client visual nodes, and no binary assets, packages, or addons are
  added.
