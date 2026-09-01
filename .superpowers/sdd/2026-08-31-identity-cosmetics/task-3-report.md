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
