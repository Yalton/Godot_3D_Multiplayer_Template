# Task 6 report — document and prove the completed contract

## Scope delivered

- Documented the five canonical roster keys: `appearance_body_color`, `appearance_head`,
  `appearance_back`, `appearance_hips`, and `appearance_hand`.
- Documented the server-only, Lobby-only validation and update flow; invalid or out-of-state
  requests leave roster state unchanged.
- Documented procedural `BlockmanRig` cosmetics, the no-rig headless-server behavior, and the
  custom-rig responsibility for `ApplyAppearance` and all five `AvatarSlot` markers.
- Marked P2b shipped and recorded `NetworkBuildInfo.ProtocolVersion` 9 in the handoff.
- Preserved the pre-existing P2b plan amendment and included it in this task's commit.

## Files

- `docs/EXTENDING.md`
- `docs/HANDOFF.md`
- `docs/superpowers/plans/2026-08-31-identity-cosmetics.md` (pre-existing amendment preserved)
- `.superpowers/sdd/2026-08-31-identity-cosmetics/task-6-report.md`

## Verification

```text
$ dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj
Passed!  - Failed: 0, Passed: 128, Skipped: 0, Total: 128

$ bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
SMOKE HOST LOSS PASS (client returned offline)
SMOKE DEDICATED PASS (client-only services disabled)

$ bash -c 'cd -P /data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate; exec bash Tests/run_all_tests.sh'
Passed!  - Failed: 0, Passed: 128, Skipped: 0, Total: 128
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
SMOKE HOST LOSS PASS (client returned offline)
SMOKE DEDICATED PASS (client-only services disabled)

$ git diff --check
exit 0
```

## Self-review

- Compared the docs against `AppearanceProfileCodec`, `SessionManager`, `BlockmanRig`, and
  `AvatarFactory` before editing.
- Confirmed all five key names and the protocol value from source.
- Kept the documentation to the shipped implementation; it does not promise custom-rig mesh
  generation or appearance changes outside the Lobby.
- Re-read the final diff and confirmed the intentional plan amendment remains intact.

## Concerns

None. The smoke harness remains timing-sensitive under concurrent machine load as documented in
the handoff, but this clean run completed successfully.
