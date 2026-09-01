# Handoff

Written 2026-08-30. Read this before picking up work on the "friendslop" expansion —
it records the things that are true but not obvious from the code, and the wrong turns
already taken so they are not taken again.

## Where things stand

`master` is at `e4f5f6e`, pushed to `origin`. Working tree clean, no branches, no worktrees.
`NetworkBuildInfo.ProtocolVersion` is **8**.

Verification, both green as of that commit:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj   # 114 passing
bash Tests/run_multiplayer_smoke.sh                                      # 13 scenarios, MULTIPLAYER HARNESS PASS
bash Tests/run_all_tests.sh                                              # both, plus coverage
```

## The plan this is following

The original goal was to expand a server-authoritative 3D multiplayer template with the
social features that make a game playable with friends. That was decomposed into five
phases, in dependency order:

| Phase | Scope | State |
| --- | --- | --- |
| P1 — Avatar & Presence | swappable character rig, locomotion animation, third-person toggle, Tab player list, speaking indicator, AFK, host kick | **shipped** |
| P2a — Expression | radial wheel, emotes, quick chat | **shipped** |
| P2b — Identity | `AvatarAppearance` cosmetics, cosmetic catalogue, customization screen, profile persistence | **shipped** |
| P3 — Voice overhaul | Opus codec, voice activity detection, per-player volume mixer, proximity/global channels, mic settings UI | planned |
| P4 — Social physics | player push/ragdoll, carrying players, prop spawner | planned |
| P5 — World toys | ping markers, spray tags, boombox, photo mode, Steam rich presence | planned |

Designs and plans live in `docs/superpowers/specs/` and `docs/superpowers/plans/`. Read the
spec for a phase before its plan; the plan argues from the spec.

**One decision is already made for P2b and should not be relitigated:** appearance replicates
by extending `PlayerIdentity` and riding the existing roster, exactly as the `Afk` flag does.
It is recorded in the P2a spec. P2b ships the five-ID `AvatarAppearance` on that roster;
`AvatarRig.ApplyAppearance` renders it, and `AvatarSlot` attachment markers mount its
procedural cosmetics.

## Rules this codebase enforces

Violating any of these is a review failure, not a style preference.

- **The server is always peer 1.** Identity comes from `Multiplayer.GetRemoteSenderId()`, never
  from a peer id inside a client payload. Clients send intent; the server validates and
  replicates outcomes.
- **Every client request RPC** goes through `RpcSecurity` (server check, sender check,
  session-state check) and is rate limited via `RpcRateLimiter`.
- **Warnings and errors are unsuppressible** since `a127125`. Any reject a client can trigger
  repeatedly must be throttled through `RpcCooldowns`. This rule has been forgotten three times;
  see issue #2.
- **State must live on a node whose lifetime matches the state's scope.** Documented in
  `EXTENDING.md`. Four P1 bugs shared the opposite shape. The looping emote lives on
  `NetworkPlayer` precisely so it dies with the body.
- **Client-only visual systems no-op on a headless dedicated server**, following the guard in
  `PositionalVoiceChat._Ready`.
- **No binary art assets, no new NuGet packages, no new addons.** Every animation is built in
  C#. If you want to author something in the Godot editor, that is the wrong approach here.

## Traps that cost real time

Each of these was learned the expensive way.

**xUnit cannot construct Godot types.** Doing so crashes the test host process and aborts the
*entire* suite — not one test. Pure policy classes (`LocomotionSolver`, `AfkPolicy`,
`KickPolicy`, `VoiceVolumePolicy`, `EmoteCatalogue`, `QuickChatLines`, `RadialWheelMath`) must
not reference the `Godot` namespace. Anything engine-touching is covered by
`Tests/NetworkSmoke.cs` instead, which runs inside a real engine.

**`const string` is what keeps `EmoteCatalogue` pure.** It references
`ProceduralAnimationLibrary`'s clip-name constants; the compiler inlines compile-time constants,
so no Godot type loads at runtime. Changing those to `static readonly` would silently
reintroduce an engine dependency in a pure class and crash the test host.

**Godot silently ignores an unresolvable animation or bone name.** A typo yields a motionless
character with no error, a green build, and a green test suite. `NetworkSmoke` now asserts every
`EmoteCatalogue` clip resolves against the built library — keep that assertion.

**RPC signature changes fail at runtime, not compile time.** Adding an argument to an `[Rpc]`
method compiles fine and breaks in play. Grep every call site; the smoke harness is what catches
a miss.

**`_Input` versus `_UnhandledInput`.** `_UnhandledInput` runs after the GUI phase, so anything
that must beat camera look has to be in `_Input`. Anything in `_Input` must also check
`LocalInputContext.Instance.GameplaySuppressed`, or it steals keystrokes from chat and the dev
console. Reading `GameplaySuppressed` and calling `SetBlocked` are different decisions — the Tab
panel wants the mouse freed, the wheels must not free it. See issue #3.

**Do not put a git worktree inside the project directory.** A worktree under `.claude/` is a
second complete Godot project; the editor build sees every class twice, stops compiling, and
Godot then reports *every* script as "class could not be found", including untouched addons.
`MultiplayerTemplate.csproj` now excludes `.claude/**` and `.worktrees/**`, but the simplest
answer is a plain feature branch in the main checkout.

**A stale `.godot` cache survives a rebuild.** If Godot reports missing classes after a
structural change, the fix is usually to delete `.godot/` *and* fix whatever broke the compile —
either alone leaves it broken, which makes the cause look mysterious.

**`[InputRemapManager] Rebindable actions: N` never prints.** That addon registers its log module
disabled. Do not ask for it as evidence; count the entries in
`addons/InputRemapManager/InputRemapManager.tscn`'s `RebindableActionsOverride` instead. A new
input action must be added there *and* to `project.godot`, or it works but never appears in the
Controls screen.

**The smoke harness is timing-sensitive under load.** Measured 16/16 green idle, 1/5 under
concurrent load. Do not conclude a regression from a small sample taken while other work is
running — take a clean baseline first. See issue #9.

## Open issues

Filed on `repo.billbert.co/yalt/3DMultiplayerTemplate`:

| # | Title | Kind |
| --- | --- | --- |
| 1 | Windowed client can abort during engine teardown | pre-existing bug, unexplained |
| 2 | Make `RpcSecurity.Reject` throttling structural | design improvement |
| 3 | Document input phase ownership in `EXTENDING.md` | documentation |
| 4 | Tighten `RadialWheelMath` test assertions | test quality |
| 5 | `EmoteCatalogueTests.EveryCatalogueIdResolves` is circular | test quality |
| 6 | `ExpressionWheels.FindLocalPlayer` lacks a `HasMultiplayerPeer` guard | consistency |
| 7 | `ActiveEmote` is assigned twice on the client emote path | cleanup |
| 8 | Emote clips need a visual pass | needs a human |
| 9 | Smoke harness is timing-sensitive under machine load | test infrastructure |

Issues 2 and 3 are the two most worth doing before P2b, because both are about making a rule
structural that is currently a thing each new author has to remember.

Issue 8 needs eyes, not analysis: run two instances, host and join `127.0.0.1`, start a match,
hold **B**. The concern is whether `emote_cheer` reads as a snap and whether `emote_wave` reads
as a wave rather than a diagonal wobble.

## How the work was run

The process that produced P1 and P2a, if you want to continue it:

1. **Brainstorm** the phase (`superpowers:brainstorming`) — classify, ask questions one at a
   time, propose approaches, present the design in sections, write a spec to
   `docs/superpowers/specs/`.
2. **Write a plan** (`superpowers:writing-plans`) — bite-sized TDD tasks with real code, not
   descriptions, each with its own verification commands.
3. **Execute** (`superpowers:subagent-driven-development`) — a fresh subagent per task, a scoped
   review after each, a fix loop, then a final whole-branch review on the most capable model.

What actually caught bugs, in rough order of value:

- **Integration scenarios that can genuinely fail.** The emote tests found two real bugs
  immediately, one of which was a wrong exclusion made two phases earlier and confirmed by a
  reviewer at the time. Reviewers agree with plausible-sounding reasoning; a second process does
  not.
- **Mutation checks.** Asking an implementer to break the code and prove the new test fails,
  then restore it. Used on the AFK boundary, the kick clause, the wheel's angle convention, and
  the catalogue's immutability. Cheap, and it repeatedly distinguished a real test from a
  decorative one.
- **Asking reviewers specific questions** rather than "review this" — "can a one-shot set
  `ActiveEmote`?", "can a client emote for another peer?", "can this cached value go stale?".
  The specific questions found things; the general ones found style.

## Wrong turns already taken

Do not repeat these.

- I claimed the windowed-client crash was caused by a .NET version mismatch. It is not —
  `DOTNET_ROLL_FORWARD=Disable` proves Godot honours the app runtime config and runs on .NET 8
  as intended.
- I claimed player snapshots were unaffected by the match-scene RPC race, reasoning that player
  nodes live under `GameWorld` which exists on every peer. Wrong: the per-peer *child* does not
  exist until its spawn message lands. Fixed in `e0483de`.
- I declared a harness regression from a 4-of-5 sample taken under machine load, then built a
  `ResetSync` theory on top of it. Both wrong. Take the clean baseline first.
- I judged the `IsSceneReady` readiness race "survivable" when its only symptom was log noise.
  It later failed the gate one run in three. Fixed in `cacde6c`.

## P2b resume — 2026-08-31

Branch: `feature/identity-cosmetics`. Approved docs are the P2b spec and plan in
`docs/superpowers/specs/2026-08-31-identity-cosmetics-design.md` and
`docs/superpowers/plans/2026-08-31-identity-cosmetics.md`.

Completed: `eda6410` centralizes reject throttling; `635eca5` completes/reviews pure appearance
policy; `4f38c09` implements roster appearance serialization and local profile persistence;
`c020021` registers and validates canonical lobby appearance; `16b3463` renders roster-driven
procedural cosmetics; and `93a0d39` adds the lobby customization panel. P2b is **shipped**.
`NetworkBuildInfo.ProtocolVersion` is **9**, bumped for the registration RPC signature.

Intentional dirt: the P2b plan is modified with a preflight correction: Task 1 owns minimal
rig/factory compatibility, nonzero IDs are globally unique, and definitions have pure metadata.
Preserve and commit that plan amendment.

The finished contract is five `appearance_*` roster keys. Registration and lobby updates use five
primitive IDs, but the server alone validates, normalizes, and updates the sender's canonical
roster entry. `BlockmanRig` renders cosmetics procedurally; a headless dedicated server builds
no rig. Custom rigs own `ApplyAppearance` and the `AvatarSlot` markers they return from
`GetAttachment`.

The local profile remains `user://appearance_profile.cfg`. Codec tests use pure .NET dictionaries
because Godot dictionaries crash standalone xUnit; engine/RPC behavior belongs in
`Tests/NetworkSmoke.cs`.

Use the physical path for all-tests if logical `/home/...` gets the known Runtime 10 test-host
error: `bash -c 'cd -P /data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate; exec bash Tests/run_all_tests.sh'`.
