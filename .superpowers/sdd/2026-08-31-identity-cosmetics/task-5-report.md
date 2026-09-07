# Task 5 report: lobby appearance customization

## RED

Added the `NetworkSmoke` contract before production code, then ran:

```text
$ dotnet build MultiplayerTemplate.csproj --no-restore
Tests/NetworkSmoke.cs(72,13): error CS0246: The type or namespace name
'AppearanceCustomizationPanel' could not be found
Build FAILED. 0 Warning(s), 1 Error(s)
```

The missing panel type was the expected failure.

## GREEN

Implemented the minimum panel and reran:

```text
$ dotnet build MultiplayerTemplate.csproj --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

$ dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --no-restore --verbosity minimal
Passed!  Failed: 0, Passed: 128, Skipped: 0, Total: 128

$ bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
SMOKE HOST LOSS PASS (client returned offline)
SMOKE DEDICATED PASS (client-only services disabled)
```

The ClientA smoke contract opens `AppearanceCustomizationPanel` in a real Lobby,
asserts the input blocker/visible cursor/preview rig, selects Red through the actual
`OptionButton` signal, confirms profile plus authoritative roster propagation, and
confirms the panel releases its blocker when the session enters Playing.

## Files

- `Scripts/UI/AppearanceCustomizationPanel.cs` — C# modal with `ShowForLobby()` / `Hide()`,
  five catalogue-backed selectors, close button, and standalone `SubViewport` preview rig.
- `Scripts/UI/Main.cs` — panel ownership and lobby `Customize Appearance` entry.
- `Tests/NetworkSmoke.cs` — engine smoke panel API and lifecycle assertions.

## Self-review

- Uses blocker ID exactly `appearance_customization`; every panel hide path removes it.
- `ShowForLobby()` refuses all non-Lobby session states.
- The preview uses only a private `SubViewport`/`Node3D`/camera/light/`BlockmanRig`; it does
  not query `GameWorld` or player bodies.
- Selections normalize, update persisted local intent, apply to preview, request server
  authority, and accepted roster updates reconcile the controls.
- No binary assets, add-ons, or packages added.

## Concerns

The headless smoke can only assert `Input.MouseMode.Visible` (its normal non-captured mode);
the stronger portable proof is `LocalInputContext.GameplaySuppressed` while open and false
after automatic close. The normal desktop path uses the same blocker and therefore calls
`RefreshMouseMode()` to show/free the cursor.

## Review fix round 1: teardown ordering

### RED

Split the original combined panel flag into open/selection and teardown gates, but
intentionally left teardown unset. This proves the prior state-change callback was
sampling too early and that an open/selection pass could mask a failed close:

```text
$ dotnet build MultiplayerTemplate.csproj --no-restore && bash Tests/run_multiplayer_smoke.sh
Build succeeded. 0 Warning(s), 0 Error(s)
SMOKE CLIENT FAIL name=ClientA, ... appearancePanelOpenSelection=True,
appearancePanelTeardown=False
MULTIPLAYER HARNESS FAIL
```

### GREEN

`NetworkSmoke` now schedules the teardown assertion for the next process frame after
entering Playing. This guarantees the panel's own later `StateChanged` subscriber has
run its `Hide()` path before the smoke samples `IsOpen` and
`LocalInputContext.GameplaySuppressed`. The final ClientA gate requires both the
open/selection result and this separate teardown result.

```text
$ dotnet build MultiplayerTemplate.csproj --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

$ dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --no-restore --verbosity minimal
Passed!  Failed: 0, Passed: 128, Skipped: 0, Total: 128

$ bash Tests/run_multiplayer_smoke.sh
SMOKE CLIENT PASS (ClientA, synchronized scene and clock)
SMOKE CLIENT PASS (ClientB, synchronized scene and clock)
SMOKE INCOMPATIBLE PASS (rejected before registration)
SMOKE SERVER PASS (3 peers, synchronized load, playing)
```

The smoke process then completed its remaining scenarios; `git diff --check` was also
clean.
