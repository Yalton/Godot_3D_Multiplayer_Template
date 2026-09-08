# Task 1 Report: Pure Voice Transport Policy

Commit: `900d511` (`feat: define voice transport policies`)

## Outcome

Task 1 is complete. The pure voice transport layer now defines the fixed 16 kHz / 320-sample wire contract, frame assembly, RMS VAD with hangover, proximity/global routing, wrapping sequence freshness, and the per-peer frame/byte limiter. The implementation stays engine-free and does not reference `Godot`.

## RED Evidence

I wrote the task-first tests before any production code and confirmed they failed for the expected reason: missing voice types.

Command:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~VoiceWireTests|FullyQualifiedName~VoiceFrameAssemblerTests|FullyQualifiedName~VoiceActivityDetectorTests|FullyQualifiedName~VoiceRoutingPolicyTests"
```

Key failure output:

```text
error CS0246: The type or namespace name 'VoiceActivityDetector' could not be found
error CS0246: The type or namespace name 'VoiceFrameAssembler' could not be found
error CS0103: The name 'VoiceWirePolicy' does not exist in the current context
error CS0246: The type or namespace name 'VoicePacket' could not be found
error CS0246: The type or namespace name 'VoiceByteLimiter' could not be found
error CS0246: The type or namespace name 'VoicePoint' could not be found
error CS0103: The name 'VoiceRoutingPolicy' does not exist in the current context
```

That is the expected red state for this task.

## GREEN Evidence

Focused voice tests:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~VoiceWireTests|FullyQualifiedName~VoiceFrameAssemblerTests|FullyQualifiedName~VoiceActivityDetectorTests|FullyQualifiedName~VoiceRoutingPolicyTests"
```

Result:

```text
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13
```

Full test project:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj
```

Result:

```text
Passed!  - Failed:     0, Passed:   141, Skipped:     0, Total:   141
```

## Files Changed

- `Scripts/Communication/VoiceWire.cs`
- `Scripts/Communication/VoiceFrameAssembler.cs`
- `Scripts/Communication/VoiceActivityDetector.cs`
- `Scripts/Communication/VoiceRoutingPolicy.cs`
- `MultiplayerTemplate.Tests/VoiceWireTests.cs`
- `MultiplayerTemplate.Tests/VoiceFrameAssemblerTests.cs`
- `MultiplayerTemplate.Tests/VoiceActivityDetectorTests.cs`
- `MultiplayerTemplate.Tests/VoiceRoutingPolicyTests.cs`

## Self-Review

- `VoiceWirePolicy` exposes the fixed wire constants the rest of the voice stack will consume.
- `VoiceWirePolicy.IsValid` rejects wrong versions, invalid channels, empty payloads, and payloads above 512 bytes.
- `VoiceWirePolicy.IsNewerSequence` uses wrap-aware unsigned comparison so `uint.MaxValue -> 0` is treated as fresh.
- `VoiceByteLimiter` enforces the 50-frame and 30 KiB window per peer and supports `Forget(peerId)`.
- `VoiceFrameAssembler` retains partial samples across pushes and only emits complete 320-sample frames.
- `VoiceActivityDetector` computes RMS energy and keeps activity alive for the requested hangover frames after a loud frame.
- `VoiceRoutingPolicy` returns deterministic recipient sets for both proximity and global routing.

## Concerns

- `VoiceByteLimiter` is keyed by caller-supplied millisecond timestamps; later network integration must feed it a monotonic clock.
- Sequence freshness is currently a pure comparison helper, which is enough for Task 1 but may be wrapped by a later per-peer tracker if Task 5 needs stateful stale/duplicate rejection.
- The current routing policy is intentionally deterministic and sorted, which is good for tests; later server code should preserve that ordering when relaying packets.

## Fix Round 1

### Findings Addressed

1. `Scripts/Communication/VoiceWire.cs` now uses `long` arithmetic before comparing or mutating the frame and byte budget, so oversized requests cannot wrap negative and bypass the limiter after prior consumption.
2. `MultiplayerTemplate.Tests/VoiceActivityDetectorTests.cs` now proves the full hangover release path: loud frame, two silent hangover frames, then inactive.
3. `MultiplayerTemplate.Tests/VoiceWireTests.cs` now proves the limiter window resets at exactly `1_000` ms.
4. `MultiplayerTemplate.Tests/VoiceWireTests.cs` now proves an overflow-sized frame request is rejected.

### Changed Files

- `Scripts/Communication/VoiceWire.cs`
- `MultiplayerTemplate.Tests/VoiceActivityDetectorTests.cs`
- `MultiplayerTemplate.Tests/VoiceWireTests.cs`

### Focused Verification

Command:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~VoiceWireTests|FullyQualifiedName~VoiceFrameAssemblerTests|FullyQualifiedName~VoiceActivityDetectorTests|FullyQualifiedName~VoiceRoutingPolicyTests"
```

Result:

```text
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15
```

### Why This Fix Is Correct

- The limiter now checks the post-addition budget in 64-bit space before updating state, so the comparison cannot be bypassed by `int` overflow.
- The new VAD test covers the release boundary that the prior test missed, which proves the hangover counter decrements as intended and then goes inactive.
- The new time-based limiter test verifies the window boundary is inclusive at `1_000` ms, matching the intended one-second budget window.
- The overflow-sized request test exercises the exact failure case from review instead of relying on a smaller boundary case.

## Fix Round 2

### Findings Addressed

1. Added a byte-overflow regression proof in `MultiplayerTemplate.Tests/VoiceWireTests.cs` that first consumes byte budget and then asserts `TryConsume(..., bytes: int.MaxValue, ...)` rejects. This exercises the byte cap's `long` arithmetic directly, not just the frame path.

### Changed Files

- `MultiplayerTemplate.Tests/VoiceWireTests.cs`

### Focused Verification

Command:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~VoiceWireTests|FullyQualifiedName~VoiceFrameAssemblerTests|FullyQualifiedName~VoiceActivityDetectorTests|FullyQualifiedName~VoiceRoutingPolicyTests"
```

Result:

```text
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16
```

### Why This Fix Is Correct

- The new test uses the byte budget after prior consumption, so it reaches the path where unchecked `int` addition would have wrapped and bypassed the cap.
- The existing `long` arithmetic in `VoiceByteLimiter` now has direct regression coverage for both the frame and byte budgets.
