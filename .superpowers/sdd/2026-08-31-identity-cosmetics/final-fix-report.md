# Final P2b fix report

## Scope

- Added a ClientB engine smoke sequence that sends a cross-slot `RequestAppearanceRpc`, proves
  the canonical roster remains unchanged, then sends a seven-request burst and proves the
  seventh request is refused by the six-per-five-seconds limiter.
- Added an `AppearanceProfileService.LoadProfile` path seam and an engine-only ConfigFile smoke
  check. It writes a valid selection, recreates the service to reload it, then recreates it again
  after malformed stored IDs and verifies canonical defaults.

## TDD evidence

- RED: `dotnet build MultiplayerTemplate.csproj --no-restore` failed with three expected
  `AppearanceProfileService.LoadProfile` missing-member errors from the new engine persistence
  smoke.
- GREEN: adding only the profile-path loading seam made the project build, and the multiplayer
  smoke's primary server/client scenario passed with `profilePersistence=True`,
  `crossSlot=True`, and `rateLimited=True` for ClientB.

## Mutation proof

- Temporarily removing the appearance limiter made ClientB fail with `rateLimited=False`; the
  seventh request changed the canonical roster to the sixth burst appearance.
- Temporarily removing the normalized-vs-requested cross-slot validation made ClientB fail with
  `crossSlot=False` (and therefore `rateLimited=False`); the cross-slot request altered the
  canonical roster before the burst.
- Both production guards were restored immediately after their mutation runs.

## Verification

- `dotnet build MultiplayerTemplate.csproj --no-restore` — passed, zero warnings/errors.
- `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --no-restore` — 128 passed.
- `bash Tests/run_multiplayer_smoke.sh` — primary appearance scenario passed for server, ClientA,
  ClientB, incompatible-build, host-loss, and dedicated roles; ClientB reported both new proofs.
- `git diff --check` — clean.
