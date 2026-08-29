# Avatar and Presence (Friendslop P1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give players a visible animated body, a third-person view, and in-match social presence (speaking, AFK, ping, mute, per-peer volume, host kick), without adding art assets or third-party dependencies.

**Architecture:** A swappable `AvatarRig` node replaces the capsule mesh; the default rig is built in C# from a `Skeleton3D`, `BoneAttachment3D` boxes, and procedurally generated `Animation` resources driven by an `AnimationTree`. Locomotion animation is derived locally on every peer from data it already has, so it costs no round trip; discrete presence state (AFK) is server-authoritative and rides the existing replicated roster. All decision rules live in pure, Godot-free policy classes so they are unit-testable in the existing xUnit project.

**Tech Stack:** Godot 4.6, C# / .NET 8 (`Godot.NET.Sdk/4.6.0`), xUnit 2.9.3 + Coverlet for pure-policy tests, `Tests/NetworkSmoke.cs` for multi-process integration.

**Spec:** `docs/superpowers/specs/2026-08-28-avatar-presence-design.md`

## Global Constraints

- Godot 4.6, C# only. Target framework `net8.0`. `Nullable` and `ImplicitUsings` are enabled project-wide.
- **No new NuGet packages, no new addons, no binary art assets.** The default avatar is constructed in code.
- **Pure policy classes must not reference the `Godot` namespace.** The xUnit project runs without a Godot engine instance; use `System.MathF` and plain types. Classes that touch `Node`, `ConfigFile`, `Color`, or `Vector3` are engine-side and are covered by `Tests/NetworkSmoke.cs`, not xUnit.
- Every file keeps its own existing indentation. `Scripts/UI/Main.cs` uses **tabs**; every other file under `Scripts/` uses **4 spaces**.
- The server is always multiplayer peer `1`. Derive identity from `Multiplayer.GetRemoteSenderId()`, never from a client-supplied peer id.
- Every client request RPC must pass through `RpcSecurity` (server check, sender check, session-state check) and be rate limited via `RpcRateLimiter`.
- `NetworkBuildInfo.ProtocolVersion` is incremented exactly once for this whole phase, in Task 5 (from 5 to 6 — a pre-task fix on this branch already moved it from 4 to 5 for an unrelated ENet channel change). Do not bump it again in later tasks.
- Client-only visual systems must no-op when `DisplayServer.GetName() == "headless"`, following the guard in `Scripts/Communication/PositionalVoiceChat.cs:29`.
- Full suite entry point: `bash Tests/run_all_tests.sh`.

### Deviations from the spec, already decided

Three details were resolved during planning and supersede the spec text:

1. `AvatarRig.SetLocomotion` takes the **solved** `LocomotionResult`, not raw `(speed, grounded, verticalVelocity)`. A user-supplied rig should not have to re-implement the thresholds.
2. `ReceiveSnapshot` gains a **`bool grounded`** field. Remote players never call `MoveAndSlide`, so their `IsOnFloor()` is meaningless and jump/fall animation would be wrong without it. One bool per player per snapshot at 20 Hz.
3. At the apex of a jump (vertical velocity between the rise and fall thresholds) the character reads as **falling**. This avoids carrying jump/fall history in the solver.

---

### Task 1: LocomotionSolver

**Files:**
- Create: `Scripts/Avatar/LocomotionSolver.cs`
- Test: `MultiplayerTemplate.Tests/LocomotionSolverTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum MultiplayerTemplate.Avatar.LocomotionState { Idle, Walk, Run, Jump, Fall, Land }`; `readonly record struct MultiplayerTemplate.Avatar.LocomotionResult(LocomotionState State, float Blend, bool LandTriggered)`; `static LocomotionResult LocomotionSolver.Solve(float planarSpeed, bool grounded, float verticalVelocity, float airborneSeconds, bool wasGrounded)`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/LocomotionSolverTests.cs`:

```csharp
using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class LocomotionSolverTests
{
    [Theory]
    [InlineData(0f, LocomotionState.Idle)]
    [InlineData(0.149f, LocomotionState.Idle)]
    [InlineData(0.15f, LocomotionState.Walk)]
    [InlineData(2.999f, LocomotionState.Walk)]
    [InlineData(3f, LocomotionState.Run)]
    [InlineData(6f, LocomotionState.Run)]
    public void ClassifiesGroundSpeed(float speed, LocomotionState expected)
    {
        var result = LocomotionSolver.Solve(speed, true, 0f, 0f, true);
        Assert.Equal(expected, result.State);
        Assert.False(result.LandTriggered);
    }

    [Theory]
    [InlineData(5f, LocomotionState.Jump)]
    [InlineData(0.51f, LocomotionState.Jump)]
    [InlineData(0.5f, LocomotionState.Fall)]
    [InlineData(0f, LocomotionState.Fall)]
    [InlineData(-9f, LocomotionState.Fall)]
    public void ClassifiesAirborneByVerticalVelocity(float verticalVelocity, LocomotionState expected)
    {
        var result = LocomotionSolver.Solve(4f, false, verticalVelocity, 0.4f, false);
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void LandFiresOnceWhenGroundIsRegainedAfterLongEnoughFlight()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 0.25f, false);
        Assert.Equal(LocomotionState.Land, result.State);
        Assert.True(result.LandTriggered);
    }

    [Fact]
    public void ShortHopsDoNotTriggerLand()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 0.24f, false);
        Assert.Equal(LocomotionState.Idle, result.State);
        Assert.False(result.LandTriggered);
    }

    [Fact]
    public void StayingGroundedNeverTriggersLand()
    {
        var result = LocomotionSolver.Solve(0f, true, 0f, 10f, true);
        Assert.Equal(LocomotionState.Idle, result.State);
        Assert.False(result.LandTriggered);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(3f, 0.5f)]
    [InlineData(6f, 1f)]
    [InlineData(20f, 1f)]
    public void BlendIsSpeedNormalisedToRunSpeed(float speed, float expected)
    {
        Assert.Equal(expected, LocomotionSolver.Solve(speed, true, 0f, 0f, true).Blend, 3);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~LocomotionSolverTests --verbosity minimal`
Expected: FAIL to compile — `The type or namespace name 'Avatar' does not exist in the namespace 'MultiplayerTemplate'`.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Avatar/LocomotionSolver.cs`:

```csharp
namespace MultiplayerTemplate.Avatar;

public enum LocomotionState
{
    Idle,
    Walk,
    Run,
    Jump,
    Fall,
    Land
}

/// <summary>
/// One frame of cosmetic locomotion animation state. <see cref="Blend"/> drives the
/// idle/walk/run blend space; <see cref="LandTriggered"/> is a one-frame edge.
/// </summary>
public readonly record struct LocomotionResult(LocomotionState State, float Blend, bool LandTriggered);

/// <summary>
/// Derives cosmetic locomotion state from motion every peer already has. Pure and
/// engine-free so it can be unit tested; must not reference the Godot namespace.
/// </summary>
public static class LocomotionSolver
{
    public const float IdleSpeed = 0.15f;
    public const float WalkSpeed = 3f;
    public const float RunSpeed = 6f;
    public const float RiseVelocity = 0.5f;
    public const float LandAirborneSeconds = 0.25f;

    public static LocomotionResult Solve(
        float planarSpeed, bool grounded, float verticalVelocity, float airborneSeconds, bool wasGrounded)
    {
        var blend = Math.Clamp(planarSpeed / RunSpeed, 0f, 1f);
        if (!grounded)
        {
            // At the apex the character reads as falling; carrying jump history here
            // would make the solver stateful for no visual gain.
            var airborne = verticalVelocity > RiseVelocity ? LocomotionState.Jump : LocomotionState.Fall;
            return new LocomotionResult(airborne, blend, false);
        }
        if (!wasGrounded && airborneSeconds >= LandAirborneSeconds)
            return new LocomotionResult(LocomotionState.Land, blend, true);
        var ground = planarSpeed < IdleSpeed
            ? LocomotionState.Idle
            : planarSpeed < WalkSpeed
                ? LocomotionState.Walk
                : LocomotionState.Run;
        return new LocomotionResult(ground, blend, false);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~LocomotionSolverTests --verbosity minimal`
Expected: PASS, 20 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Avatar/LocomotionSolver.cs MultiplayerTemplate.Tests/LocomotionSolverTests.cs
git commit -m "feat(avatar): add pure locomotion state solver"
```

---

### Task 2: AfkPolicy

**Files:**
- Create: `Scripts/Game/AfkPolicy.cs`
- Test: `MultiplayerTemplate.Tests/AfkPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MultiplayerTemplate.Game.AfkPolicy` with `const double DefaultThresholdSeconds = 90`, `const double ExitSeconds = 0.5`, `static bool Evaluate(double secondsSinceInput, double thresholdSeconds, bool currentlyAfk)`, `static bool ShouldKick(double secondsSinceInput, double kickThresholdSeconds)`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/AfkPolicyTests.cs`:

```csharp
using MultiplayerTemplate.Game;

namespace MultiplayerTemplate.Tests;

public sealed class AfkPolicyTests
{
    [Fact]
    public void BecomesAfkAtTheThreshold()
    {
        Assert.False(AfkPolicy.Evaluate(89.9, 90, false));
        Assert.True(AfkPolicy.Evaluate(90, 90, false));
    }

    [Fact]
    public void StaysAfkUntilFreshInputResetsTheTimer()
    {
        Assert.True(AfkPolicy.Evaluate(120, 90, true));
        Assert.True(AfkPolicy.Evaluate(1.0, 90, true));
        Assert.False(AfkPolicy.Evaluate(0.0, 90, true));
    }

    [Fact]
    public void NonPositiveThresholdDisablesAfk()
    {
        Assert.False(AfkPolicy.Evaluate(9999, 0, false));
        Assert.False(AfkPolicy.Evaluate(9999, -1, true));
    }

    [Fact]
    public void KickOnlyWhenAKickThresholdIsConfigured()
    {
        Assert.False(AfkPolicy.ShouldKick(9999, 0));
        Assert.False(AfkPolicy.ShouldKick(299, 300));
        Assert.True(AfkPolicy.ShouldKick(300, 300));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~AfkPolicyTests --verbosity minimal`
Expected: FAIL to compile — `AfkPolicy` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Game/AfkPolicy.cs`:

```csharp
namespace MultiplayerTemplate.Game;

/// <summary>
/// Idle-detection rule. Pure and engine-free so it can be unit tested; must not
/// reference the Godot namespace.
/// </summary>
public static class AfkPolicy
{
    public const double DefaultThresholdSeconds = 90;

    /// <summary>
    /// Once flagged, a player stays away until input actually arrives and resets the
    /// timer. Without this a very small threshold would flap on and off every frame.
    /// </summary>
    public const double ExitSeconds = 0.5;

    public static bool Evaluate(double secondsSinceInput, double thresholdSeconds, bool currentlyAfk)
    {
        if (thresholdSeconds <= 0) return false;
        return currentlyAfk
            ? secondsSinceInput > ExitSeconds
            : secondsSinceInput >= thresholdSeconds;
    }

    public static bool ShouldKick(double secondsSinceInput, double kickThresholdSeconds) =>
        kickThresholdSeconds > 0 && secondsSinceInput >= kickThresholdSeconds;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~AfkPolicyTests --verbosity minimal`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Game/AfkPolicy.cs MultiplayerTemplate.Tests/AfkPolicyTests.cs
git commit -m "feat(presence): add pure AFK detection policy"
```

---

### Task 3: Voice volume policy and speaking tracker

**Files:**
- Create: `Scripts/Communication/VoicePolicy.cs`
- Test: `MultiplayerTemplate.Tests/VoicePolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MultiplayerTemplate.Communication.VoiceVolumePolicy` with `const float Min = 0f`, `const float Max = 2f`, `const float Default = 1f`, `static float Clamp(float linear)`, `static float ToDecibels(float linear)`; `sealed class MultiplayerTemplate.Communication.SpeakingTracker` with `const double DecaySeconds = 0.25`, `void NotePacket(long peerId, double now)`, `bool IsSpeaking(long peerId, double now)`, `void Remove(long peerId)`, `void Clear()`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/VoicePolicyTests.cs`:

```csharp
using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class VoiceVolumePolicyTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 2f)]
    [InlineData(9f, 2f)]
    public void ClampsToSupportedRange(float input, float expected)
    {
        Assert.Equal(expected, VoiceVolumePolicy.Clamp(input), 3);
    }

    [Fact]
    public void UnityGainIsZeroDecibels()
    {
        Assert.Equal(0f, VoiceVolumePolicy.ToDecibels(1f), 3);
    }

    [Fact]
    public void HalfGainIsAboutMinusSixDecibels()
    {
        Assert.Equal(-6.02f, VoiceVolumePolicy.ToDecibels(0.5f), 2);
    }

    [Fact]
    public void SilenceIsFloored()
    {
        Assert.Equal(-80f, VoiceVolumePolicy.ToDecibels(0f), 3);
    }
}

public sealed class SpeakingTrackerTests
{
    [Fact]
    public void PeerIsSpeakingUntilTheDecayWindowElapses()
    {
        var tracker = new SpeakingTracker();
        tracker.NotePacket(7, 10.0);
        Assert.True(tracker.IsSpeaking(7, 10.0));
        Assert.True(tracker.IsSpeaking(7, 10.25));
        Assert.False(tracker.IsSpeaking(7, 10.26));
    }

    [Fact]
    public void UnknownPeerIsNotSpeaking()
    {
        Assert.False(new SpeakingTracker().IsSpeaking(7, 0.0));
    }

    [Fact]
    public void RemovedPeerIsNotSpeaking()
    {
        var tracker = new SpeakingTracker();
        tracker.NotePacket(7, 10.0);
        tracker.Remove(7);
        Assert.False(tracker.IsSpeaking(7, 10.0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~VoicePolicyTests --verbosity minimal`
Expected: FAIL to compile — `VoiceVolumePolicy` and `SpeakingTracker` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Communication/VoicePolicy.cs`:

```csharp
namespace MultiplayerTemplate.Communication;

/// <summary>
/// Per-peer listening gain rules. Pure and engine-free so it can be unit tested;
/// must not reference the Godot namespace.
/// </summary>
public static class VoiceVolumePolicy
{
    public const float Min = 0f;
    public const float Max = 2f;
    public const float Default = 1f;
    private const float SilenceDecibels = -80f;

    public static float Clamp(float linear) => Math.Clamp(linear, Min, Max);

    public static float ToDecibels(float linear)
    {
        var clamped = Clamp(linear);
        return clamped <= 0.0001f ? SilenceDecibels : 20f * MathF.Log10(clamped);
    }
}

/// <summary>
/// Receive-side observation of who is talking, derived from voice packet arrival.
/// Deliberately local: this flips several times a second and has no business on the
/// replicated roster.
/// </summary>
public sealed class SpeakingTracker
{
    public const double DecaySeconds = 0.25;
    private readonly Dictionary<long, double> _lastPacketSeconds = new();

    public void NotePacket(long peerId, double now) => _lastPacketSeconds[peerId] = now;

    public bool IsSpeaking(long peerId, double now) =>
        _lastPacketSeconds.TryGetValue(peerId, out var last) && now - last <= DecaySeconds;

    public void Remove(long peerId) => _lastPacketSeconds.Remove(peerId);

    public void Clear() => _lastPacketSeconds.Clear();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~VoicePolicyTests --verbosity minimal`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Communication/VoicePolicy.cs MultiplayerTemplate.Tests/VoicePolicyTests.cs
git commit -m "feat(voice): add per-peer volume policy and speaking tracker"
```

---

### Task 4: KickPolicy

**Files:**
- Create: `Scripts/Network/KickPolicy.cs`
- Test: `MultiplayerTemplate.Tests/KickPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MultiplayerTemplate.Network.KickPolicy` with `static bool CanKick(long senderPeerId, long targetPeerId, bool senderIsHost, bool targetIsKnown)`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/KickPolicyTests.cs`:

```csharp
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Tests;

public sealed class KickPolicyTests
{
    [Fact]
    public void HostMayKickAKnownClient()
    {
        Assert.True(KickPolicy.CanKick(1, 42, true, true));
    }

    [Fact]
    public void NonHostMayNotKick()
    {
        Assert.False(KickPolicy.CanKick(42, 43, false, true));
    }

    [Fact]
    public void UnknownTargetIsRejected()
    {
        Assert.False(KickPolicy.CanKick(1, 42, true, false));
    }

    [Fact]
    public void HostMayNotKickItself()
    {
        Assert.False(KickPolicy.CanKick(1, 1, true, true));
    }

    [Fact]
    public void ServerPeerIsNeverAValidTarget()
    {
        Assert.False(KickPolicy.CanKick(99, 1, true, true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~KickPolicyTests --verbosity minimal`
Expected: FAIL to compile — `KickPolicy` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Network/KickPolicy.cs`:

```csharp
namespace MultiplayerTemplate.Network;

/// <summary>
/// Authorization rule for host-initiated removal. Pure and engine-free so it can be
/// unit tested; must not reference the Godot namespace.
/// </summary>
public static class KickPolicy
{
    public const long ServerPeerId = 1;

    public static bool CanKick(long senderPeerId, long targetPeerId, bool senderIsHost, bool targetIsKnown) =>
        senderIsHost &&
        targetIsKnown &&
        targetPeerId != senderPeerId &&
        targetPeerId != ServerPeerId;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~KickPolicyTests --verbosity minimal`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Network/KickPolicy.cs MultiplayerTemplate.Tests/KickPolicyTests.cs
git commit -m "feat(session): add pure kick authorization policy"
```

---

### Task 5: Replicate the AFK flag on PlayerIdentity

`PlayerIdentity` is a positional record struct, so adding a field changes its constructor arity. Every construction site must be updated in the same commit.

**Files:**
- Modify: `Scripts/Network/PlayerIdentity.cs`
- Modify: `Scripts/Network/NetworkBuildInfo.cs`
- Modify: `Scripts/Network/SessionManager.cs:169` and `Scripts/Network/SessionManager.cs:247`
- Modify: `Scripts/Network/PlayerIdentityService.cs:35`
- Test: `MultiplayerTemplate.Tests/PlayerIdentityTests.cs` (existing file — add cases)

**Interfaces:**
- Consumes: nothing.
- Produces: `PlayerIdentity(long PeerId, string DisplayName, string Platform, long SteamId, bool IsHost, bool Ready, bool Afk)` — `Afk` is the **last** positional parameter, and `ToDictionary()` writes the key `"afk"`.

- [ ] **Step 1: Write the failing test**

Append to `MultiplayerTemplate.Tests/PlayerIdentityTests.cs` inside the existing test class:

```csharp
    [Fact]
    public void AfkSurvivesADictionaryRoundTrip()
    {
        var identity = new PlayerIdentity(7, "Ann", "LAN", 0, false, true, true);
        var restored = PlayerIdentity.FromDictionary(7, identity.ToDictionary());
        Assert.True(restored.Afk);
        Assert.True(restored.Ready);
        Assert.Equal("Ann", restored.DisplayName);
    }

    [Fact]
    public void PayloadWithoutAnAfkKeyDefaultsToPresent()
    {
        var legacy = new Godot.Collections.Dictionary
        {
            ["name"] = "Bo",
            ["platform"] = "LAN",
            ["steam_id"] = 0L,
            ["is_host"] = false,
            ["ready"] = false
        };
        Assert.False(PlayerIdentity.FromDictionary(9, legacy).Afk);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~PlayerIdentityTests --verbosity minimal`
Expected: FAIL to compile — `PlayerIdentity` does not take 7 arguments and has no `Afk` member.

- [ ] **Step 3: Write the implementation**

Replace the record declaration and both dictionary methods in `Scripts/Network/PlayerIdentity.cs`:

```csharp
public readonly record struct PlayerIdentity(
    long PeerId,
    string DisplayName,
    string Platform,
    long SteamId,
    bool IsHost,
    bool Ready,
    bool Afk = false)
{
```

```csharp
    public static PlayerIdentity FromDictionary(long peerId, Dictionary data) => new(
        peerId,
        data.GetValueOrDefault("name", $"Player {peerId}").AsString(),
        data.GetValueOrDefault("platform", "LAN").AsString(),
        data.GetValueOrDefault("steam_id", 0L).AsInt64(),
        data.GetValueOrDefault("is_host", peerId == 1).AsBool(),
        data.GetValueOrDefault("ready", false).AsBool(),
        data.GetValueOrDefault("afk", false).AsBool());

    public Dictionary ToDictionary() => new()
    {
        ["name"] = DisplayName,
        ["platform"] = Platform,
        ["steam_id"] = SteamId,
        ["is_host"] = IsHost,
        ["ready"] = Ready,
        ["afk"] = Afk
    };
```

In `Scripts/Network/SessionManager.cs:169`, extend the host record:

```csharp
        Players[1] = new PlayerIdentity(1, PlayerName, platform, steamId, true, true, false).ToDictionary();
```

In `Scripts/Network/SessionManager.cs:247`, extend the registration record:

```csharp
        Players[sender] = new PlayerIdentity(
            sender, safeName, identityPlatform, steamId, false, false, false).ToDictionary();
```

In `Scripts/Network/PlayerIdentityService.cs:35`, extend the fallback:

```csharp
            : new PlayerIdentity(peerId, $"Player {peerId}", "LAN", 0, peerId == 1, false, false);
```

Bump the protocol in `Scripts/Network/NetworkBuildInfo.cs` — increase `ProtocolVersion` by exactly one. This single bump covers the roster's new `afk` key (this task), the snapshot's new `grounded` field (Task 12), and the new kick RPC (Task 7).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --verbosity minimal`
Expected: PASS — the whole xUnit suite, including the pre-existing identity tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Network/PlayerIdentity.cs Scripts/Network/NetworkBuildInfo.cs \
        Scripts/Network/SessionManager.cs Scripts/Network/PlayerIdentityService.cs \
        MultiplayerTemplate.Tests/PlayerIdentityTests.cs
git commit -m "feat(presence): replicate an AFK flag on the player roster"
```

---

### Task 6: Persist per-peer voice volume

**Files:**
- Modify: `Scripts/Network/PlayerIdentityService.cs`

**Interfaces:**
- Consumes: `VoiceVolumePolicy.Clamp`, `VoiceVolumePolicy.Default` (Task 3); `PlayerIdentity.StableSocialKey`.
- Produces: `float PlayerIdentityService.GetVoiceVolume(PlayerIdentity identity)`, `void PlayerIdentityService.SetVoiceVolume(PlayerIdentity identity, float linear)`. Both raise the existing `SocialStateChanged` event with the social key.

There is no xUnit coverage here: `ConfigFile` needs a Godot runtime. The clamp rule it delegates to is already covered by Task 3, and persistence is exercised by hand in Step 3.

- [ ] **Step 1: Add the accessors**

In `Scripts/Network/PlayerIdentityService.cs`, add `using MultiplayerTemplate.Communication;` to the usings, then add below `SetBlocked`:

```csharp
    public float GetVoiceVolume(PlayerIdentity identity) => VoiceVolumePolicy.Clamp(
        (float)_socialConfig.GetValue(
            "voice_volume", identity.StableSocialKey, VoiceVolumePolicy.Default).AsDouble());

    public void SetVoiceVolume(PlayerIdentity identity, float linear)
    {
        _socialConfig.SetValue("voice_volume", identity.StableSocialKey, VoiceVolumePolicy.Clamp(linear));
        _socialConfig.Save(SocialConfigPath);
        SocialStateChanged?.Invoke(identity.StableSocialKey);
    }
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Verify persistence by hand**

Run: `godot --headless --path . --quit-after 2`
Then run: `godot --path . --quit-after 5` and, from a hosted session with a second client, move a volume slider once Task 18 lands. Until then, confirm only that `user://social_profile.cfg` still loads without error in the log — a malformed float section would surface as a `ConfigFile` parse warning at startup.

Expected: no `ConfigFile` errors in output.

- [ ] **Step 4: Commit**

```bash
git add Scripts/Network/PlayerIdentityService.cs
git commit -m "feat(voice): persist per-peer listening volume with mute and block state"
```

---

### Task 7: Server-side AFK and kick on SessionManager

**Files:**
- Modify: `Scripts/Network/SessionManager.cs`

**Interfaces:**
- Consumes: `KickPolicy.CanKick` (Task 4); `PlayerIdentity` with `Afk` (Task 5); existing `RpcSecurity`, `_requestLimiter`, `SyncRoster`, `RosterChanged`, `DisconnectRejectedPeer`.
- Produces: `void SessionManager.SetAfkServer(long peerId, bool afk)` (server-only, no-op elsewhere), `void SessionManager.RequestKick(long targetPeerId, string reason)` (called by any client; the host path calls the server handler directly), and `double SessionManager.AfkKickSeconds { get; private set; }` read from `server.cfg`.

- [ ] **Step 1: Add the AFK setter**

In `Scripts/Network/SessionManager.cs`, directly below the existing `SetReadyServer` method:

```csharp
    public void SetAfkServer(long peerId, bool afk)
    {
        if (!Multiplayer.IsServer()) return;
        if (!Players.TryGetValue(peerId, out var data)) return;
        if (data.GetValueOrDefault("afk", false).AsBool() == afk) return;
        data["afk"] = afk;
        Rpc(nameof(SyncRoster), Players);
        RosterChanged?.Invoke(Players);
    }
```

- [ ] **Step 2: Add the kick request and handler**

Add below `SetAfkServer`:

```csharp
    public void RequestKick(long targetPeerId, string reason)
    {
        if (Multiplayer.IsServer()) KickServer(Multiplayer.GetUniqueId(), targetPeerId, reason);
        else RpcId(1, nameof(RequestKickRpc), targetPeerId, reason);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestKickRpc(long targetPeerId, string reason)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestKickRpc))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!_requestLimiter.Allow(sender, nameof(RequestKickRpc), 4, 5000))
        {
            RpcSecurity.Reject(nameof(RequestKickRpc), sender, "rate limit exceeded");
            return;
        }
        KickServer(sender, targetPeerId, reason);
    }

    private void KickServer(long senderPeerId, long targetPeerId, string reason)
    {
        var senderIsHost = Players.TryGetValue(senderPeerId, out var senderData) &&
                           senderData.GetValueOrDefault("is_host", senderPeerId == 1).AsBool();
        if (!KickPolicy.CanKick(senderPeerId, targetPeerId, senderIsHost, Players.ContainsKey(targetPeerId)))
        {
            RpcSecurity.Reject(nameof(RequestKickRpc), senderPeerId, $"may not kick {targetPeerId}");
            return;
        }
        var safeReason = reason.Trim();
        if (safeReason.Length > 96) safeReason = safeReason[..96];
        if (safeReason.Length == 0) safeReason = "Removed by the host.";
        DebugLogger.Instance.Info(LogModule, $"Kicking peer {targetPeerId}: {safeReason}");
        RpcId(targetPeerId, nameof(ConnectionRejected), safeReason);
        Players.Remove(targetPeerId);
        Rpc(nameof(SyncRoster), Players);
        RosterChanged?.Invoke(Players);
        _ = DisconnectRejectedPeer(targetPeerId);
    }
```

`ConnectionRejected` and `DisconnectRejectedPeer` already exist and are reused deliberately: the kicked client sees the same "returned to menu with a reason" path as an incompatible build.

- [ ] **Step 3: Read the AFK kick threshold from server.cfg**

Add the property beside the other configurable server fields near `Scripts/Network/SessionManager.cs:31`:

```csharp
    public double AfkKickSeconds { get; private set; }
```

In `LoadServerConfiguration`, inside the `if (config.Load(path) == Error.Ok)` block, after the `MaxPlayers` line:

```csharp
            AfkKickSeconds = Math.Max(0, config.GetValue("server", "afk_kick_seconds", 0).AsDouble());
```

Extend the dedicated-server log line in the same method:

```csharp
        if (IsDedicatedServer)
            DebugLogger.Instance.Info(LogModule,
                $"Dedicated server configuration: name='{ServerName}', max={MaxPlayers}, " +
                $"afk_kick_seconds={AfkKickSeconds}");
```

Add the documented default to `server.cfg.example`:

```ini
# Seconds of no input before the server kicks an idle player. 0 disables it.
afk_kick_seconds=0
```

- [ ] **Step 4: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Network/SessionManager.cs server.cfg.example
git commit -m "feat(session): add server AFK flag updates and host kick"
```

---

### Task 8: Avatar rig contract

**Files:**
- Create: `Scripts/Avatar/AvatarRig.cs`

**Interfaces:**
- Consumes: `LocomotionResult` (Task 1).
- Produces: `enum MultiplayerTemplate.Avatar.AvatarSlot { Head, Back, Hips, HandLeft, HandRight }`; `readonly record struct MultiplayerTemplate.Avatar.AvatarAppearance(Color BodyColor)`; `abstract partial class MultiplayerTemplate.Avatar.AvatarRig : Node3D` with `abstract void SetLocomotion(LocomotionResult state)`, `abstract void PlayAction(string clip)`, `abstract void StopAction()`, `abstract bool ActionPlaying { get; }`, `abstract Node3D GetAttachment(AvatarSlot slot)`, `abstract void ApplyAppearance(AvatarAppearance appearance)`, and `static AvatarAppearance DefaultAppearanceFor(long peerId)`.

- [ ] **Step 1: Write the contract**

Create `Scripts/Avatar/AvatarRig.cs`:

```csharp
using Godot;

namespace MultiplayerTemplate.Avatar;

/// <summary>Attachment points a rig must expose for props and cosmetics.</summary>
public enum AvatarSlot
{
    Head,
    Back,
    Hips,
    HandLeft,
    HandRight
}

/// <summary>
/// Replicated-cosmetic description of a character. P1 carries only a body colour;
/// P2 extends this record with cosmetic slots.
/// </summary>
public readonly record struct AvatarAppearance(Color BodyColor);

/// <summary>
/// Replacement seam for the character body. Supply your own rig by setting the
/// project setting <c>template/avatar/scene_path</c> to a scene whose root derives
/// from this class. See docs/EXTENDING.md.
/// </summary>
public abstract partial class AvatarRig : Node3D
{
    /// <summary>Cosmetic, called every physics frame on every peer.</summary>
    public abstract void SetLocomotion(LocomotionResult state);

    /// <summary>Plays a one-shot clip over locomotion. Unknown clips are ignored.</summary>
    public abstract void PlayAction(string clip);

    public abstract void StopAction();

    public abstract bool ActionPlaying { get; }

    public abstract Node3D GetAttachment(AvatarSlot slot);

    public abstract void ApplyAppearance(AvatarAppearance appearance);

    /// <summary>
    /// The per-peer hue the template used for its capsule players, kept so a fresh
    /// session still gives every player a distinguishable colour before P2 adds
    /// real customization.
    /// </summary>
    public static AvatarAppearance DefaultAppearanceFor(long peerId) =>
        new(Color.FromHsv(Mathf.PosMod(peerId * 0.19f, 1f), 0.7f, 0.9f));
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Scripts/Avatar/AvatarRig.cs
git commit -m "feat(avatar): add swappable avatar rig contract"
```

---

### Task 9: Procedural animation library

**Files:**
- Create: `Scripts/Avatar/ProceduralAnimationLibrary.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MultiplayerTemplate.Avatar.ProceduralAnimationLibrary` with `const string SkeletonNodeName = "Skeleton3D"`, the clip-name constants `Idle`, `Walk`, `Run`, `Jump`, `Fall`, `Land`, and `static AnimationLibrary Build()`.

Bone names used by the tracks — `hips`, `spine`, `head`, `upperarm.L`, `upperarm.R`, `thigh.L`, `thigh.R`, `shin.L`, `shin.R` — must match the bones created in Task 10 exactly.

- [ ] **Step 1: Write the library builder**

Create `Scripts/Avatar/ProceduralAnimationLibrary.cs`:

```csharp
using Godot;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// Builds every default animation clip in code, so the template ships no binary art.
/// Clips are authored as bone rotation tracks against a skeleton named
/// <see cref="SkeletonNodeName"/>.
/// </summary>
public static class ProceduralAnimationLibrary
{
    public const string SkeletonNodeName = "Skeleton3D";
    public const string Idle = "idle";
    public const string Walk = "walk";
    public const string Run = "run";
    public const string Jump = "jump";
    public const string Fall = "fall";
    public const string Land = "land";

    public static AnimationLibrary Build()
    {
        var library = new AnimationLibrary();
        library.AddAnimation(Idle, BuildIdle());
        library.AddAnimation(Walk, BuildStride(1.0f, 0.35f, 0.28f, 0.10f));
        library.AddAnimation(Run, BuildStride(0.6f, 0.75f, 0.55f, 0.22f));
        library.AddAnimation(Jump, BuildJump());
        library.AddAnimation(Fall, BuildFall());
        library.AddAnimation(Land, BuildLand());
        return library;
    }

    private static Animation BuildIdle()
    {
        var animation = NewAnimation(2.4f, loop: true);
        AddRotationTrack(animation, "spine",
            (0f, Pitch(0f)), (1.2f, Pitch(0.035f)), (2.4f, Pitch(0f)));
        AddRotationTrack(animation, "upperarm.L",
            (0f, Roll(0.14f)), (1.2f, Roll(0.19f)), (2.4f, Roll(0.14f)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Roll(-0.14f)), (1.2f, Roll(-0.19f)), (2.4f, Roll(-0.14f)));
        return animation;
    }

    private static Animation BuildStride(float length, float legSwing, float armSwing, float lean)
    {
        var animation = NewAnimation(length, loop: true);
        var half = length * 0.5f;
        AddRotationTrack(animation, "spine", (0f, Pitch(-lean)), (length, Pitch(-lean)));
        AddRotationTrack(animation, "thigh.L",
            (0f, Pitch(legSwing)), (half, Pitch(-legSwing)), (length, Pitch(legSwing)));
        AddRotationTrack(animation, "thigh.R",
            (0f, Pitch(-legSwing)), (half, Pitch(legSwing)), (length, Pitch(-legSwing)));
        AddRotationTrack(animation, "shin.L",
            (0f, Pitch(-legSwing * 0.5f)), (half, Pitch(-legSwing * 0.1f)), (length, Pitch(-legSwing * 0.5f)));
        AddRotationTrack(animation, "shin.R",
            (0f, Pitch(-legSwing * 0.1f)), (half, Pitch(-legSwing * 0.5f)), (length, Pitch(-legSwing * 0.1f)));
        AddRotationTrack(animation, "upperarm.L",
            (0f, Pitch(-armSwing)), (half, Pitch(armSwing)), (length, Pitch(-armSwing)));
        AddRotationTrack(animation, "upperarm.R",
            (0f, Pitch(armSwing)), (half, Pitch(-armSwing)), (length, Pitch(armSwing)));
        return animation;
    }

    private static Animation BuildJump()
    {
        var animation = NewAnimation(0.4f, loop: false);
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0f)), (0.4f, Pitch(0.7f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0f)), (0.4f, Pitch(0.45f)));
        AddRotationTrack(animation, "shin.L", (0f, Pitch(0f)), (0.4f, Pitch(-0.9f)));
        AddRotationTrack(animation, "shin.R", (0f, Pitch(0f)), (0.4f, Pitch(-0.6f)));
        AddRotationTrack(animation, "upperarm.L", (0f, Roll(0.2f)), (0.4f, Roll(1.4f)));
        AddRotationTrack(animation, "upperarm.R", (0f, Roll(-0.2f)), (0.4f, Roll(-1.4f)));
        return animation;
    }

    private static Animation BuildFall()
    {
        var animation = NewAnimation(0.8f, loop: true);
        AddRotationTrack(animation, "spine", (0f, Pitch(0.12f)), (0.4f, Pitch(0.2f)), (0.8f, Pitch(0.12f)));
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0.3f)), (0.8f, Pitch(0.3f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0.15f)), (0.8f, Pitch(0.15f)));
        AddRotationTrack(animation, "upperarm.L", (0f, Roll(1.5f)), (0.4f, Roll(1.7f)), (0.8f, Roll(1.5f)));
        AddRotationTrack(animation, "upperarm.R", (0f, Roll(-1.5f)), (0.4f, Roll(-1.7f)), (0.8f, Roll(-1.5f)));
        return animation;
    }

    private static Animation BuildLand()
    {
        var animation = NewAnimation(0.35f, loop: false);
        AddRotationTrack(animation, "spine", (0f, Pitch(0.35f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "thigh.L", (0f, Pitch(0.55f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "thigh.R", (0f, Pitch(0.55f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "shin.L", (0f, Pitch(-0.9f)), (0.35f, Pitch(0f)));
        AddRotationTrack(animation, "shin.R", (0f, Pitch(-0.9f)), (0.35f, Pitch(0f)));
        return animation;
    }

    private static Animation NewAnimation(float length, bool loop)
    {
        var animation = new Animation { Length = length };
        animation.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        return animation;
    }

    private static void AddRotationTrack(
        Animation animation, string boneName, params (float Time, Quaternion Rotation)[] keys)
    {
        var track = animation.AddTrack(Animation.TrackType.Rotation3D);
        animation.TrackSetPath(track, $"{SkeletonNodeName}:{boneName}");
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Linear);
        foreach (var (time, rotation) in keys)
            animation.RotationTrackInsertKey(track, time, rotation);
    }

    private static Quaternion Pitch(float radians) =>
        new Quaternion(Vector3.Right, radians);

    private static Quaternion Roll(float radians) =>
        new Quaternion(Vector3.Forward, radians);
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Scripts/Avatar/ProceduralAnimationLibrary.cs
git commit -m "feat(avatar): generate default animation clips in code"
```

---

### Task 10: Blockman rig

The body is rigid boxes hung off bones with `BoneAttachment3D`, so no skin weights need to be authored in code. Total height is 1.8 to match the existing `CapsuleShape3D` in `GameWorld.MakePlayer`.

The `AnimationTree` root is a blend tree: a state machine drives locomotion, and an `AnimationNodeOneShot` layers a one-shot action clip over it. P1 ships no action clips; the node exists so P2 emotes plug in without restructuring.

**Files:**
- Create: `Scripts/Avatar/BlockmanRig.cs`

**Interfaces:**
- Consumes: `AvatarRig`, `AvatarSlot`, `AvatarAppearance` (Task 8); `LocomotionResult`, `LocomotionState` (Task 1); `ProceduralAnimationLibrary` (Task 9).
- Produces: `partial class MultiplayerTemplate.Avatar.BlockmanRig : AvatarRig`.

- [ ] **Step 1: Write the rig**

Create `Scripts/Avatar/BlockmanRig.cs`:

```csharp
using Godot;
using DebugLoggerKit;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// The default character: a skeleton with one rigid box per bone. Deliberately
/// asset-free. Replace it by setting <c>template/avatar/scene_path</c>.
/// </summary>
public partial class BlockmanRig : AvatarRig
{
    private const string LogModule = "Avatar";
    private const string LibraryName = "template";
    private const string ActionNode = "parameters/action/animation";
    private const string ActionRequest = "parameters/action_shot/request";
    private const string ActionActive = "parameters/action_shot/active";
    private const string GroundBlend = "parameters/locomotion/ground/blend_position";
    private const string StatePlayback = "parameters/locomotion/playback";

    private Skeleton3D _skeleton = null!;
    private AnimationPlayer _player = null!;
    private AnimationTree _tree = null!;
    private AnimationLibrary _library = null!;
    private readonly System.Collections.Generic.Dictionary<AvatarSlot, Node3D> _attachments = new();
    private readonly System.Collections.Generic.List<StandardMaterial3D> _materials = new();
    private LocomotionState _currentState = LocomotionState.Idle;

    public override bool ActionPlaying => (bool)_tree.Get(ActionActive);

    public override void _Ready()
    {
        BuildSkeleton();
        BuildBoxes();
        BuildAttachments();
        BuildAnimation();
    }

    public override void SetLocomotion(LocomotionResult state)
    {
        _tree.Set(GroundBlend, state.Blend);
        if (state.State == _currentState && !state.LandTriggered) return;
        _currentState = state.State;
        if (_tree.Get(StatePlayback).As<AnimationNodeStateMachinePlayback>() is not { } playback) return;
        playback.Travel(StateNodeName(state.State));
    }

    public override void PlayAction(string clip)
    {
        if (!_library.HasAnimation(clip))
        {
            DebugLogger.Instance.Warning(LogModule, $"Avatar has no action clip '{clip}'; ignoring");
            return;
        }
        _tree.Set(ActionNode, $"{LibraryName}/{clip}");
        _tree.Set(ActionRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    public override void StopAction() =>
        _tree.Set(ActionRequest, (int)AnimationNodeOneShot.OneShotRequest.Abort);

    public override Node3D GetAttachment(AvatarSlot slot) => _attachments[slot];

    public override void ApplyAppearance(AvatarAppearance appearance)
    {
        foreach (var material in _materials) material.AlbedoColor = appearance.BodyColor;
    }

    private static string StateNodeName(LocomotionState state) => state switch
    {
        LocomotionState.Jump => ProceduralAnimationLibrary.Jump,
        LocomotionState.Fall => ProceduralAnimationLibrary.Fall,
        LocomotionState.Land => ProceduralAnimationLibrary.Land,
        _ => "ground"
    };

    private void BuildSkeleton()
    {
        _skeleton = new Skeleton3D { Name = ProceduralAnimationLibrary.SkeletonNodeName };
        AddChild(_skeleton);
        var hips = AddBone("hips", -1, new Vector3(0, 0.95f, 0));
        var spine = AddBone("spine", hips, new Vector3(0, 0.35f, 0));
        AddBone("head", spine, new Vector3(0, 0.35f, 0));
        AddBone("upperarm.L", spine, new Vector3(0.3f, 0.22f, 0));
        AddBone("upperarm.R", spine, new Vector3(-0.3f, 0.22f, 0));
        var thighL = AddBone("thigh.L", hips, new Vector3(0.14f, -0.05f, 0));
        var thighR = AddBone("thigh.R", hips, new Vector3(-0.14f, -0.05f, 0));
        AddBone("shin.L", thighL, new Vector3(0, -0.45f, 0));
        AddBone("shin.R", thighR, new Vector3(0, -0.45f, 0));
        _skeleton.ResetBonePoses();
    }

    private int AddBone(string name, int parent, Vector3 offset)
    {
        var index = _skeleton.AddBone(name);
        if (parent >= 0) _skeleton.SetBoneParent(index, parent);
        _skeleton.SetBoneRest(index, new Transform3D(Basis.Identity, offset));
        return index;
    }

    private void BuildBoxes()
    {
        AttachBox("hips", new Vector3(0.5f, 0.28f, 0.28f), Vector3.Zero);
        AttachBox("spine", new Vector3(0.55f, 0.42f, 0.3f), new Vector3(0, 0.16f, 0));
        AttachBox("head", new Vector3(0.34f, 0.34f, 0.34f), new Vector3(0, 0.17f, 0));
        AttachBox("upperarm.L", new Vector3(0.16f, 0.5f, 0.16f), new Vector3(0, -0.25f, 0));
        AttachBox("upperarm.R", new Vector3(0.16f, 0.5f, 0.16f), new Vector3(0, -0.25f, 0));
        AttachBox("thigh.L", new Vector3(0.2f, 0.45f, 0.2f), new Vector3(0, -0.23f, 0));
        AttachBox("thigh.R", new Vector3(0.2f, 0.45f, 0.2f), new Vector3(0, -0.23f, 0));
        AttachBox("shin.L", new Vector3(0.18f, 0.45f, 0.18f), new Vector3(0, -0.23f, 0));
        AttachBox("shin.R", new Vector3(0.18f, 0.45f, 0.18f), new Vector3(0, -0.23f, 0));
    }

    private void AttachBox(string boneName, Vector3 size, Vector3 offset)
    {
        var attachment = NewBoneAttachment(boneName, $"Box_{boneName}");
        var material = new StandardMaterial3D { AlbedoColor = Colors.White };
        _materials.Add(material);
        attachment.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = offset,
            MaterialOverride = material
        });
    }

    private void BuildAttachments()
    {
        _attachments[AvatarSlot.Head] = NewMarker("head", new Vector3(0, 0.36f, 0), "Slot_Head");
        _attachments[AvatarSlot.Back] = NewMarker("spine", new Vector3(0, 0.16f, -0.18f), "Slot_Back");
        _attachments[AvatarSlot.Hips] = NewMarker("hips", new Vector3(0, 0f, -0.18f), "Slot_Hips");
        _attachments[AvatarSlot.HandLeft] = NewMarker("upperarm.L", new Vector3(0, -0.52f, 0), "Slot_HandLeft");
        _attachments[AvatarSlot.HandRight] = NewMarker("upperarm.R", new Vector3(0, -0.52f, 0), "Slot_HandRight");
    }

    private Node3D NewMarker(string boneName, Vector3 offset, string name)
    {
        var attachment = NewBoneAttachment(boneName, $"{name}_Attachment");
        var marker = new Node3D { Name = name, Position = offset };
        attachment.AddChild(marker);
        return marker;
    }

    private BoneAttachment3D NewBoneAttachment(string boneName, string nodeName)
    {
        var attachment = new BoneAttachment3D { Name = nodeName, BoneName = boneName };
        _skeleton.AddChild(attachment);
        attachment.BoneIdx = _skeleton.FindBone(boneName);
        return attachment;
    }

    private void BuildAnimation()
    {
        _library = ProceduralAnimationLibrary.Build();
        _player = new AnimationPlayer { Name = "AnimationPlayer" };
        AddChild(_player);
        _player.AddAnimationLibrary(LibraryName, _library);

        var locomotion = new AnimationNodeStateMachine();
        var ground = new AnimationNodeBlendSpace1D { MinSpace = 0f, MaxSpace = 1f };
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Idle), 0f);
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Walk), 0.5f);
        ground.AddBlendPoint(Clip(ProceduralAnimationLibrary.Run), 1f);
        locomotion.AddNode("ground", ground, new Vector2(0, 0));
        locomotion.AddNode(ProceduralAnimationLibrary.Jump, Clip(ProceduralAnimationLibrary.Jump), new Vector2(220, -80));
        locomotion.AddNode(ProceduralAnimationLibrary.Fall, Clip(ProceduralAnimationLibrary.Fall), new Vector2(220, 0));
        locomotion.AddNode(ProceduralAnimationLibrary.Land, Clip(ProceduralAnimationLibrary.Land), new Vector2(220, 80));
        string[] states =
        [
            "ground", ProceduralAnimationLibrary.Jump,
            ProceduralAnimationLibrary.Fall, ProceduralAnimationLibrary.Land
        ];
        // Travel() needs a transition to exist between any pair the solver can move between.
        foreach (var from in states)
        foreach (var to in states)
        {
            if (from == to) continue;
            locomotion.AddTransition(from, to, new AnimationNodeStateMachineTransition
            {
                SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
                AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Disabled,
                XfadeTime = 0.12f
            });
        }

        var oneShot = new AnimationNodeOneShot { FadeInTime = 0.1f, FadeOutTime = 0.15f };
        var root = new AnimationNodeBlendTree();
        root.AddNode("locomotion", locomotion, new Vector2(0, 0));
        root.AddNode("action", new AnimationNodeAnimation(), new Vector2(0, 140));
        root.AddNode("action_shot", oneShot, new Vector2(260, 60));
        root.ConnectNode("action_shot", 0, "locomotion");
        root.ConnectNode("action_shot", 1, "action");
        root.ConnectNode("output", 0, "action_shot");

        _tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = root,
            AnimPlayer = _player.GetPath(),
            Active = true
        };
        AddChild(_tree);
    }

    private static AnimationNodeAnimation Clip(string name) =>
        new() { Animation = $"{LibraryName}/{name}" };
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Scripts/Avatar/BlockmanRig.cs
git commit -m "feat(avatar): add the code-built default blockman rig"
```

---

### Task 11: Avatar factory and the replacement setting

**Files:**
- Create: `Scripts/Avatar/AvatarFactory.cs`
- Modify: `project.godot`

**Interfaces:**
- Consumes: `AvatarRig`, `BlockmanRig` (Tasks 8, 10).
- Produces: `static class MultiplayerTemplate.Avatar.AvatarFactory` with `const string ScenePathSetting = "template/avatar/scene_path"` and `static AvatarRig? Build(long peerId)`. Returns `null` on headless.

- [ ] **Step 1: Write the factory**

Create `Scripts/Avatar/AvatarFactory.cs`:

```csharp
using Godot;
using DebugLoggerKit;

namespace MultiplayerTemplate.Avatar;

/// <summary>
/// Resolves which rig to build. A misconfigured replacement rig is a logged warning
/// and a fallback to the built-in body, never a crash.
/// </summary>
public static class AvatarFactory
{
    public const string ScenePathSetting = "template/avatar/scene_path";
    private const string LogModule = "Avatar";

    public static AvatarRig? Build(long peerId)
    {
        // Servers need collision and movement, not boxes. BoneAttachment3D never
        // participates in physics, so simulation is identical without a rig.
        if (DisplayServer.GetName() == "headless") return null;
        var rig = BuildConfigured() ?? new BlockmanRig();
        rig.Name = "Avatar";
        rig.Ready += () => rig.ApplyAppearance(AvatarRig.DefaultAppearanceFor(peerId));
        return rig;
    }

    private static AvatarRig? BuildConfigured()
    {
        var configured = ProjectSettings.GetSetting(ScenePathSetting, "").AsString();
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (!ResourceLoader.Exists(configured))
        {
            DebugLogger.Instance.Warning(
                LogModule, $"{ScenePathSetting}='{configured}' does not exist; using the built-in rig");
            return null;
        }
        if (ResourceLoader.Load<PackedScene>(configured) is not { } scene)
        {
            DebugLogger.Instance.Warning(
                LogModule, $"{ScenePathSetting}='{configured}' is not a PackedScene; using the built-in rig");
            return null;
        }
        if (scene.Instantiate() is AvatarRig rig) return rig;
        DebugLogger.Instance.Warning(
            LogModule, $"{ScenePathSetting}='{configured}' root does not derive from AvatarRig; using the built-in rig");
        return null;
    }
}
```

- [ ] **Step 2: Declare the setting**

Add to `project.godot`, as a new section placed after the `[input]` section so the file stays readable:

```ini
[template]

avatar/scene_path=""
```

- [ ] **Step 3: Verify the project builds and the setting resolves**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Run: `godot --headless --path . --quit-after 2`
Expected: no errors mentioning `template/avatar/scene_path`.

- [ ] **Step 4: Commit**

```bash
git add Scripts/Avatar/AvatarFactory.cs project.godot
git commit -m "feat(avatar): resolve the rig through a replaceable project setting"
```

---

### Task 12: Wire the avatar into the player and replicate grounded state

**Files:**
- Modify: `Scripts/Game/NetworkPlayer.cs`
- Modify: `Scripts/Game/GameWorld.cs:99-119`

**Interfaces:**
- Consumes: `AvatarFactory.Build` (Task 11); `LocomotionSolver.Solve` (Task 1).
- Produces: `void NetworkPlayer.AttachAvatar(AvatarRig? rig)`, `AvatarRig? NetworkPlayer.Avatar { get; }`, and `ReceiveSnapshot(Vector3 position, Vector3 velocity, float yaw, bool grounded, uint acknowledgedInput)`.

Note the deliberate ordering in `UpdateAvatarAnimation`: airborne time is computed **before** it is reset, otherwise the landing frame always reports zero airborne time and `Land` never fires.

- [ ] **Step 1: Add the avatar field and animation update to NetworkPlayer**

Add `using MultiplayerTemplate.Avatar;` to the usings in `Scripts/Game/NetworkPlayer.cs`, then add the fields beside the other private state:

```csharp
    private AvatarRig? _avatar;
    private float _airborneSeconds;
    private bool _wasGrounded = true;
    private bool _targetGrounded = true;
```

Add the accessors below `EyePosition`:

```csharp
    public AvatarRig? Avatar => _avatar;

    public void AttachAvatar(AvatarRig? rig)
    {
        if (rig == null) return;
        _avatar = rig;
        AddChild(rig);
    }
```

Add the update method below `ApplyMovement`:

```csharp
    private void UpdateAvatarAnimation(float delta)
    {
        if (_avatar == null) return;
        // Remote peers never run MoveAndSlide, so their IsOnFloor() is meaningless;
        // the server tells them whether the character is grounded.
        var grounded = Multiplayer.IsServer() || IsLocalPlayer ? IsOnFloor() : _targetGrounded;
        var airborne = grounded ? _airborneSeconds : _airborneSeconds + delta;
        var planarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        _avatar.SetLocomotion(
            LocomotionSolver.Solve(planarSpeed, grounded, Velocity.Y, airborne, _wasGrounded));
        _airborneSeconds = grounded ? 0f : airborne;
        _wasGrounded = grounded;
    }
```

Call it as the last statement of `_PhysicsProcess`:

```csharp
        UpdateAvatarAnimation(step);
```

- [ ] **Step 2: Add grounded to the snapshot**

In `SendSnapshots`, extend the broadcast:

```csharp
        Rpc(nameof(ReceiveSnapshot), GlobalPosition, Velocity, Rotation.Y, IsOnFloor(), _lastReceivedSequence);
```

Change the `ReceiveSnapshot` signature and store the flag on the remote branch:

```csharp
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveSnapshot(
        Vector3 position, Vector3 velocity, float yaw, bool grounded, uint acknowledgedInput)
    {
```

and, in the remote branch at the end of that method, beside the other `_target*` assignments:

```csharp
        _targetGrounded = grounded;
```

- [ ] **Step 3: Build the avatar instead of the capsule mesh in GameWorld**

Add `using MultiplayerTemplate.Avatar;` to the usings in `Scripts/Game/GameWorld.cs`. In `MakePlayer`, delete the `material` and `mesh` locals and the `player.AddChild(mesh)` call, and add after the collision shape is added:

```csharp
        player.AttachAvatar(AvatarFactory.Build(peerId));
```

Leave the collision shape, `Head`, camera, and nameplate construction exactly as they are; Task 13 replaces the camera construction and Task 14 the nameplate.

- [ ] **Step 4: Verify the project builds and two peers animate**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: the existing smoke suite still passes. A protocol mismatch here means Task 5's bump was skipped or double-applied.

Manual check: run `godot --path . --quit-after 60` twice, host on one and join `127.0.0.1` on the other, start the match, and confirm the other player is a walking blockman rather than a capsule and that jumping shows the airborne pose.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Game/NetworkPlayer.cs Scripts/Game/GameWorld.cs
git commit -m "feat(avatar): replace the capsule mesh with an animated rig"
```

---

### Task 13: PlayerCamera with a third-person mode

`NetworkPlayer` is already 258 lines. Camera ownership moves out rather than growing it further. `PlayerCamera` *is* the head pivot node, so `HoldPosition` and `EyePosition` keep their existing meaning.

One code path serves both modes: the `Camera3D` always hangs off a `SpringArm3D`, and first person is simply spring length zero.

**Files:**
- Create: `Scripts/Game/PlayerCamera.cs`
- Modify: `Scripts/Game/NetworkPlayer.cs`
- Modify: `Scripts/Game/GameWorld.cs`
- Modify: `addons/SettingsManager/SettingsManager.cs`
- Modify: `project.godot`

**Interfaces:**
- Consumes: `AvatarRig` (Task 8); `SettingsManager`; `LocalInputContext`. Adds `int SettingsManager.GetCameraMode()` and `void SettingsManager.SetCameraMode(int mode)`.
- Produces: `enum MultiplayerTemplate.Game.CameraMode { FirstPerson, ThirdPerson }`; `partial class MultiplayerTemplate.Game.PlayerCamera : Node3D` with `void Initialize(bool isLocal, AvatarRig? rig)`, `void ApplyPitch(float pitch)`, `void ToggleMode()`, `CameraMode Mode { get; }`, `Vector3 AimOrigin { get; }`, `Vector3 AimForward { get; }`, `void PushTemporaryThirdPerson(string owner)`, `void PopTemporary(string owner)`. `NetworkPlayer.Setup` becomes `Setup(long id, PlayerCamera camera, Label3D nameLabel)`.

`PushTemporaryThirdPerson` / `PopTemporary` ship unused in P1; P2 emotes call them so a player sees their own emote and is returned to the mode they chose.

- [ ] **Step 1: Write PlayerCamera**

Create `Scripts/Game/PlayerCamera.cs`:

```csharp
using Godot;
using SettingsKit;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.UI;

namespace MultiplayerTemplate.Game;

public enum CameraMode
{
    FirstPerson,
    ThirdPerson
}

/// <summary>
/// The head pivot and its camera. Deliberately a plain Node3D and not a
/// BoneAttachment3D: parenting the camera to an animated bone makes the
/// first-person view shake.
/// </summary>
public partial class PlayerCamera : Node3D
{
    private const float ThirdPersonDistance = 3.5f;

    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private AvatarRig? _rig;
    private bool _isLocal;
    private readonly System.Collections.Generic.HashSet<string> _temporaryOwners = new();

    public CameraMode Mode { get; private set; } = CameraMode.FirstPerson;

    /// <summary>Interaction and aim always originate at the head, in both modes.</summary>
    public Vector3 AimOrigin => GlobalPosition;

    public Vector3 AimForward => -GlobalBasis.Z;

    public Vector3 EyePosition => GlobalPosition;

    public override void _Ready()
    {
        Name = "Head";
        _arm = new SpringArm3D { Name = "SpringArm3D", SpringLength = 0f, Margin = 0.2f };
        AddChild(_arm);
        _camera = new Camera3D { Name = "Camera3D", Fov = SettingsManager.Instance.GetFov() };
        _arm.AddChild(_camera);
    }

    public void Initialize(bool isLocal, AvatarRig? rig)
    {
        _isLocal = isLocal;
        _rig = rig;
        if (DisplayServer.GetName() == "headless") return;
        _camera.Current = isLocal;
        if (!isLocal) return;
        Mode = (CameraMode)Mathf.Clamp(SettingsManager.Instance.GetCameraMode(), 0, 1);
        ApplyMode();
    }

    public void ApplyPitch(float pitch) => Rotation = new Vector3(pitch, 0, 0);

    public void ToggleMode()
    {
        if (!_isLocal) return;
        Mode = Mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
        SettingsManager.Instance.SetCameraMode((int)Mode);
        ApplyMode();
    }

    public void PushTemporaryThirdPerson(string owner)
    {
        if (!_isLocal || !_temporaryOwners.Add(owner)) return;
        ApplyMode();
    }

    public void PopTemporary(string owner)
    {
        if (!_isLocal || !_temporaryOwners.Remove(owner)) return;
        ApplyMode();
    }

    private void ApplyMode()
    {
        var thirdPerson = Mode == CameraMode.ThirdPerson || _temporaryOwners.Count > 0;
        _arm.SpringLength = thirdPerson ? ThirdPersonDistance : 0f;
        _camera.Fov = SettingsManager.Instance.GetFov();
        // The local body is hidden in first person; remote bodies are always visible.
        if (_isLocal && _rig != null) _rig.Visible = thirdPerson;
    }
}
```

- [ ] **Step 2: Add the camera-mode accessors to SettingsManager**

`addons/SettingsManager/SettingsManager.cs` has no generic integer accessor; it uses one typed pair per setting. Follow that. The file uses **tabs**.

Add the default beside the other defaults near `addons/SettingsManager/SettingsManager.cs:34`:

```csharp
	private const int DEFAULT_CAMERA_MODE = 0;
```

Add the pair beside the other video accessors, using the existing `SaveSetting` helper:

```csharp
	public int GetCameraMode()
	{
		return (int)_config.GetValue("video", "camera_mode", DEFAULT_CAMERA_MODE);
	}

	public void SetCameraMode(int mode)
	{
		SaveSetting("video", "camera_mode", Mathf.Clamp(mode, 0, 1));
	}
```

Add the same key to `SaveDefaultSettings` beside the existing `video`/`fov` default:

```csharp
		_config.SetValue("video", "camera_mode", DEFAULT_CAMERA_MODE);
```

- [ ] **Step 3: Add the toggle_camera action**

Add to the `[input]` section of `project.godot`, following the exact shape of the existing `push_to_talk` entry. `C` is physical keycode `67`:

```ini
toggle_camera={
"deadzone": 0.2,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":0,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":67,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

- [ ] **Step 4: Point NetworkPlayer at PlayerCamera**

In `Scripts/Game/NetworkPlayer.cs`, replace the `_camera` and `_head` fields with one:

```csharp
    private PlayerCamera _view = null!;
```

Change `Setup` and the derived positions:

```csharp
    public void Setup(long id, PlayerCamera view, Label3D nameLabel)
    {
        PeerId = id;
        Name = id.ToString();
        _view = view;
        _nameLabel = nameLabel;
        // The server owns every player node. PeerId identifies who may submit input.
        SetMultiplayerAuthority(1);
    }

    public Vector3 HoldPosition => _view.GlobalPosition - _view.GlobalBasis.Z * 2.2f;
    public Vector3 EyePosition => _view.EyePosition;
```

In `_Ready`, replace the `_camera.Current` line with:

```csharp
        _view.Initialize(IsLocalPlayer, _avatar);
```

In `_UnhandledInput`, add the toggle before the mouse-motion handling, and replace the head rotation:

```csharp
        if (IsLocalPlayer && @event.IsActionPressed("toggle_camera") &&
            !LocalInputContext.Instance.GameplaySuppressed)
        {
            _view.ToggleMode();
            GetViewport().SetInputAsHandled();
            return;
        }
```

```csharp
        _view.ApplyPitch(_pitch);
```

In `InteractionTarget`, raycast from the head in both modes:

```csharp
        var from = _view.AimOrigin;
        var query = PhysicsRayQueryParameters3D.Create(
            from, from + _view.AimForward * InteractionManager.Reach);
```

- [ ] **Step 5: Build the camera in GameWorld**

In `Scripts/Game/GameWorld.MakePlayer`, replace the `head` and `camera` construction with:

```csharp
        var view = new PlayerCamera { Position = new Vector3(0, 1.55f, 0) };
        player.AddChild(view);
```

and update the `Setup` call:

```csharp
        player.Setup(peerId, view, nameLabel);
```

- [ ] **Step 6: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: PASS.

Manual check: run the game, press `C`, confirm the camera pulls back and your own body appears, confirm `E` still picks up a grabbable object while in third person, press `C` again and confirm your body disappears. Confirm `toggle_camera` appears in the Controls rebinding list.

- [ ] **Step 7: Commit**

```bash
git add Scripts/Game/PlayerCamera.cs Scripts/Game/NetworkPlayer.cs Scripts/Game/GameWorld.cs \
        addons/SettingsManager/SettingsManager.cs project.godot
git commit -m "feat(camera): extract PlayerCamera and add a third-person toggle"
```

---

### Task 14: Player nameplate

**Files:**
- Create: `Scripts/Game/PlayerNameplate.cs`
- Modify: `Scripts/Game/GameWorld.cs`
- Modify: `Scripts/Game/NetworkPlayer.cs`

**Interfaces:**
- Consumes: `SessionManager.DisplayName`, `SessionManager.Players`, `RosterChanged`; `PositionalVoiceChat.IsSpeaking` (Task 15 — write this task first and land Task 15 before running the manual check, or temporarily treat speaking as always false).
- Produces: `partial class MultiplayerTemplate.Game.PlayerNameplate : Label3D` with `void Initialize(long peerId, bool isLocal)`. `NetworkPlayer.Setup` takes `PlayerNameplate` in place of `Label3D`.

- [ ] **Step 1: Write the nameplate**

Create `Scripts/Game/PlayerNameplate.cs`:

```csharp
using Godot;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.Game;

/// <summary>
/// Floating name, speaking icon, and AFK tag. Client-only; a headless server never
/// builds one.
/// </summary>
public partial class PlayerNameplate : Label3D
{
    private const float FullAlphaDistance = 12f;
    private const float FadeEndDistance = 40f;

    private long _peerId;
    private bool _isLocal;

    public void Initialize(long peerId, bool isLocal)
    {
        _peerId = peerId;
        _isLocal = isLocal;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            Visible = false;
            return;
        }
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        NoDepthTest = true;
        FontSize = 32;
        Visible = !isLocal;
    }

    public override void _Process(double delta)
    {
        if (_isLocal) return;
        var identity = PlayerIdentityService.Instance.Get(_peerId);
        var speaking = PositionalVoiceChat.Instance != null &&
                       PositionalVoiceChat.Instance.IsSpeaking(_peerId);
        Text = identity.Afk
            ? $"{identity.DisplayName}  (away)"
            : speaking
                ? $"{identity.DisplayName}  <))"
                : identity.DisplayName;
        var alpha = 1f;
        if (GetViewport().GetCamera3D() is { } camera)
        {
            var distance = camera.GlobalPosition.DistanceTo(GlobalPosition);
            alpha = 1f - Mathf.Clamp(
                (distance - FullAlphaDistance) / (FadeEndDistance - FullAlphaDistance), 0f, 1f);
        }
        Modulate = new Color(1f, 1f, 1f, identity.Afk ? alpha * 0.45f : alpha);
        Visible = alpha > 0.02f;
    }
}
```

- [ ] **Step 2: Build it in GameWorld**

In `Scripts/Game/GameWorld.MakePlayer`, replace the `nameLabel` construction with:

```csharp
        var nameLabel = new PlayerNameplate { Position = new Vector3(0, 2.25f, 0) };
        player.AddChild(nameLabel);
```

- [ ] **Step 3: Update NetworkPlayer**

Change the field and `Setup` parameter type from `Label3D` to `PlayerNameplate`, and replace the `_nameLabel.Text = ...` assignment in `_Ready` with:

```csharp
        _nameLabel.Initialize(PeerId, IsLocalPlayer);
```

- [ ] **Step 4: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Manual check (after Task 15): two peers, confirm the remote nameplate shows the speaking icon while the other player holds `V`, and fades with distance.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Game/PlayerNameplate.cs Scripts/Game/GameWorld.cs Scripts/Game/NetworkPlayer.cs
git commit -m "feat(presence): add speaking and AFK aware nameplates"
```

---

### Task 15: Speaking state and per-peer volume in PositionalVoiceChat

**Files:**
- Modify: `Scripts/Communication/PositionalVoiceChat.cs`

**Interfaces:**
- Consumes: `SpeakingTracker`, `VoiceVolumePolicy` (Task 3); `PlayerIdentityService.GetVoiceVolume` (Task 6).
- Produces: `bool PositionalVoiceChat.IsSpeaking(long peerId)`, `void PositionalVoiceChat.SetPeerVolume(long peerId, float linear)`, `float PositionalVoiceChat.GetPeerVolume(long peerId)`.

- [ ] **Step 1: Track speakers and apply gain**

Add the tracker field beside `_speakers`:

```csharp
    private readonly SpeakingTracker _speaking = new();
```

Add the public surface below `ClientCaptureEnabled`:

```csharp
    public bool IsSpeaking(long peerId) =>
        _speaking.IsSpeaking(peerId, Time.GetTicksMsec() / 1000.0);

    public float GetPeerVolume(long peerId) =>
        PlayerIdentityService.Instance.GetVoiceVolume(PlayerIdentityService.Instance.Get(peerId));

    public void SetPeerVolume(long peerId, float linear)
    {
        var clamped = VoiceVolumePolicy.Clamp(linear);
        PlayerIdentityService.Instance.SetVoiceVolume(PlayerIdentityService.Instance.Get(peerId), clamped);
        if (_speakers.TryGetValue(peerId, out var speaker))
            speaker.VolumeDb = VoiceVolumePolicy.ToDecibels(clamped);
    }
```

In `PlayPacket`, record the packet before playing it:

```csharp
        _speaking.NotePacket(peerId, Time.GetTicksMsec() / 1000.0);
```

Place that line after the mute and length guards, so a muted peer does not light up as speaking.

In `EnsureSpeaker`, apply the stored gain when the speaker node is created, immediately before `AddChild(speaker)`:

```csharp
        speaker.VolumeDb = VoiceVolumePolicy.ToDecibels(GetPeerVolume(peerId));
```

- [ ] **Step 2: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Manual check: two peers in a match; hold `V` on one and confirm the other's nameplate shows the speaking icon and clears roughly a quarter second after release.

- [ ] **Step 3: Commit**

```bash
git add Scripts/Communication/PositionalVoiceChat.cs
git commit -m "feat(voice): expose speaking state and per-peer listening volume"
```

---

### Task 16: Server-side AFK timing

**Files:**
- Modify: `Scripts/Game/NetworkPlayer.cs`

**Interfaces:**
- Consumes: `AfkPolicy` (Task 2); `SessionManager.SetAfkServer`, `SessionManager.RequestKick`, `SessionManager.AfkKickSeconds` (Task 7).
- Produces: nothing new for later tasks.

- [ ] **Step 1: Stamp meaningful input on the server**

Add the fields to `Scripts/Game/NetworkPlayer.cs`:

```csharp
    private const float YawIdleEpsilon = 0.01f;
    private double _lastInputSeconds;
    private bool _afk;
```

Initialize the stamp at the top of `_Ready`:

```csharp
        _lastInputSeconds = Time.GetTicksMsec() / 1000.0;
```

Add the helper below `UpdateAvatarAnimation`:

```csharp
    private void NoteActivity(Vector2 move, bool jump, float yaw)
    {
        if (move.LengthSquared() <= 0f && !jump && Mathf.Abs(Mathf.AngleDifference(yaw, _serverYaw)) < YawIdleEpsilon)
            return;
        _lastInputSeconds = Time.GetTicksMsec() / 1000.0;
    }
```

Call it from `SubmitInput`, immediately before `_serverMoveInput` is assigned:

```csharp
        NoteActivity(move, jump, yaw);
```

and from the server-hosted local player branch of `_PhysicsProcess`, immediately after `var local = ReadLocalInput();`:

```csharp
                NoteActivity(local.Move, local.Jump, _yaw);
```

- [ ] **Step 2: Evaluate and publish**

Add below `NoteActivity`:

```csharp
    private void UpdateAfk()
    {
        if (!Multiplayer.IsServer()) return;
        var idleSeconds = Time.GetTicksMsec() / 1000.0 - _lastInputSeconds;
        var afk = AfkPolicy.Evaluate(idleSeconds, AfkPolicy.DefaultThresholdSeconds, _afk);
        if (afk != _afk)
        {
            _afk = afk;
            SessionManager.Instance.SetAfkServer(PeerId, afk);
        }
        if (PeerId != 1 && AfkPolicy.ShouldKick(idleSeconds, SessionManager.Instance.AfkKickSeconds))
            SessionManager.Instance.RequestKick(PeerId, "Idle for too long.");
    }
```

Call it from `_PhysicsProcess`, on the server branch only, immediately after `SendSnapshots(delta);`:

```csharp
            UpdateAfk();
```

- [ ] **Step 3: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Manual check: temporarily lower `AfkPolicy.DefaultThresholdSeconds` to `5`, run two peers, leave the client untouched, and confirm the client's nameplate reads "(away)" on the host and clears on the next input. Restore the constant to `90` before committing.

- [ ] **Step 4: Commit**

```bash
git add Scripts/Game/NetworkPlayer.cs
git commit -m "feat(presence): derive AFK from authoritative input arrival"
```

---

### Task 17: Extract the shared player row

**Files:**
- Create: `Scripts/UI/PlayerRow.cs`
- Modify: `Scripts/UI/Main.cs:343-400`

**Interfaces:**
- Consumes: `PlayerIdentity` (Task 5); `PlayerIdentityService`; `ConnectionQualityMonitor.Instance.ForPeer(peerId)` returning `ConnectionQuality` with `Health` and `ShortText`; `PositionalVoiceChat.IsSpeaking`, `GetPeerVolume`, `SetPeerVolume` (Task 15); `SessionManager.RequestKick` (Task 7).
- Produces: `readonly record struct MultiplayerTemplate.UI.PlayerRowOptions(bool ShowReady, bool ShowQuality, bool ShowVoiceControls, bool ShowKick, long LocalPeerId)`; `partial class MultiplayerTemplate.UI.PlayerRow : HBoxContainer` with `void Configure(PlayerIdentity identity, PlayerRowOptions options)`, `void RefreshQuality()`, `void RefreshSpeaking()`, `TextureRect Avatar { get; }`, and `event Action? Changed` (raised when mute, block, or kick is used so the owner can re-render).

`Main.cs` uses **tabs**. `PlayerRow.cs` is a new file and uses 4 spaces, matching the rest of `Scripts/`.

- [ ] **Step 1: Write the row widget**

Create `Scripts/UI/PlayerRow.cs`:

```csharp
using Godot;
using MultiplayerTemplate.Communication;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

public readonly record struct PlayerRowOptions(
    bool ShowReady,
    bool ShowQuality,
    bool ShowVoiceControls,
    bool ShowKick,
    long LocalPeerId);

/// <summary>
/// One roster entry, shared by the lobby list and the in-match player list so both
/// render identity, presence, and social controls the same way.
/// </summary>
public partial class PlayerRow : HBoxContainer
{
    public event Action? Changed;
    public TextureRect Avatar { get; private set; } = null!;

    private Label _name = null!;
    private Label _quality = null!;
    private long _peerId;
    private PlayerRowOptions _options;

    public void Configure(PlayerIdentity identity, PlayerRowOptions options)
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _peerId = identity.PeerId;
        // Remembered so RefreshSpeaking rebuilds the same columns instead of
        // silently dropping the ready state on the next frame.
        _options = options;

        Avatar = new TextureRect
        {
            CustomMinimumSize = new Vector2(36, 36),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        AddChild(Avatar);

        _name = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_name);
        WriteName(identity);

        _quality = new Label
        {
            TooltipText = "Round-trip latency and rolling packet-loss estimate",
            CustomMinimumSize = new Vector2(110, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Visible = options.ShowQuality
        };
        AddChild(_quality);
        RefreshQuality();

        if (identity.PeerId == options.LocalPeerId) return;

        var muted = PlayerIdentityService.Instance.IsMuted(identity);
        AddChild(TextButton(muted ? "Unmute" : "Mute", () =>
        {
            PlayerIdentityService.Instance.SetMuted(
                identity, !PlayerIdentityService.Instance.IsMuted(identity));
            Changed?.Invoke();
        }));

        if (options.ShowVoiceControls)
        {
            var slider = new HSlider
            {
                MinValue = VoiceVolumePolicy.Min,
                MaxValue = VoiceVolumePolicy.Max,
                Step = 0.05,
                Value = PositionalVoiceChat.Instance.GetPeerVolume(identity.PeerId),
                CustomMinimumSize = new Vector2(110, 0),
                TooltipText = "How loudly you hear this player"
            };
            var peerId = identity.PeerId;
            slider.ValueChanged += value =>
                PositionalVoiceChat.Instance.SetPeerVolume(peerId, (float)value);
            AddChild(slider);

            var blocked = PlayerIdentityService.Instance.IsBlocked(identity);
            AddChild(TextButton(blocked ? "Unblock" : "Block", () =>
            {
                PlayerIdentityService.Instance.SetBlocked(
                    identity, !PlayerIdentityService.Instance.IsBlocked(identity));
                Changed?.Invoke();
            }));
        }

        if (!options.ShowKick) return;
        AddChild(TextButton("Kick", () =>
        {
            SessionManager.Instance.RequestKick(identity.PeerId, "Removed by the host.");
            Changed?.Invoke();
        }));
    }

    public void RefreshSpeaking()
    {
        if (!IsInstanceValid(_name)) return;
        WriteName(PlayerIdentityService.Instance.Get(_peerId));
    }

    public void RefreshQuality()
    {
        if (!_options.ShowQuality || !IsInstanceValid(_quality)) return;
        _quality.Text = QualityText(_peerId);
    }

    /// <summary>Moved here verbatim from Main.QualityText so both lists format alike.</summary>
    private static string QualityText(long peerId)
    {
        var quality = ConnectionQualityMonitor.Instance.ForPeer(peerId);
        var icon = quality.Health switch
        {
            ConnectionHealth.Excellent => "●",
            ConnectionHealth.Good => "●",
            ConnectionHealth.Degraded => "▲",
            _ => "×"
        };
        return $"{icon} {quality.ShortText}";
    }

    private void WriteName(PlayerIdentity identity)
    {
        var host = identity.IsHost ? "  * HOST" : "";
        var ready = _options.ShowReady ? identity.Ready ? "  READY" : "  NOT READY" : "";
        var away = identity.Afk ? "  (away)" : "";
        var speaking = PositionalVoiceChat.Instance != null &&
                       PositionalVoiceChat.Instance.IsSpeaking(identity.PeerId)
            ? "  <))"
            : "";
        _name.Text = $"{identity.DisplayName}{host}{ready}{away}{speaking}  [{identity.Platform}]";
    }

    private static Button TextButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }
}
```

- [ ] **Step 2: Re-point Main.RenderRoster at the widget**

Replace the body of `RenderRoster` in `Scripts/UI/Main.cs` (tabs, not spaces) so it constructs `PlayerRow` instead of building an `HBoxContainer` inline. Keep the existing `_avatarViews` and `_qualityLabels` bookkeeping by storing the row's `Avatar` and calling `row.RefreshQuality()` from the existing quality refresh path:

```csharp
	private void RenderRoster(PlayerRoster players)
	{
		foreach (var child in _roster.GetChildren()) child.QueueFree();
		_avatarViews.Clear();
		_rows.Clear();
		var ownId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;
		var isHost = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
		foreach (var (peerId, data) in players)
		{
			var identity = PlayerIdentity.FromDictionary(peerId, data);
			var row = new PlayerRow();
			_roster.AddChild(row);
			row.Configure(identity, new PlayerRowOptions(
				ShowReady: true,
				ShowQuality: true,
				ShowVoiceControls: false,
				ShowKick: isHost,
				LocalPeerId: ownId));
			row.Changed += () => RenderRoster(SessionManager.Instance.Players);
			_rows[peerId] = row;
			if (!identity.HasSteamIdentity) continue;
			if (!_avatarViews.TryGetValue(identity.SteamId, out var views))
				_avatarViews[identity.SteamId] = views = new List<TextureRect>();
			views.Add(row.Avatar);
			if (PlayerIdentityService.Instance.TryGetAvatar(identity.SteamId, out var cached))
				row.Avatar.Texture = cached;
			else
				PlayerIdentityService.Instance.RequestAvatar(identity.SteamId);
		}
		var ownReady = players.TryGetValue(ownId, out var own)
					   && own.GetValueOrDefault("ready", false).AsBool();
		_ready.Text = ownReady ? "Unready" : "Ready";
		_start.Visible = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
		UpdateSteamSocialControls();
	}
```

Replace the `_qualityLabels` field with `private readonly Dictionary<long, PlayerRow> _rows = new();` and rewrite the quality-refresh loop near `Scripts/UI/Main.cs:405` as:

```csharp
		foreach (var (_, row) in _rows)
			if (GodotObject.IsInstanceValid(row)) row.RefreshQuality();
```

Then delete `Main.QualityText` (`Scripts/UI/Main.cs:409-420`); `PlayerRow` now owns that formatting and is its only caller.

- [ ] **Step 3: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Manual check: host and join, confirm the lobby roster still renders names, ready state, platform, ping, and the mute button, and that the host now sees a Kick button on the client row that actually removes them.

- [ ] **Step 4: Commit**

```bash
git add Scripts/UI/PlayerRow.cs Scripts/UI/Main.cs
git commit -m "refactor(ui): extract a shared player row widget and add host kick"
```

---

### Task 18: In-match player list panel

**Files:**
- Create: `Scripts/UI/PlayerListPanel.cs`
- Modify: `project.godot`

**Interfaces:**
- Consumes: `PlayerRow`, `PlayerRowOptions` (Task 17); `SessionManager.RosterChanged`, `MatchActive`; `LocalInputContext.SetBlocked`.
- Produces: autoload `PlayerList` at `res://Scripts/UI/PlayerListPanel.cs`; `PlayerListPanel.Instance`.

- [ ] **Step 1: Write the panel**

Create `Scripts/UI/PlayerListPanel.cs`:

```csharp
using Godot;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

using PlayerRoster = Godot.Collections.Dictionary<long, Godot.Collections.Dictionary>;

/// <summary>
/// Tab panel listing everyone in the match with presence and social controls.
/// Client-only. Toggling it frees the mouse through LocalInputContext, matching how
/// every other modal surface in the template behaves.
/// </summary>
public partial class PlayerListPanel : CanvasLayer
{
    public static PlayerListPanel Instance { get; private set; } = null!;
    private const string BlockerId = "player_list";

    private PanelContainer _panel = null!;
    private VBoxContainer _rows = null!;
    private readonly System.Collections.Generic.Dictionary<long, PlayerRow> _entries = new();

    public override void _Ready()
    {
        Instance = this;
        if (DisplayServer.GetName() == "headless")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        Layer = 20;
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.CustomMinimumSize = new Vector2(720, 360);
        AddChild(_panel);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _panel.AddChild(margin);
        _rows = new VBoxContainer();
        margin.AddChild(_rows);
        SessionManager.Instance.RosterChanged += Render;
        SessionManager.Instance.MatchEnded += Close;
    }

    public override void _ExitTree()
    {
        if (SessionManager.Instance == null) return;
        SessionManager.Instance.RosterChanged -= Render;
        SessionManager.Instance.MatchEnded -= Close;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("player_list") || !SessionManager.Instance.MatchActive) return;
        Toggle();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;
        foreach (var row in _entries.Values)
        {
            row.RefreshQuality();
            row.RefreshSpeaking();
        }
    }

    private void Toggle()
    {
        if (_panel.Visible) Close();
        else Open();
    }

    private void Open()
    {
        Render(SessionManager.Instance.Players);
        _panel.Visible = true;
        LocalInputContext.Instance.SetBlocked(BlockerId, true);
    }

    private void Close()
    {
        if (!_panel.Visible) return;
        _panel.Visible = false;
        LocalInputContext.Instance.SetBlocked(BlockerId, false);
    }

    private void Render(PlayerRoster players)
    {
        if (!_panel.Visible) return;
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _entries.Clear();
        var ownId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 0;
        var isHost = Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer();
        foreach (var (peerId, data) in players)
        {
            var identity = PlayerIdentity.FromDictionary(peerId, data);
            var row = new PlayerRow();
            _rows.AddChild(row);
            row.Configure(identity, new PlayerRowOptions(
                ShowReady: false,
                ShowQuality: true,
                ShowVoiceControls: true,
                ShowKick: isHost,
                LocalPeerId: ownId));
            row.Changed += () => Render(SessionManager.Instance.Players);
            _entries[peerId] = row;
            if (!identity.HasSteamIdentity) continue;
            if (PlayerIdentityService.Instance.TryGetAvatar(identity.SteamId, out var cached))
                row.Avatar.Texture = cached;
            else
                PlayerIdentityService.Instance.RequestAvatar(identity.SteamId);
        }
    }
}
```

- [ ] **Step 2: Register the autoload and the action**

Add to the `[autoload]` section of `project.godot`, after `PositionalVoiceChat`:

```ini
PlayerList="*res://Scripts/UI/PlayerListPanel.cs"
```

Add to the `[input]` section, following the exact shape of the existing `push_to_talk` entry. `Tab` is physical keycode `4194306`:

```ini
player_list={
"deadzone": 0.2,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":0,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":4194306,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

`SetInputAsHandled` in `_UnhandledInput` is what stops `Tab` from also driving Godot's `ui_focus_next`.

- [ ] **Step 3: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: PASS.

Manual check: two peers in a match. Press `Tab`; the panel opens, the mouse is freed, and gameplay input stops. Drag the other player's volume slider to zero and confirm their voice goes silent, then reopen the panel after a rejoin and confirm the slider kept its position. Press `Tab` again and confirm the mouse recaptures. As host, kick the client and confirm they return to the menu with the reason shown.

- [ ] **Step 4: Commit**

```bash
git add Scripts/UI/PlayerListPanel.cs project.godot
git commit -m "feat(ui): add the in-match Tab player list"
```

---

### Task 19: Integration coverage

`Tests/NetworkSmoke.cs` runs every peer with `--headless`, so no rig is ever built there. That makes the harness the right place to prove the **headless guard**, AFK replication, and kick authorization; the visual result stays a manual check.

**Files:**
- Modify: `Tests/NetworkSmoke.cs`
- Modify: `Tests/run_multiplayer_smoke.sh`

**Interfaces:**
- Consumes: `NetworkPlayer.Avatar` (Task 12); `SessionManager.SetAfkServer`, `RequestKick` (Task 7).
- Produces: nothing.

- [ ] **Step 1: Add the new state fields**

In `Tests/NetworkSmoke.cs`, beside the existing `_saw*` fields:

```csharp
    private bool _sawPlayerNodes;
    private bool _avatarBuiltHeadless;
    private bool _sawAfkFlag;
    private bool _sawAfkCleared;
    private bool _sawKickReason;
    private bool _requestedRogueKick;
    private bool _startedKickProbe;
    private int _rosterBeforeHostKick;
```

Extend the existing `ConnectionStateChanged` handler in `_Ready` with one more line:

```csharp
            _sawKickReason |= message.Contains("smoke kick");
```

- [ ] **Step 2: Assert the headless avatar guard**

Add `using MultiplayerTemplate.Game;` to the usings, then extend `_Process` — keep the existing physics loop and add:

```csharp
        foreach (var node in GetTree().GetNodesInGroup("network_players"))
            if (node is NetworkPlayer player)
            {
                _sawPlayerNodes = true;
                _avatarBuiltHeadless |= player.Avatar != null;
            }
```

- [ ] **Step 3: Drive and observe the AFK flag**

Add the probe below `SubmitInteractionAfterSpawn`:

```csharp
    private async Task DriveAfkProbe()
    {
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        foreach (var peerId in SessionManager.Instance.Players.Keys)
            if (peerId != 1) SessionManager.Instance.SetAfkServer(peerId, true);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        foreach (var peerId in SessionManager.Instance.Players.Keys)
            if (peerId != 1) SessionManager.Instance.SetAfkServer(peerId, false);
    }
```

Start it from `SubmitFeatureChecks`, beside the existing server-only interaction call:

```csharp
        if (Multiplayer.IsServer()) _ = DriveAfkProbe();
```

Observe it on the client, at the top of `OnRoster`:

```csharp
        if (!Multiplayer.IsServer() && Multiplayer.HasMultiplayerPeer() &&
            players.TryGetValue(Multiplayer.GetUniqueId(), out var mine))
        {
            var afk = mine.GetValueOrDefault("afk", false).AsBool();
            _sawAfkFlag |= afk;
            _sawAfkCleared |= _sawAfkFlag && !afk;
        }
```

- [ ] **Step 4: Scope the existing ready/start logic to the main roles**

The kick roles must not ready up and start a match. Wrap the existing ready-and-start block in `OnRoster`:

```csharp
        if (_role is not ("server" or "client")) return;
```

Place that line immediately before the existing `if (!Multiplayer.IsServer() && !_requestedReady)` check, and after the AFK observation from Step 3.

- [ ] **Step 5: Add the kick roles**

Add the probe below `DriveAfkProbe`:

```csharp
    private async Task DriveKickProbe()
    {
        // Long enough for the rogue client's illegal request to have been served
        // and refused before the roster is sampled.
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        _rosterBeforeHostKick = SessionManager.Instance.Players.Count;
        foreach (var (peerId, data) in SessionManager.Instance.Players)
            if (data.GetValueOrDefault("name", "").AsString() == "KickTarget")
            {
                SessionManager.Instance.RequestKick(peerId, "smoke kick");
                break;
            }
    }
```

Add the role triggers to `OnRoster`, before the Step 4 early return:

```csharp
        if (_role == "kick-server" && Multiplayer.IsServer() && !_startedKickProbe && players.Count == 3)
        {
            _startedKickProbe = true;
            _ = DriveKickProbe();
        }
        if (_role == "kick-rogue" && !_requestedRogueKick && players.Count == 3)
        {
            _requestedRogueKick = true;
            foreach (var (peerId, data) in players)
                if (data.GetValueOrDefault("name", "").AsString() == "KickTarget")
                {
                    SessionManager.Instance.RequestKick(peerId, "rogue kick");
                    break;
                }
        }
```

Add the roles in `_Ready`, beside the existing `host-loss-*` branches:

```csharp
        else if (_role == "kick-server")
        {
            SessionManager.Instance.PlayerName = "KickHost";
            SessionManager.Instance.HostEnet(Port + 2);
            FinishAfter(4, FinishKickServer);
        }
        else if (_role == "kick-target")
        {
            SessionManager.Instance.PlayerName = "KickTarget";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 2);
            FinishAfter(3.5, FinishKickTarget);
        }
        else if (_role == "kick-rogue")
        {
            SessionManager.Instance.PlayerName = "KickRogue";
            SessionManager.Instance.JoinEnet("127.0.0.1", Port + 2);
            FinishAfter(3.5, FinishKickRogue);
        }
```

Add the three reporters beside the existing `Finish*` methods:

```csharp
    private void FinishKickServer()
    {
        var rogueRefused = _rosterBeforeHostKick == 3;
        var hostKickWorked = SessionManager.Instance.Players.Count == 2;
        var success = rogueRefused && hostKickWorked;
        GD.Print(success
            ? "SMOKE KICK SERVER PASS (client request refused, host request honoured)"
            : $"SMOKE KICK SERVER FAIL before={_rosterBeforeHostKick}, " +
              $"after={SessionManager.Instance.Players.Count}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishKickTarget()
    {
        var success = _sawKickReason &&
                      SessionManager.Instance.State == SessionManager.SessionState.Offline;
        GD.Print(success
            ? "SMOKE KICK TARGET PASS (removed with a reason)"
            : $"SMOKE KICK TARGET FAIL reason={_sawKickReason}, " +
              $"state={SessionManager.Instance.State}");
        GetTree().Quit(success ? 0 : 1);
    }

    private void FinishKickRogue()
    {
        var success = _requestedRogueKick &&
                      SessionManager.Instance.State == SessionManager.SessionState.Lobby;
        GD.Print(success
            ? "SMOKE KICK ROGUE PASS (unauthorized request did not remove anyone)"
            : $"SMOKE KICK ROGUE FAIL attempted={_requestedRogueKick}, " +
              $"state={SessionManager.Instance.State}");
        SessionManager.Instance.DisconnectSession(false);
        GetTree().Quit(success ? 0 : 1);
    }
```

- [ ] **Step 6: Fold the new assertions into the existing reporters**

In `FinishServer`, extend the success expression and the failure message:

```csharp
        var avatarGuard = _sawPlayerNodes && !_avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && _securityHelpersPassed &&
                      _sawChat && _sawPhysicsInteraction && avatarGuard &&
                      SessionManager.Instance.Players.Count == 1;
        GD.Print(success
            ? "SMOKE SERVER PASS (3 peers, synchronized load, playing)"
            : $"SMOKE SERVER FAIL roster={SessionManager.Instance.Players}, three={_sawThreePeers}, " +
              $"playing={_sawPlaying}, avatarGuard={avatarGuard}");
```

In `FinishClient`:

```csharp
        var loadedScene = MatchSceneCoordinator.Instance.LoadedSceneCount == 1;
        var avatarGuard = _sawPlayerNodes && !_avatarBuiltHeadless;
        var success = _sawThreePeers && _sawPlaying && loadedScene &&
                      _sawChat && _sawPhysicsInteraction &&
                      _clockSynchronized && _qualitySampled &&
                      avatarGuard && _sawAfkFlag && _sawAfkCleared;
        GD.Print(success
            ? $"SMOKE CLIENT PASS ({_clientName}, synchronized scene and clock)"
            : $"SMOKE CLIENT FAIL name={_clientName}, roster={SessionManager.Instance.Players}, " +
              $"three={_sawThreePeers}, playing={_sawPlaying}, loaded={loadedScene}, " +
              $"clock={_clockSynchronized}, quality={_qualitySampled}, " +
              $"avatarGuard={avatarGuard}, afkSet={_sawAfkFlag}, afkCleared={_sawAfkCleared}");
```

- [ ] **Step 7: Launch the kick scenario from the runner**

In `Tests/run_multiplayer_smoke.sh`, inside the existing `if (( result == 0 ))` block, after the dedicated-server run and before the closing `fi`:

```bash
  kick_dir="$(mktemp -d)"
  godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-server >"$kick_dir/server.log" 2>&1 &
  kick_server=$!
  sleep 0.3
  godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-target >"$kick_dir/target.log" 2>&1 &
  kick_target=$!
  sleep 0.2
  godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-rogue >"$kick_dir/rogue.log" 2>&1 &
  kick_rogue=$!
  wait "$kick_server" || result=1
  wait "$kick_target" || result=1
  wait "$kick_rogue" || result=1
  grep -h "SMOKE" "$kick_dir"/*.log || true
  if grep -E "ERROR:|SMOKE .* FAIL" "$kick_dir"/*.log; then result=1; fi
```

- [ ] **Step 8: Run the full suite**

Run: `bash Tests/run_all_tests.sh`
Expected: xUnit passes; the smoke output includes `SMOKE KICK SERVER PASS`, `SMOKE KICK TARGET PASS`, and `SMOKE KICK ROGUE PASS`; `MULTIPLAYER HARNESS PASS` is printed along with a coverage line.

- [ ] **Step 9: Commit**

```bash
git add Tests/NetworkSmoke.cs Tests/run_multiplayer_smoke.sh
git commit -m "test: cover the headless avatar guard, AFK replication, and kick authorization"
```

---

### Task 20: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/EXTENDING.md`
- Modify: `stretch_features.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Update the README feature list**

Add these bullets to the feature list, in the style of the surrounding lines:

```markdown
- Swappable code-built avatar rig with procedural locomotion animation
- First/third-person camera toggle (C) with a head-anchored aim origin
- In-match player list (Tab) with ping, speaking, mute, block, per-player volume, and host kick
- Server-authoritative AFK detection with an optional dedicated-server idle kick
```

Add `PlayerCamera.cs`, `PlayerNameplate.cs`, `PlayerListPanel.cs`, and `AvatarFactory.cs` to the Architecture list with a one-line responsibility each, matching the existing entries.

- [ ] **Step 2: Document the replacement seam**

Add a "Replace the avatar" section to `docs/EXTENDING.md` covering:

- `template/avatar/scene_path` in Project Settings; empty means the built-in blockman.
- The scene root must derive from `AvatarRig` and implement `SetLocomotion`, `PlayAction`, `StopAction`, `ActionPlaying`, `GetAttachment`, and `ApplyAppearance`.
- The clip names the built-in rig defines: `idle`, `walk`, `run`, `jump`, `fall`, `land`.
- The attachment slots: `Head`, `Back`, `Hips`, `HandLeft`, `HandRight`.
- That locomotion is cosmetic and derived locally, discrete presence is server-authoritative, and any new state that affects gameplay outcomes must be replicated rather than derived.
- That a rig is never built on a headless server.

- [ ] **Step 3: Update the stretch feature list**

In `stretch_features.md`, replace the "Positional voice chat" line under "Optional modules, not core" with a short "Friendslop phases" section recording P1 as implemented and P2 through P5 as planned, pointing at `docs/superpowers/specs/`.

- [ ] **Step 4: Verify and commit**

Run: `bash Tests/run_all_tests.sh`
Expected: PASS.

```bash
git add README.md docs/EXTENDING.md stretch_features.md
git commit -m "docs: document the avatar seam, camera modes, and presence features"
```
