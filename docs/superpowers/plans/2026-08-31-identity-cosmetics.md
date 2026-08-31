# Identity & Cosmetics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build persistent, lobby-only, server-authoritative cosmetic customization.

**Architecture:** A pure integer-ID appearance record and catalogue define valid choices. `PlayerIdentity` carries the server-normalized record in the existing roster; Godot-only profile, avatar, and UI layers consume it.

**Tech Stack:** Godot 4.6 C#, xUnit, ConfigFile, existing multiplayer smoke harness, procedural meshes.

**Spec:** `docs/superpowers/specs/2026-08-31-identity-cosmetics-design.md`

## Global Constraints

- Peer 1 is authoritative; derive sender with `GetRemoteSenderId()`.
- All requests use `RpcSecurity`, `RpcRateLimiter`, and structural reject throttling.
- No Godot types in xUnit; visual/RPC integration tests belong in `Tests/NetworkSmoke.cs`.
- No binary assets, packages, or addons. Headless servers create no avatar rig.
- Increment `NetworkBuildInfo.ProtocolVersion` for the `RegisterPlayer` signature change.
- Run the final wrapper from `/data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate`.

---

### Task 1: Define pure appearance and catalogue policy

**Files:** Create `Scripts/Avatar/AvatarAppearance.cs`, `Scripts/Avatar/CosmeticCatalogue.cs`, and `MultiplayerTemplate.Tests/CosmeticCatalogueTests.cs`; modify `Scripts/Avatar/AvatarRig.cs`.

**Produces:** `AvatarAppearance(int BodyColorId, int HeadId, int BackId, int HipsId, int HandId)`, `AvatarAppearance.Default`, `CosmeticSlot`, and `CosmeticCatalogue.Normalize(AvatarAppearance)`.

- [ ] Write failing tests that assert an unknown ID and a hand ID supplied to the head slot normalize to defaults, while valid IDs survive unchanged.
- [ ] Run `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~CosmeticCatalogueTests`; confirm the missing types/API cause the failure.
- [ ] Implement immutable integer-only IDs and a `CosmeticCatalogue` with per-slot constants, `none` as ID zero, `IsValid(CosmeticSlot, int)`, and `Normalize`. Do not import `Godot`.
- [ ] Re-run the focused test and full unit suite. Mutate one `IsValid` branch and prove the cross-slot test fails, then restore it.
- [ ] Commit: `feat: define cosmetic appearance policy`.

### Task 2: Serialize roster appearance and persist local intent

**Files:** Create `Scripts/Avatar/AppearanceProfileCodec.cs`, `Scripts/Network/AppearanceProfileService.cs`, and `MultiplayerTemplate.Tests/AppearanceProfileCodecTests.cs`; modify `Scripts/Network/PlayerIdentity.cs`, `MultiplayerTemplate.Tests/PlayerIdentityTests.cs`, and `project.godot`.

**Produces:** final `PlayerIdentity.Appearance` parameter; five `appearance_*` roster keys; pure codec; autoloaded profile service exposing `RequestedAppearance` and `SetRequestedAppearance`.

- [ ] Write failing tests for legacy dictionaries resolving to `AvatarAppearance.Default`, full dictionary round-tripping, and malformed profile IDs normalizing to defaults.
- [ ] Run `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~PlayerIdentityTests|FullyQualifiedName~AppearanceProfileCodecTests"`; confirm the new appearance members are missing.
- [ ] Add appearance dictionary conversion to `PlayerIdentity`. Use pure codec key/value conversion; keep `ConfigFile` reads/writes in `AppearanceProfileService` at `user://appearance_profile.cfg`, saving only normalized values. Add the service to `[autoload]`.
- [ ] Run all unit tests and commit: `feat: persist roster-compatible appearance`.

### Task 3: Register and update canonical lobby appearance

**Files:** Modify `Scripts/Network/SessionManager.cs`, `Scripts/Network/NetworkBuildInfo.cs`, and `Tests/NetworkSmoke.cs`.

**Produces:** `RequestAppearance(AvatarAppearance)` and server-only `RequestAppearanceRpc(int bodyColorId, int headId, int backId, int hipsId, int handId)`; registration appends the same five primitive IDs.

- [ ] Extend smoke roles so one client registers a valid non-default appearance; assert server, peer, and late-join roster state match it. Add invalid and Playing-state update attempts that must not mutate the roster.
- [ ] Run `bash Tests/run_multiplayer_smoke.sh`; confirm the new assertion fails before implementation.
- [ ] In `OnConnectedToServer`, append the five properties of `AppearanceProfileService.Instance.RequestedAppearance` to registration. Server registration reconstructs and normalizes the record before creating `PlayerIdentity`. Add a per-peer `RpcRateLimiter` for appearance changes; require server, known sender, and `Lobby`; reconstruct and normalize the five integers; replace only sender appearance; call existing `SyncRoster`. Forget rate state on disconnect and increment protocol version.
- [ ] Grep `rg -n "RegisterPlayer|RequestAppearanceRpc|RequestAppearance" Scripts Tests`, then run unit tests and the smoke harness. Commit: `feat: replicate validated lobby appearances`.

### Task 4: Render roster appearance on avatar rigs

**Files:** Modify `Scripts/Avatar/AvatarFactory.cs`, `Scripts/Avatar/BlockmanRig.cs`, `Scripts/Game/GameWorld.cs`, and `Tests/NetworkSmoke.cs`.

**Produces:** canonical roster appearance applied on spawn and roster change; procedural cosmetic nodes beneath `AvatarSlot` markers.

- [ ] Add real-engine smoke assertions that a `BlockmanRig` reflects the spawned roster appearance and that a roster update applies it to an existing rig; retain the dedicated-server null-rig assertion.
- [ ] Run the smoke harness and confirm those visual assertions fail.
- [ ] Remove peer-hue initialization from `AvatarFactory`; use roster data in `GameWorld` after rig readiness and on `RosterChanged`. In `BlockmanRig.ApplyAppearance`, update body materials, clear only previous cosmetic children, then create deterministic primitive meshes for non-none head/back/hips items and mirrored hand items under both hand markers. Do not alter animation or collision code.
- [ ] Run unit tests and smoke. Commit: `feat: render replicated cosmetics`.

### Task 5: Build and connect the lobby customization panel

**Files:** Create `Scripts/UI/AppearanceCustomizationPanel.cs`; modify `Scripts/UI/Main.cs` and `Tests/NetworkSmoke.cs`.

**Produces:** `ShowForLobby()` / `Hide()` modal API with blocker ID `appearance_customization`; a `Customize Appearance` lobby button.

- [ ] Add an engine test: opening panel in Lobby suppresses gameplay and frees mouse; choosing a valid swatch updates profile intent and roster; leaving Lobby hides the panel and releases the blocker.
- [ ] Run smoke and confirm panel API/assertions fail.
- [ ] Construct the panel in C# using one `OptionButton` per catalogue slot, a close button, and a `SubViewportContainer` preview. Its `SubViewport` owns a `Node3D`, camera, light, and a local `BlockmanRig`, so lobby preview does not depend on a match-spawned player. Open only in `Lobby`; every hide path calls `SetBlocked("appearance_customization", false)`. On a valid selection, apply to this preview rig, update profile intent, and call `SessionManager.RequestAppearance`; roster updates reconcile its controls.
- [ ] Run full unit tests, smoke, and `bash -c 'cd -P /data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate; exec bash Tests/run_all_tests.sh'`. Commit: `feat: add lobby appearance customization`.

### Task 6: Document and prove the completed contract

**Files:** Modify `docs/EXTENDING.md` and `docs/HANDOFF.md`.

- [ ] Document the five roster keys, server-only validation/update rule, procedural/headless behavior, and custom-rig responsibility for `ApplyAppearance` and `AvatarSlot` markers.
- [ ] Mark P2b shipped in handoff and record the resulting protocol version.
- [ ] Run `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj`, `bash Tests/run_multiplayer_smoke.sh`, the physical-path all-tests wrapper, and `git diff --check`.
- [ ] Commit: `docs: document appearance customization`.
