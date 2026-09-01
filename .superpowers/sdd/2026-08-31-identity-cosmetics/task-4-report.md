# Task 4 report — render replicated cosmetics

## Scope and files

Modified `Scripts/Avatar/AvatarFactory.cs`, `Scripts/Avatar/BlockmanRig.cs`, `Scripts/Game/GameWorld.cs`, and `Tests/NetworkSmoke.cs`; added this report. Preserved unrelated in-progress changes in `docs/HANDOFF.md` and `docs/superpowers/plans/2026-08-31-identity-cosmetics.md`.

## RED evidence

After adding the real-engine checks and before production changes:

```text
$ timeout 90 bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT FAIL name=ClientA, ... three=True, playing=True, loaded=True
SMOKE CLIENT FAIL name=ClientB, ... three=True, playing=True, loaded=True
SMOKE SERVER FAIL roster=..., three=True, playing=True, avatarGuard=True
MULTIPLAYER HARNESS FAIL
```

The new `CheckRigAppearanceRendering` assertion was the only added success condition; prior `BlockmanRig.ApplyAppearance` changed body material only, so the required cosmetic-marker children were absent. The pre-existing dedicated assertion remained green: `SMOKE DEDICATED PASS (client-only services disabled)`.

## GREEN evidence

```text
$ dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --no-restore
Passed!  - Failed:     0, Passed:   128, Skipped:     0, Total:   128, Duration: 37 ms - MultiplayerTemplate.Tests.dll (net8.0)
```

```text
$ bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
SMOKE HOST LOSS PASS (client returned offline)
SMOKE DEDICATED PASS (client-only services disabled)
SMOKE KICK ROGUE PASS (...)
SMOKE KICK SERVER PASS (...)
SMOKE KICK TARGET PASS (...)
SMOKE EMOTE A PASS (...)
SMOKE EMOTE B PASS (...)
SMOKE EMOTE LATE PASS (...)
SMOKE EMOTE SERVER PASS (...)
MULTIPLAYER HARNESS PASS
```

`dotnet build MultiplayerTemplate.csproj --no-restore` completed with 0 warnings and 0 errors. `git diff --check` passed.

## Implementation

- The factory no longer applies a peer/default appearance; only dedicated headless mode returns no rig.
- `GameWorld` applies canonical `PlayerIdentity.Appearance` after rig readiness at spawn and reapplies it for every existing player on `RosterChanged`.
- `BlockmanRig.ApplyAppearance` normalizes public input, recolors body meshes, removes only `Cosmetic_*` children under `AvatarSlot` markers, and recreates deterministic primitive cosmetics for head, back, hips, and both hands.
- The smoke harness checks spawned roster-driven `BlockmanRig` rendering, a changed existing ready rig, invalid direct rig input falling back to defaults, and dedicated null-rig behavior. Godot-dependent checks remain out of xUnit.

## Self-review and concerns

- Animation and collision setup were not changed.
- Cosmetic nodes are scoped directly below marker nodes; no body nodes or attachment markers are cleared.
- Both hand markers receive the same hand cosmetic, preserving symmetric hands.
- No known remaining concern. The full multiplayer harness was started detached only because this environment limits foreground command execution to roughly 30 seconds; its completed log contains `MULTIPLAYER HARNESS PASS`.
