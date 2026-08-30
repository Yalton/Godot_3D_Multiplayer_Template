# Expression (Friendslop P2a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give players emotes — including ones that persist until they move — and canned chat lines, both chosen from one reusable radial wheel and both validated on the server.

**Architecture:** A pure catalogue table defines emotes and canned lines, and that lookup doubles as the server's validation. Clips are authored in C# as bone rotation tracks, like the existing locomotion clips, so the template still ships no art. Looping emote state lives on `NetworkPlayer`, so its lifetime matches the body that owns it; one-shots are fire-and-forget broadcasts. One `RadialWheel` control serves both wheels and knows about neither.

**Tech Stack:** Godot 4.6, C# / .NET 8 (`Godot.NET.Sdk/4.6.2`), xUnit for pure policy classes, `Tests/NetworkSmoke.cs` for multi-process integration.

**Spec:** `docs/superpowers/specs/2026-08-30-expression-design.md`

## Global Constraints

- Godot 4.6, C# only. `net8.0`. `Nullable` and `ImplicitUsings` are enabled project-wide.
- **No new NuGet packages, no new addons, no binary art assets.** Every clip is built in code.
- **Pure policy classes must not reference the `Godot` namespace.** The xUnit project runs with no engine; constructing a Godot type there crashes the test host and aborts the entire run. Use `System.MathF` and plain types. Engine-touching code is covered by `Tests/NetworkSmoke.cs` instead.
- Files under `Scripts/` and `Tests/` use **4 spaces**. `Scripts/UI/Main.cs` and `addons/SettingsManager/SettingsManager.cs` use **TABS**. Match each file's own style.
- The server is always multiplayer peer `1`. Derive identity from `Multiplayer.GetRemoteSenderId()`, never from a peer id in a client payload.
- Every client request RPC passes through `RpcSecurity` (server, sender, session state) and is rate limited via `RpcRateLimiter`. Rejections are logged through `RpcSecurity.Reject`.
- **`NetworkBuildInfo.ProtocolVersion` is bumped exactly once for this phase, from 7 to 8, in Task 5.** Do not bump it again.
- Client-only visual systems no-op on a headless server, following the guard in `Scripts/Communication/PositionalVoiceChat.cs`.
- Warnings and errors are no longer suppressible. Any reject that a client can trigger repeatedly must be throttled, following the `RpcCooldowns` pattern already in `NetworkPlayer._rejectLog`.
- A new input action must be added to `project.godot` **and** to `addons/InputRemapManager/InputRemapManager.tscn`'s `RebindableActionsOverride` and `DisplayNameOverrides`, or it works but never appears in the Controls screen.

---

### Task 1: EmoteCatalogue

**Files:**
- Create: `Scripts/Avatar/EmoteCatalogue.cs`
- Test: `MultiplayerTemplate.Tests/EmoteCatalogueTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct MultiplayerTemplate.Avatar.EmoteDefinition(int Id, string Clip, string DisplayName, bool Looping)`; `static class MultiplayerTemplate.Avatar.EmoteCatalogue` with `static IReadOnlyList<EmoteDefinition> All`, `static bool TryGet(int id, out EmoteDefinition definition)`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/EmoteCatalogueTests.cs`:

```csharp
using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class EmoteCatalogueTests
{
    [Fact]
    public void EveryCatalogueIdResolves()
    {
        Assert.NotEmpty(EmoteCatalogue.All);
        foreach (var entry in EmoteCatalogue.All)
        {
            Assert.True(EmoteCatalogue.TryGet(entry.Id, out var found));
            Assert.Equal(entry.Clip, found.Clip);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void UnknownIdsAreRejected(int id)
    {
        Assert.False(EmoteCatalogue.TryGet(id, out _));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = EmoteCatalogue.All.Select(entry => entry.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ClipNamesAreUniqueAndNonEmpty()
    {
        var clips = EmoteCatalogue.All.Select(entry => entry.Clip).ToList();
        Assert.All(clips, clip => Assert.False(string.IsNullOrWhiteSpace(clip)));
        Assert.Equal(clips.Count, clips.Distinct().Count());
    }

    [Fact]
    public void CatalogueContainsBothOneShotsAndLoops()
    {
        Assert.Contains(EmoteCatalogue.All, entry => !entry.Looping);
        Assert.Contains(EmoteCatalogue.All, entry => entry.Looping);
    }

    [Fact]
    public void ZeroIsReservedForNoEmote()
    {
        Assert.DoesNotContain(EmoteCatalogue.All, entry => entry.Id == 0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~EmoteCatalogueTests --verbosity minimal`
Expected: FAIL to compile — `EmoteCatalogue` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Avatar/EmoteCatalogue.cs`:

```csharp
namespace MultiplayerTemplate.Avatar;

/// <summary>One emote. <see cref="Clip"/> must name an animation the rig defines.</summary>
public readonly record struct EmoteDefinition(int Id, string Clip, string DisplayName, bool Looping);

/// <summary>
/// The emote table. This lookup is also the server's validation: an id absent here is
/// rejected, so a client cannot index anything with an arbitrary integer. Id 0 is reserved
/// for "no emote". Pure and engine-free so it can be unit tested; must not reference the
/// Godot namespace.
///
/// To add an emote: add a row here and a clip of the same name in
/// ProceduralAnimationLibrary. To replace the set wholesale, replace this table — a rig
/// that defines different clip names owns what those names mean.
/// </summary>
public static class EmoteCatalogue
{
    public const int None = 0;

    private static readonly EmoteDefinition[] Entries =
    [
        new(1, "emote_wave", "Wave", false),
        new(2, "emote_point", "Point", false),
        new(3, "emote_clap", "Clap", false),
        new(4, "emote_cheer", "Cheer", false),
        new(5, "emote_sit", "Sit", true),
        new(6, "emote_dance", "Dance", true),
    ];

    public static IReadOnlyList<EmoteDefinition> All => Entries;

    public static bool TryGet(int id, out EmoteDefinition definition)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id != id) continue;
            definition = entry;
            return true;
        }
        definition = default;
        return false;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~EmoteCatalogueTests --verbosity minimal`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Avatar/EmoteCatalogue.cs MultiplayerTemplate.Tests/EmoteCatalogueTests.cs
git commit -m "feat(emotes): add the pure emote catalogue"
```

---

### Task 2: QuickChatLines

**Files:**
- Create: `Scripts/Communication/QuickChatLines.cs`
- Test: `MultiplayerTemplate.Tests/QuickChatLinesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct MultiplayerTemplate.Communication.QuickChatLine(int Id, string Text)`; `static class MultiplayerTemplate.Communication.QuickChatLines` with `static IReadOnlyList<QuickChatLine> All` and `static bool TryGet(int id, out QuickChatLine line)`.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/QuickChatLinesTests.cs`:

```csharp
using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class QuickChatLinesTests
{
    [Fact]
    public void EveryLineResolves()
    {
        Assert.NotEmpty(QuickChatLines.All);
        foreach (var line in QuickChatLines.All)
        {
            Assert.True(QuickChatLines.TryGet(line.Id, out var found));
            Assert.Equal(line.Text, found.Text);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9999)]
    public void UnknownIdsAreRejected(int id)
    {
        Assert.False(QuickChatLines.TryGet(id, out _));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = QuickChatLines.All.Select(line => line.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void TextSurvivesChatSanitisationUnchanged()
    {
        foreach (var line in QuickChatLines.All)
            Assert.Equal(line.Text, ChatPolicy.Sanitize(line.Text));
    }
}
```

The last test matters: a canned line that the chat sanitiser would alter means the table and
the chat path disagree about what is displayable. `ChatPolicy` already exists and is pure;
read `Scripts/Communication/TextChatManager.cs` for its namespace and add the using the test
needs.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~QuickChatLinesTests --verbosity minimal`
Expected: FAIL to compile — `QuickChatLines` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/Communication/QuickChatLines.cs`:

```csharp
namespace MultiplayerTemplate.Communication;

public readonly record struct QuickChatLine(int Id, string Text);

/// <summary>
/// Canned chat lines, sent by id so no client-supplied string ever becomes displayable
/// text. This lookup is the server's validation. Id 0 is reserved. Pure and engine-free;
/// must not reference the Godot namespace.
/// </summary>
public static class QuickChatLines
{
    private static readonly QuickChatLine[] Entries =
    [
        new(1, "Hello!"),
        new(2, "Yes"),
        new(3, "No"),
        new(4, "Follow me"),
        new(5, "Wait here"),
        new(6, "Nice one"),
        new(7, "Sorry"),
        new(8, "Help!"),
    ];

    public static IReadOnlyList<QuickChatLine> All => Entries;

    public static bool TryGet(int id, out QuickChatLine line)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id != id) continue;
            line = entry;
            return true;
        }
        line = default;
        return false;
    }
}
```

If `ChatPolicy.Sanitize` alters any of these strings, change the string rather than the
sanitiser, and say which in your report.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~QuickChatLinesTests --verbosity minimal`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Communication/QuickChatLines.cs MultiplayerTemplate.Tests/QuickChatLinesTests.cs
git commit -m "feat(chat): add the canned quick-chat line table"
```

---

### Task 3: RadialWheelMath

**Files:**
- Create: `Scripts/UI/RadialWheelMath.cs`
- Test: `MultiplayerTemplate.Tests/RadialWheelMathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MultiplayerTemplate.UI.RadialWheelMath` with `const float DeadZone = 40f` and `static int Select(float x, float y, int count, float deadZone)` returning a zero-based wedge index, or `-1` inside the dead zone or when `count <= 0`.

Wedge 0 is centred straight up (negative Y in screen space), and wedges advance clockwise.
Pinning that convention in tests is the point of this task: it is the part a reader cannot
verify by looking.

- [ ] **Step 1: Write the failing test**

Create `MultiplayerTemplate.Tests/RadialWheelMathTests.cs`:

```csharp
using MultiplayerTemplate.UI;

namespace MultiplayerTemplate.Tests;

public sealed class RadialWheelMathTests
{
    private const float Zone = 40f;

    [Fact]
    public void InsideTheDeadZoneSelectsNothing()
    {
        Assert.Equal(-1, RadialWheelMath.Select(0f, 0f, 6, Zone));
        Assert.Equal(-1, RadialWheelMath.Select(Zone - 1f, 0f, 6, Zone));
    }

    [Fact]
    public void OnTheDeadZoneEdgeSelects()
    {
        Assert.NotEqual(-1, RadialWheelMath.Select(0f, -Zone, 6, Zone));
    }

    [Fact]
    public void StraightUpIsWedgeZero()
    {
        Assert.Equal(0, RadialWheelMath.Select(0f, -100f, 4, Zone));
    }

    [Fact]
    public void WedgesAdvanceClockwise()
    {
        Assert.Equal(1, RadialWheelMath.Select(100f, 0f, 4, Zone));
        Assert.Equal(2, RadialWheelMath.Select(0f, 100f, 4, Zone));
        Assert.Equal(3, RadialWheelMath.Select(-100f, 0f, 4, Zone));
    }

    [Fact]
    public void EveryDirectionLandsInRange()
    {
        for (var degrees = 0; degrees < 360; degrees += 7)
        {
            var radians = degrees * MathF.PI / 180f;
            var index = RadialWheelMath.Select(
                MathF.Cos(radians) * 100f, MathF.Sin(radians) * 100f, 6, Zone);
            Assert.InRange(index, 0, 5);
        }
    }

    [Fact]
    public void NoEntriesSelectsNothing()
    {
        Assert.Equal(-1, RadialWheelMath.Select(0f, -100f, 0, Zone));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~RadialWheelMathTests --verbosity minimal`
Expected: FAIL to compile — `RadialWheelMath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Scripts/UI/RadialWheelMath.cs`:

```csharp
namespace MultiplayerTemplate.UI;

/// <summary>
/// Direction-to-wedge selection for the radial wheel. Wedge 0 is centred straight up
/// (negative Y in screen space) and wedges advance clockwise. Pure and engine-free so it
/// can be unit tested; must not reference the Godot namespace.
/// </summary>
public static class RadialWheelMath
{
    public const float DeadZone = 40f;

    public static int Select(float x, float y, int count, float deadZone)
    {
        if (count <= 0) return -1;
        if (MathF.Sqrt(x * x + y * y) < deadZone) return -1;
        // Atan2(x, -y) puts zero straight up and grows clockwise, which is how a wheel
        // reads; the raw Atan2(y, x) would put zero to the right and grow anticlockwise.
        var angle = MathF.Atan2(x, -y);
        if (angle < 0) angle += MathF.Tau;
        var wedge = MathF.Tau / count;
        var index = (int)MathF.Floor((angle + wedge * 0.5f) / wedge);
        return index % count;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~RadialWheelMathTests --verbosity minimal`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add Scripts/UI/RadialWheelMath.cs MultiplayerTemplate.Tests/RadialWheelMathTests.cs
git commit -m "feat(ui): add pure radial wheel selection maths"
```

---

### Task 4: Emote clips

**Files:**
- Modify: `Scripts/Avatar/ProceduralAnimationLibrary.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: six new clips registered in the returned `AnimationLibrary`, named exactly as `EmoteCatalogue` spells them: `emote_wave`, `emote_point`, `emote_clap`, `emote_cheer`, `emote_sit`, `emote_dance`. Add matching name constants beside the existing `Idle` / `Walk` / … constants.

Read the whole file first. Author these exactly as the existing clips are authored — via
`NewAnimation`, `AddRotationTrack`, `Pitch` and `Roll`, against the bone names already used
(`hips`, `spine`, `head`, `upperarm.L/R`, `thigh.L/R`, `shin.L/R`). Do not add helpers unless
an existing one genuinely cannot express a pose.

**`emote_sit` and `emote_dance` must loop** (`NewAnimation(..., loop: true)`); the other four
must not. That is what makes a looping emote persist: `AnimationNodeOneShot` ends when its
sub-animation finishes, and a looping clip never finishes, so it plays until `StopAction()`.
Getting this backwards makes sitting a half-second twitch.

**Loop seams:** for the two looping clips, the key at time 0 must equal the key at the clip's
full length for every bone, or the emote visibly pops every cycle. The existing `BuildIdle`
and `BuildStride` do this; match them.

Suggested shapes, adjust as needed for legibility:
- `emote_wave` — 1.2s, right upper arm raised via `Roll`, two small `Pitch` oscillations.
- `emote_point` — 0.8s, right arm forward via `Pitch`, held, slight spine lean.
- `emote_clap` — 1.0s, both upper arms swinging together and apart via `Roll`.
- `emote_cheer` — 1.0s, both arms up via `Roll`, hips bobbing via a rotation on `hips`.
- `emote_sit` — 2.0s loop, thighs forward via `Pitch`, shins down, spine slightly back; near-static with a small breathing motion on `spine`.
- `emote_dance` — 1.6s loop, alternating hips rotation and arm `Roll`, thighs pitching in counterphase.

- [ ] **Step 1: Add the constants and builders**

Add six constants beside the existing clip-name constants, then six private builders, then
register all six in `Build()` beside the existing `library.AddAnimation(...)` calls.

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 3: Verify every catalogue clip exists**

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --verbosity minimal`
Expected: all pass.

Then confirm by grep that every `Clip` string in `EmoteCatalogue` appears as a registered
animation name in `ProceduralAnimationLibrary`, and report the comparison as two lists. A
mismatch here fails silently at runtime — Godot ignores an unknown clip name — so the build
and the tests will not catch it.

- [ ] **Step 4: Verify the engine loads them**

Run: `godot --headless --path . --quit-after 120`
Expected: clean start and exit, with no error mentioning `Animation`, `track`, or `Quaternion`.

- [ ] **Step 5: Commit**

```bash
git add Scripts/Avatar/ProceduralAnimationLibrary.cs
git commit -m "feat(emotes): author the six emote clips in code"
```

---

### Task 5: Emote state and RPCs on NetworkPlayer

**Files:**
- Modify: `Scripts/Game/NetworkPlayer.cs`
- Modify: `Scripts/Network/NetworkBuildInfo.cs`

**Interfaces:**
- Consumes: `EmoteCatalogue` (Task 1); the clips from Task 4.
- Produces: `int NetworkPlayer.ActiveEmote { get; private set; }`, `void NetworkPlayer.RequestEmote(int id)`, and a server-authoritative `ReceiveEmote` broadcast.

This is where the phase's wire changes land. **Bump `NetworkBuildInfo.ProtocolVersion` from 7 to 8 here, and nowhere else.**

- [ ] **Step 1: Add the state and the request path**

Add fields beside the existing ones:

```csharp
    private readonly RpcRateLimiter _emoteLimiter = new();
    public int ActiveEmote { get; private set; } = EmoteCatalogue.None;
```

Add the local request entry point and the server handler. Follow the shape of `SubmitInput`
exactly — same guard order, same reject style:

```csharp
    public void RequestEmote(int id)
    {
        if (!IsLocalPlayer) return;
        if (Multiplayer.IsServer()) SubmitEmoteServer(Multiplayer.GetUniqueId(), id);
        else RpcId(1, nameof(SubmitEmote), id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitEmote(int id)
    {
        if (!RpcSecurity.RequireServer(this, nameof(SubmitEmote))) return;
        var sender = Multiplayer.GetRemoteSenderId();
        if (!RpcSecurity.RequireSender(sender, PeerId, nameof(SubmitEmote)) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(SubmitEmote),
                SessionManager.SessionState.Playing)) return;
        SubmitEmoteServer(sender, id);
    }

    private void SubmitEmoteServer(long sender, int id)
    {
        if (!_emoteLimiter.Allow(sender, nameof(SubmitEmote), 4, 2000))
        {
            // Throttled: a wheel is trivially spammable and warnings are no longer suppressed.
            if (_rejectLog.TryConsume(sender, nameof(SubmitEmote), 1000))
                RpcSecurity.Reject(nameof(SubmitEmote), sender, "rate limit exceeded");
            return;
        }
        if (id != EmoteCatalogue.None && !EmoteCatalogue.TryGet(id, out _))
        {
            if (_rejectLog.TryConsume(sender, nameof(SubmitEmote), 1000))
                RpcSecurity.Reject(nameof(SubmitEmote), sender, $"unknown emote {id}");
            return;
        }
        SetEmoteServer(id);
    }
```

- [ ] **Step 2: Add the authoritative setter and the broadcast**

```csharp
    private void SetEmoteServer(int id)
    {
        if (!Multiplayer.IsServer()) return;
        // Only looping emotes are state. A one-shot is broadcast and forgotten, so a peer
        // that joins later correctly sees nothing.
        var looping = EmoteCatalogue.TryGet(id, out var definition) && definition.Looping;
        ActiveEmote = looping ? id : EmoteCatalogue.None;
        Rpc(nameof(ReceiveEmote), id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEmote(int id)
    {
        if (!Multiplayer.IsServer())
            ActiveEmote = EmoteCatalogue.TryGet(id, out var d) && d.Looping ? id : EmoteCatalogue.None;
        PlayEmoteLocally(id);
    }

    public void PlayEmoteLocally(int id)
    {
        if (_avatar == null) return;
        if (id == EmoteCatalogue.None)
        {
            _avatar.StopAction();
            if (IsLocalPlayer) _view.PopTemporary("emote");
            return;
        }
        if (!EmoteCatalogue.TryGet(id, out var definition)) return;
        _avatar.PlayAction(definition.Clip);
        if (IsLocalPlayer) _view.PushTemporaryThirdPerson("emote");
    }
```

`PlayEmoteLocally` is public because Task 6 calls it when spawning a late joiner's view of an
already-emoting player.

- [ ] **Step 3: Cancel a looping emote on movement**

In `SubmitInput`, immediately after `NoteActivity(move, jump, yaw)`, add:

```csharp
        // Movement ends a looping emote. NoteActivity already decides what counts as the
        // player doing something; reuse that rather than inventing a second notion.
        if (ActiveEmote != EmoteCatalogue.None &&
            (move.LengthSquared() > 0f || jump)) SetEmoteServer(EmoteCatalogue.None);
```

Add the same cancel in the server-hosted local player branch of `_PhysicsProcess`, right after
its `NoteActivity(local.Move, local.Jump, _yaw)` call, using `local.Move` and `local.Jump`.

- [ ] **Step 4: Pop the camera when a one-shot ends**

In `_PhysicsProcess`, for the local player only, pop the temporary camera once the rig reports
the action finished and no looping emote is active:

```csharp
        if (IsLocalPlayer && _avatar is { ActionPlaying: false } &&
            ActiveEmote == EmoteCatalogue.None) _view.PopTemporary("emote");
```

`PopTemporary` is idempotent — it returns early when the owner is not present — so calling it
every frame is safe.

- [ ] **Step 5: Bump the protocol**

In `Scripts/Network/NetworkBuildInfo.cs`, change `ProtocolVersion` from `7` to `8`.

- [ ] **Step 6: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --verbosity minimal`
Expected: all pass, no test host crash.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: `MULTIPLAYER HARNESS PASS`. A protocol mismatch or an RPC arity error surfaces here.

- [ ] **Step 7: Commit**

```bash
git add Scripts/Game/NetworkPlayer.cs Scripts/Network/NetworkBuildInfo.cs
git commit -m "feat(emotes): add server-authoritative emote state and RPCs"
```

---

### Task 6: Late joiners see an emote in progress

**Files:**
- Modify: `Scripts/Game/GameWorld.cs`

**Interfaces:**
- Consumes: `NetworkPlayer.ActiveEmote`, `NetworkPlayer.PlayEmoteLocally` (Task 5).
- Produces: `SpawnPlayer` carries an emote id.

This is the one path that reads emote state from outside the node that owns it, and therefore
the only one needing explicit handling. Everywhere else the state dies with the body.

- [ ] **Step 1: Carry the active emote through the spawn RPC**

`SpawnPlayer` currently takes `(long peerId, Vector3 position)`. Add a third argument for the
emote id, and apply it after `Setup` has run and the avatar is attached:

```csharp
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayer(long peerId, Vector3 position, int activeEmote)
    {
        if (_players.ContainsKey(peerId)) return;
        var player = MakePlayer(peerId);
        _players[peerId] = player;
        AddChild(player);
        player.GlobalPosition = position;
        if (activeEmote != EmoteCatalogue.None) player.PlayEmoteLocally(activeEmote);
    }
```

Update **every** call site to pass the id. `OnMatchStarted` passes `EmoteCatalogue.None` — a
match has just started and nobody is emoting. `SpawnForLateJoin` passes the existing player's
current `ActiveEmote` for each already-present peer, and `EmoteCatalogue.None` for the joining
peer. Read the method and update both loops.

An `Rpc` argument-count mismatch fails at runtime, not compile time. Check each call site.

- [ ] **Step 2: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: `MULTIPLAYER HARNESS PASS`. The harness exercises late join, so a broken spawn
signature shows up here.

- [ ] **Step 3: Commit**

```bash
git add Scripts/Game/GameWorld.cs
git commit -m "feat(emotes): show late joiners an emote already in progress"
```

---

### Task 7: The radial wheel control

**Files:**
- Create: `Scripts/UI/RadialWheel.cs`

**Interfaces:**
- Consumes: `RadialWheelMath` (Task 3).
- Produces: `partial class MultiplayerTemplate.UI.RadialWheel : Control` with `void Open(IReadOnlyList<(string Label, int Id)> entries)`, `void Close()`, `bool IsOpen { get; }`, `int Commit()` returning the selected id or `0`, and `void AccumulateMotion(Vector2 relative)`.

The wheel knows nothing about emotes or chat. It draws labels around a circle, tracks an
accumulated direction, and reports what was selected.

- [ ] **Step 1: Write the control**

Create `Scripts/UI/RadialWheel.cs`:

```csharp
using Godot;

namespace MultiplayerTemplate.UI;

/// <summary>
/// A held radial selector. It knows nothing about what it is selecting: callers hand it
/// (label, id) entries and read back the committed id. Client-only; a dedicated server
/// never builds one.
/// </summary>
public partial class RadialWheel : Control
{
    private const float Radius = 140f;
    private const float MaxDrift = 240f;

    private readonly List<(string Label, int Id)> _entries = new();
    private Vector2 _direction;

    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        // The wheel never eats clicks; it is driven entirely by relative motion.
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public void Open(IReadOnlyList<(string Label, int Id)> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        _direction = Vector2.Zero;
        IsOpen = true;
        Visible = true;
        QueueRedraw();
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
        _entries.Clear();
    }

    public void AccumulateMotion(Vector2 relative)
    {
        if (!IsOpen) return;
        _direction = (_direction + relative).LimitLength(MaxDrift);
        QueueRedraw();
    }

    /// <summary>The selected id, or 0 when the pointer is inside the dead zone.</summary>
    public int Commit()
    {
        var index = RadialWheelMath.Select(
            _direction.X, _direction.Y, _entries.Count, RadialWheelMath.DeadZone);
        return index < 0 ? 0 : _entries[index].Id;
    }

    public override void _Draw()
    {
        if (!IsOpen || _entries.Count == 0) return;
        var centre = Size * 0.5f;
        var selected = RadialWheelMath.Select(
            _direction.X, _direction.Y, _entries.Count, RadialWheelMath.DeadZone);
        var font = GetThemeDefaultFont();
        var fontSize = GetThemeDefaultFontSize();
        DrawCircle(centre, RadialWheelMath.DeadZone, new Color(0, 0, 0, 0.35f));
        for (var i = 0; i < _entries.Count; i++)
        {
            // Wedge 0 is straight up and wedges advance clockwise, matching RadialWheelMath.
            var angle = i * Mathf.Tau / _entries.Count;
            var offset = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * Radius;
            var label = _entries[i].Label;
            var width = font.GetStringSize(label, fontSize: fontSize).X;
            var colour = i == selected ? Colors.White : new Color(1, 1, 1, 0.55f);
            DrawString(font, centre + offset - new Vector2(width * 0.5f, 0),
                label, HorizontalAlignment.Left, -1, fontSize, colour);
        }
    }
}
```

Two details a reader should not have to rediscover: `MouseFilterEnum.Ignore` keeps the wheel
from swallowing clicks, and the `(Sin, -Cos)` offset is what makes the drawn layout agree with
`RadialWheelMath`'s convention — get one of them backwards and the highlight sits on a
different wedge than the one that fires.

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add Scripts/UI/RadialWheel.cs
git commit -m "feat(ui): add the reusable radial wheel control"
```

---

### Task 8: Wire the emote wheel

**Files:**
- Create: `Scripts/UI/ExpressionWheels.cs`
- Modify: `project.godot`
- Modify: `addons/InputRemapManager/InputRemapManager.tscn`

**Interfaces:**
- Consumes: `RadialWheel` (Task 7), `EmoteCatalogue` (Task 1), `NetworkPlayer.RequestEmote` (Task 5).
- Produces: autoload `ExpressionWheels` at `res://Scripts/UI/ExpressionWheels.cs`.

- [ ] **Step 1: Write the controller**

Create `Scripts/UI/ExpressionWheels.cs` — a `CanvasLayer` autoload owning one `RadialWheel`,
opened by whichever action is held.

The input handling is the part that matters:

- Handle everything in **`_Input`**, not `_UnhandledInput`. `NetworkPlayer._UnhandledInput`
  consumes mouse motion for camera look and runs after the GUI phase; a wheel that handled
  motion there would never win. The in-match player list hit exactly this in P1.
- On `@event.IsActionPressed("emote_wheel")` and the session being in a match: open the wheel
  with the emote entries, and `SetInputAsHandled()`.
- While open, on `InputEventMouseMotion`, call `AccumulateMotion(motion.Relative)` and
  `SetInputAsHandled()` — this is what suppresses camera look for exactly as long as the
  wheel is held.
- On `@event.IsActionReleased("emote_wheel")`, `Commit()`, close, `SetInputAsHandled()`, and
  if the committed id is non-zero, find the local player and call `RequestEmote(id)`.
- **Do not call `LocalInputContext.SetBlocked`.** The mouse must stay captured and the player
  must keep walking. That is the opposite of the Tab panel's requirement and is deliberate.
- Guard everything on not being headless, and on `SessionManager.Instance.MatchActive`.

To reach the local player, iterate the `network_players` group and match
`player.PeerId == Multiplayer.GetUniqueId()`, the same pattern `PositionalVoiceChat` uses.

- [ ] **Step 2: Register the action and the autoload**

In `project.godot`, add to `[input]`, following the exact shape of the existing `push_to_talk`
entry. `B` is physical keycode `66`:

```ini
emote_wheel={
"deadzone": 0.2,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":0,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":66,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

Add to `[autoload]`, after the existing `PlayerList` entry:

```ini
ExpressionWheels="*res://Scripts/UI/ExpressionWheels.cs"
```

In `addons/InputRemapManager/InputRemapManager.tscn`, append `"emote_wheel"` to
`RebindableActionsOverride` and add `"emote_wheel": "Emote Wheel"` to `DisplayNameOverrides`.
Read the file first and match its formatting; do not reorder existing entries.

- [ ] **Step 3: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

Run: `godot --headless --path . --quit-after 120`
Expected: clean start and exit. Quote the `[InputRemapManager] Rebindable actions: N` line —
it must have increased by one.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: `MULTIPLAYER HARNESS PASS`. The new autoload runs in every smoke peer including the
dedicated-server check, so this gates the headless guard.

- [ ] **Step 4: Commit**

```bash
git add Scripts/UI/ExpressionWheels.cs project.godot addons/InputRemapManager/InputRemapManager.tscn
git commit -m "feat(emotes): add the held emote wheel"
```

---

### Task 9: Wire the quick-chat wheel

**Files:**
- Modify: `Scripts/UI/ExpressionWheels.cs`
- Modify: `Scripts/Communication/TextChatManager.cs`
- Modify: `project.godot`
- Modify: `addons/InputRemapManager/InputRemapManager.tscn`

**Interfaces:**
- Consumes: `QuickChatLines` (Task 2), the wheel from Tasks 7-8.
- Produces: `void TextChatManager.SubmitQuickChat(int id)` and a validated `RequestQuickChat` RPC.

- [ ] **Step 1: Add the server path**

In `Scripts/Communication/TextChatManager.cs`, add a public entry point and an RPC beside the
existing `SubmitText` / `RequestMessage` pair, following their shape exactly:

```csharp
    public void SubmitQuickChat(int id)
    {
        if (!Multiplayer.HasMultiplayerPeer()) return;
        if (Multiplayer.IsServer()) AcceptQuickChat(Multiplayer.GetUniqueId(), id);
        else RpcId(1, nameof(RequestQuickChat), id);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestQuickChat(int id)
    {
        if (!RpcSecurity.RequireServer(this, nameof(RequestQuickChat)) ||
            !RpcSecurity.RequireKnownSender(this, nameof(RequestQuickChat), out var sender) ||
            !RpcSecurity.RequireState(SessionManager.Instance.State, nameof(RequestQuickChat),
                SessionManager.SessionState.Lobby, SessionManager.SessionState.Playing)) return;
        AcceptQuickChat(sender, id);
    }

    private void AcceptQuickChat(long peerId, int id)
    {
        if (!_rateLimiter.Allow(peerId, nameof(RequestQuickChat), 5, 4000))
        {
            RpcSecurity.Reject(nameof(RequestQuickChat), peerId, "rate limit exceeded");
            return;
        }
        if (!QuickChatLines.TryGet(id, out var line))
        {
            RpcSecurity.Reject(nameof(RequestQuickChat), peerId, $"unknown quick chat line {id}");
            return;
        }
        AcceptMessage(peerId, line.Text);
    }
```

Read the existing `RequestMessage` first and match its guard order and rate-limit numbers.
Reuse the existing `_rateLimiter` field rather than adding a second limiter.

Note the canned text goes through `AcceptMessage`, so the existing mute/block filtering and
display path apply unchanged, and no client-supplied string is ever displayed.

- [ ] **Step 2: Wire the second wheel**

In `ExpressionWheels`, generalise what Task 8 built so the same wheel serves either action:
hold `quick_chat_wheel` to open with the `QuickChatLines` entries, and on commit call
`TextChatManager.Instance.SubmitQuickChat(id)`. Only one wheel may be open at a time; ignore a
second action pressed while one is held.

- [ ] **Step 3: Register the action**

`project.godot` `[input]`, same shape as before. `Z` is physical keycode `90`:

```ini
quick_chat_wheel={
"deadzone": 0.2,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":0,"window_id":0,"alt_pressed":false,"shift_pressed":false,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":90,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

And add `"quick_chat_wheel"` plus a `"Quick Chat Wheel"` display name to
`InputRemapManager.tscn`, as in Task 8.

- [ ] **Step 4: Verify**

Run: `dotnet build MultiplayerTemplate.csproj --verbosity minimal`
Expected: Build succeeded, 0 errors, 0 warnings.

Run: `dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --verbosity minimal`
Expected: all pass.

Run: `godot --headless --path . --quit-after 120` — quote the rebindable-actions count; it
must have increased by one again.

Run: `bash Tests/run_multiplayer_smoke.sh`
Expected: `MULTIPLAYER HARNESS PASS`. The harness sends a chat message and asserts it arrives,
so a broken chat path fails here.

- [ ] **Step 5: Commit**

```bash
git add Scripts/UI/ExpressionWheels.cs Scripts/Communication/TextChatManager.cs project.godot addons/InputRemapManager/InputRemapManager.tscn
git commit -m "feat(chat): add the held quick-chat wheel"
```

---

### Task 10: Integration coverage

**Files:**
- Modify: `Tests/NetworkSmoke.cs`
- Modify: `Tests/run_multiplayer_smoke.sh` if a new role is needed

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

Read `Tests/NetworkSmoke.cs` end to end first and follow its existing structure exactly: roles
selected from `--smoke-role=`, `FinishAfter` deadlines, one `SMOKE <ROLE> PASS/FAIL` line per
role with diagnostics, and `GetTree().Quit(0 or 1)`.

Add scenarios proving what unit tests cannot:

1. **A looping emote replicates.** After the match starts, one client calls `RequestEmote` with
   a looping id; another client observes that player's `ActiveEmote` become that id.
2. **Movement cancels it, and the cancel replicates.** The emoting client then submits a
   non-zero move; the observing client sees `ActiveEmote` return to `EmoteCatalogue.None`.
3. **A late joiner sees an emote in progress.** Verify through the existing late-join path
   that a peer joining while someone is sitting learns the active emote.
4. **An unknown emote id is refused without disconnecting.** A client requests id `9999`; it
   stays connected and its `ActiveEmote` remains `None`.

Fold each new flag into the relevant role's success expression and failure diagnostics, as the
existing checks are.

Timing: the existing deadlines were tuned deliberately — host 16s, clients 9s, staggered
launches. Fit new work inside them; do not shorten any existing deadline.

- [ ] **Step 1: Add the scenarios**

- [ ] **Step 2: Run the full suite three times**

Run: `bash Tests/run_all_tests.sh`, then `bash Tests/run_multiplayer_smoke.sh` twice more.

Expected: all three green, with every SMOKE line passing. Quote all three results. This
harness coordinates real processes by wall-clock timing and is sensitive to machine load; one
green run is not evidence. If runs disagree, say so plainly rather than re-running until it
looks clean.

- [ ] **Step 3: Commit**

```bash
git add Tests/NetworkSmoke.cs Tests/run_multiplayer_smoke.sh
git commit -m "test: cover emote replication, cancellation, and late join"
```

---

### Task 11: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/EXTENDING.md`
- Modify: `stretch_features.md`

- [ ] **Step 1: README**

Add to the feature list, in the surrounding terse style:

```markdown
- Held emote wheel (B) with one-shot and looping emotes, cancelled by movement
- Held quick-chat wheel (Z) sending canned lines by id, never free text
```

- [ ] **Step 2: EXTENDING.md**

Add a short section covering:

- **Adding an emote:** a row in `EmoteCatalogue` and a clip of the same name in
  `ProceduralAnimationLibrary`. Note that the catalogue lookup is also the server's
  validation, so an id absent from the table is rejected rather than trusted.
- **Replacing the set:** replace the table; a rig defining different clip names owns what
  those names mean.
- **The degradation a replacement rig gets:** `PlayAction` ignores clip names the rig does not
  define, so a custom rig with no `emote_sit` plays nothing while the state still replicates —
  that player is genuinely sitting to the server while standing still visually. Consistent
  world state, missing cosmetic. State it plainly so nobody debugs it twice.
- **Quick-chat lines** are ids, not strings; adding one is a row in `QuickChatLines`.

Cross-reference the state lifetime rule already documented there: the looping emote lives on
`NetworkPlayer` so that it dies with the body, and the spawn path is the single place the
state is read from outside that node.

- [ ] **Step 3: stretch_features.md**

Mark P2a implemented; record P2b (appearance, cosmetic catalogue, customization screen,
profile persistence) as next, pointing at the specs directory.

- [ ] **Step 4: Verify and commit**

Run: `bash Tests/run_all_tests.sh`
Expected: green.

```bash
git add README.md docs/EXTENDING.md stretch_features.md
git commit -m "docs: document emotes, quick chat, and the catalogue seams"
```
