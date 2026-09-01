# Task 2 — Review Fix Round 1 Report

Date: 2026-09-01

## Scope

- Load `user://appearance_profile.cfg` before reading local appearance intent; an absent,
  malformed, or otherwise failed load clears the fresh `ConfigFile` and therefore resolves to
  the existing defaults.
- Make omitted `PlayerIdentity` appearance arguments resolve to
  `AvatarAppearance.Default`, while retaining explicit appearances.
- Cover five appearance keys, codec round-tripping, partial defaults in pure xUnit tests, and
  identity roster round-trip/legacy defaults in the existing Godot smoke check.

## TDD evidence

### RED

Command:

```text
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~PlayerIdentityTests|FullyQualifiedName~AppearanceProfileCodecTests"
```

Output:

```text
Failed MultiplayerTemplate.Tests.PlayerIdentityTests.OmittedAppearanceUsesCanonicalDefault [2 ms]
Assert.Equal() Failure: Values differ
Expected: AvatarAppearance { BodyColorId = 1, HeadId = 0, BackId = 0, HipsId = 0, HandId = 0 }
Actual:   AvatarAppearance { BodyColorId = 0, HeadId = 0, BackId = 0, HipsId = 0, HandId = 0 }
Failed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 55 ms
```

### GREEN

Command:

```text
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~PlayerIdentityTests|FullyQualifiedName~AppearanceProfileCodecTests"
```

Output:

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 39 ms
```

## Verification

```text
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj
Passed!  - Failed:     0, Passed:   126, Skipped:     0, Total:   126, Duration: 46 ms

bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
SMOKE HOST LOSS PASS (client returned offline)
SMOKE DEDICATED PASS (client-only services disabled)
```

The smoke process completed after the command-output yield; it was confirmed no longer running.

## Files changed

- `Scripts/Network/AppearanceProfileService.cs`
- `Scripts/Network/PlayerIdentity.cs`
- `MultiplayerTemplate.Tests/PlayerIdentityTests.cs`
- `MultiplayerTemplate.Tests/AppearanceProfileCodecTests.cs`
- `Tests/NetworkSmoke.cs`

## Self-review

- `ConfigFile.Load` now precedes all `GetValue` calls, and a non-`Ok` result clears state.
- The compatibility overload preserves all existing seven-argument call sites and assigns the
  canonical default only when appearance is omitted; the eight-argument overload retains the
  supplied appearance unchanged.
- xUnit tests remain engine-free. The Godot dictionary boundary is covered by the engine smoke
  identity check, including full roster round-trip, legacy roster default, and partial appearance
  defaulting.
- `git diff --check` was clean. The required read-only review identified the identity-boundary
  coverage gap; it was addressed in `Tests/NetworkSmoke.cs`.

## Concerns

- An isolated profile-reload engine test is not added: the service intentionally owns the fixed
  `user://appearance_profile.cfg` path, so exercising a second live service session would write
  to the developer's real local profile. The production fix directly uses the Godot load result
  and explicitly defaults on failure; pure xUnit must not construct Godot types.
