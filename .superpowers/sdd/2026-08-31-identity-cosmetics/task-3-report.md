# Task 3 report: canonical lobby appearance replication

## Implementation

- `SessionManager` registers the five primitive appearance IDs from `AppearanceProfileService.Instance.RequestedAppearance`; the server reconstructs and normalizes them before creating the roster identity.
- Added `RequestAppearance(AvatarAppearance)` and authority-side `RequestAppearanceRpc(int bodyColorId, int headId, int backId, int hipsId, int handId)`. The RPC targets peer 1, requires server/known-sender/lobby state, rate-limits per peer, rejects non-normalized IDs through the existing throttled `RpcSecurity.Reject`, replaces only the sender's appearance, then uses the existing `SyncRoster` path.
- Disconnect cleanup already forgets the shared per-peer limiter state. Protocol version is 9.
- `NetworkSmoke` sets ClientA's persisted requested profile to a valid non-default appearance before connecting; server, ClientA, and late-joining ClientB all assert the same roster appearance. ClientA also proves an invalid lobby update and a Playing-state update leave its roster appearance unchanged.

## TDD evidence

- RED: after adding smoke calls but before production code, `dotnet build` failed with `CS1061`: `SessionManager` had no `RequestAppearance` definition (two smoke call sites).
- GREEN: `dotnet build` succeeded with 0 warnings and 0 errors after the implementation.
- The smoke harness primary scenario then printed `SMOKE CLIENT PASS` for ClientA and ClientB and `SMOKE SERVER PASS`; this includes the new registration, late-join, invalid-update, and Playing-state assertions.

## Verification

- `dotnet build` — passed, 0 warnings/errors.
- `bash Tests/run_multiplayer_smoke.sh` — primary multiplayer scenario passed (server, ClientA, ClientB, incompatible, host-loss, dedicated output observed). The command runner has a 30-second cap, so it returned before the later kick/emote sub-scenarios could print in this invocation.
- `bash Tests/run_all_tests.sh` — blocked before test execution by the existing environment mismatch: the test host cannot load `System.Runtime, Version=10.0.0.0`. This command resolves its project under `/home/yalt/Documents/...`, not this checkout.
- `rg -n "RegisterPlayer|RequestAppearanceRpc|RequestAppearance" Scripts Tests` — passed; confirms the registration and RPC call sites are confined to the intended network/smoke paths.
- `git diff --check` — passed.

## Files changed

- `Scripts/Network/SessionManager.cs`
- `Scripts/Network/NetworkBuildInfo.cs`
- `Tests/NetworkSmoke.cs`

## Self-review and concerns

- No rendering/UI work was introduced.
- Registration deliberately normalizes legacy/malformed values; lobby updates reject malformed IDs so an invalid request cannot mutate canonical roster state.
- Full xUnit and the tail of the smoke suite need a rerun in an environment without the 30-second command cap and with a compatible .NET test host.

## Fix Round 1

### Review fixes

- Host `RequestAppearance` now enters the same `RequestAppearanceServer` gate as the RPC path. That gate requires server authority, a registered sender, Lobby state, per-peer rate-limit allowance, and rejects malformed appearance IDs through `RpcSecurity.Reject`. Remote RPC senders are still derived with `GetRemoteSenderId`; host calls use the authoritative local unique ID.
- `AddHost` now normalizes `AppearanceProfileService.Instance.RequestedAppearance` and stores it in the initial host `PlayerIdentity`.
- Smoke now records a ClientA Playing-state request only from the `StateChanged`/roster flow after `State == Playing`, and asserts the canonical appearance remains unchanged. It also stores a non-default host profile before hosting and proves a host request in Playing is rejected.

### TDD evidence

- RED: after tightening the smoke checks and before production changes, `dotnet build && bash Tests/run_multiplayer_smoke.sh` built successfully but failed the server scenario. The final host roster showed default appearance (`appearance_body_color: 1`) despite the saved red host profile; the pre-fix direct host request also changed it during Playing.
- GREEN: after the shared gate and `AddHost` changes, `dotnet build` passed (0 warnings/errors) and the complete smoke harness passed.

### Verification

- `dotnet build` — passed, 0 warnings/errors.
- `dotnet test /data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate/MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --settings .../Tests/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory /tmp/task3-test-results --verbosity minimal` — passed: 128/128.
- Full multiplayer smoke was started in a detached session and polled past the command runner's 30-second foreground cap. It finished with `MULTIPLAYER HARNESS PASS`, including server/client appearance coverage plus host-loss, dedicated, kick, and emote scenarios.
- `rg -n "RegisterPlayer|RequestAppearanceRpc|RequestAppearance" Scripts Tests` — passed.
- `git diff --check` — passed.

### Files changed

- `Scripts/Network/SessionManager.cs`
- `Tests/NetworkSmoke.cs`
- `.superpowers/sdd/2026-08-31-identity-cosmetics/task-3-report.md`

### Self-review and concerns

- Host and remote requests now share state, registration, rate-limit, normalization, and roster-sync behavior; rendering/UI remains out of scope.
- The report's original concern about unit tests and full smoke is resolved when commands use this physical checkout and the detached smoke invocation avoids the foreground cap.
