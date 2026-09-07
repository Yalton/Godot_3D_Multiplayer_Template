# Voice Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Replace raw-PCM global voice relay with validated Opus proximity/global voice, opt-in VAD, persisted microphone settings, and bounded receiver playback.

**Architecture:** PositionalVoiceChat remains the Godot lifecycle/capture/playback coordinator. Pure focused classes own wire format, framing, VAD, budgets, routing, jitter, and the codec boundary. The server routes validated opaque packets from authoritative player bodies; graphical clients decode spatial or global streams.

**Tech Stack:** Godot 4.6 C#, .NET 8, ENet channel 2, xUnit, managed Concentus 2.2.2.

**Spec:** docs/superpowers/specs/2026-09-07-voice-overhaul-design.md

## Global Constraints

- Add only managed Concentus 2.2.2; never add Concentus.Native, another NuGet package, an addon, or binary asset.
- Bump NetworkBuildInfo.ProtocolVersion once, 9 to 10, in Task 5.
- Sender identity comes only from GetRemoteSenderId(). Every request uses RpcSecurity, RpcRateLimiter, and RpcSecurity.Reject.
- Keep UnreliableOrdered channel 2. Pure policy files may not reference Godot.
- PTT is default; VAD is opt-in; both are privacy-muted while GameplaySuppressed.
- Headless dedicated servers route voice but make no microphone, decoder, playback, or UI nodes.

---

### Task 1: Define pure voice transport policy

**Files:**
- Create: Scripts/Communication/VoiceWire.cs, VoiceFrameAssembler.cs, VoiceActivityDetector.cs, VoiceRoutingPolicy.cs
- Create: MultiplayerTemplate.Tests/VoiceWireTests.cs, VoiceFrameAssemblerTests.cs, VoiceActivityDetectorTests.cs, VoiceRoutingPolicyTests.cs

**Interfaces:**
- Produce VoiceChannel, VoicePoint, VoicePacket, VoiceWirePolicy, VoiceFrameAssembler, VoiceActivityDetector, VoiceRoutingPolicy.
- VoicePacket is record struct VoicePacket(int FormatVersion, VoiceChannel Channel, uint Sequence, byte[] Payload).
- VoiceRoutingPolicy.Recipients takes channel, sender, and IReadOnlyDictionary<long, VoicePoint>.

- [ ] **Step 1: Write failing policy tests**

~~~csharp
[Fact] public void AssemblerEmitsOnlyCompleteFrames() {
    var frames = new VoiceFrameAssembler();
    Assert.Empty(frames.Push(new short[319]));
    Assert.Single(frames.Push(new short[1]));
}
[Fact] public void VadUsesHangoverAfterLoudFrame() {
    var vad = new VoiceActivityDetector(0.1f, 2);
    Assert.True(vad.IsActive(LoudFrame()));
    Assert.True(vad.IsActive(SilentFrame()));
}
[Fact] public void ProximityExcludesSenderAndPeerAt36Metres() {
    var peers = new Dictionary<long, VoicePoint> { [1] = new(0, 0, 0), [2] = new(34, 0, 0), [3] = new(36, 0, 0) };
    Assert.Equal([2], VoiceRoutingPolicy.Recipients(VoiceChannel.Proximity, 1, peers));
}
~~~

- [ ] **Step 2: Verify RED**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~VoiceWireTests|FullyQualifiedName~VoiceFrameAssemblerTests|FullyQualifiedName~VoiceActivityDetectorTests|FullyQualifiedName~VoiceRoutingPolicyTests"

Expected: compile failures for the missing types.

- [ ] **Step 3: Implement the pure contracts**

~~~csharp
public static class VoiceWirePolicy {
    public const int FormatVersion = 1, SampleRate = 16000, FrameSamples = 320, MaxPayloadBytes = 512;
    public static bool IsValid(VoicePacket packet) => packet.FormatVersion == FormatVersion &&
        Enum.IsDefined(packet.Channel) && packet.Payload is { Length: > 0 and <= MaxPayloadBytes };
}
public sealed class VoiceFrameAssembler {
    public IReadOnlyList<short[]> Push(ReadOnlySpan<short> samples);
    public void Reset();
}
~~~

Retain partial samples, use RMS VAD with a 0..1 threshold and two-frame hangover, and use squared distance at 35 m. Add wrapping-sequence freshness and VoiceByteLimiter: max 50 frames and 30 KiB per peer/action/1,000 ms; expose Forget(peerId).

- [ ] **Step 4: Verify GREEN**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~Voice

Expected: PASS for malformed packets, VAD attack/release, sequence wrap, routing, and both limiter boundaries.

- [ ] **Step 5: Commit**

~~~bash
git add Scripts/Communication/VoiceWire.cs Scripts/Communication/VoiceFrameAssembler.cs Scripts/Communication/VoiceActivityDetector.cs Scripts/Communication/VoiceRoutingPolicy.cs MultiplayerTemplate.Tests/Voice*Tests.cs
git commit -m "feat: define voice transport policies"
~~~

### Task 2: Add managed Opus codec and jitter buffer

**Files:**
- Modify: MultiplayerTemplate.csproj
- Create: Scripts/Communication/OpusVoiceCodec.cs, VoiceJitterBuffer.cs
- Create: MultiplayerTemplate.Tests/OpusVoiceCodecTests.cs, VoiceJitterBufferTests.cs

**Interfaces:**
- Consume Task 1 wire constants and packet.
- Produce IVoiceCodec.Encode(short[] frame), IVoiceCodec.Decode(byte[]? payload, bool packetLoss), and VoiceJitterBuffer.Enqueue, TryTake, Reset.

- [ ] **Step 1: Write failing codec and jitter tests**

~~~csharp
[Fact] public void OpusRoundTripReturnsOne320SampleFrame() {
    var codec = new OpusVoiceCodec();
    Assert.Equal(320, codec.Decode(codec.Encode(SineFrame()), false).Length);
}
[Fact] public void GapRequestsPlcOnceBeforeNextPacket() { var queue = new VoiceJitterBuffer(); queue.Enqueue(Packet(10)); queue.Enqueue(Packet(12)); Assert.True(queue.TryTake(out _, out var plc)); Assert.False(plc); Assert.True(queue.TryTake(out _, out plc)); Assert.True(plc); }
[Fact] public void QueueKeepsAtMostTenFrames() { var queue = new VoiceJitterBuffer(); for (uint i = 0; i < 11; i++) queue.Enqueue(Packet(i)); Assert.Equal(10, queue.Count); }
~~~

- [ ] **Step 2: Verify RED**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~OpusVoiceCodecTests|FullyQualifiedName~VoiceJitterBufferTests"

Expected: missing codec/buffer failure.

- [ ] **Step 3: Implement**

Add this main-project dependency:

~~~xml
<PackageReference Include="Concentus" Version="2.2.2" />
~~~

Wrap Concentus at 16 kHz mono, reject output above 512 bytes, and isolate codec exceptions as failed frames with no logging. Start jitter playback at three queued frames, cap it at ten, and request one PLC decode for a missing sequence before consuming a later packet.

- [ ] **Step 4: Verify GREEN**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter "FullyQualifiedName~OpusVoiceCodecTests|FullyQualifiedName~VoiceJitterBufferTests"

Expected: PASS, including corrupt-payload and payload-limit coverage.

- [ ] **Step 5: Commit**

~~~bash
git add MultiplayerTemplate.csproj Scripts/Communication/OpusVoiceCodec.cs Scripts/Communication/VoiceJitterBuffer.cs MultiplayerTemplate.Tests/OpusVoiceCodecTests.cs MultiplayerTemplate.Tests/VoiceJitterBufferTests.cs
git commit -m "feat: add managed opus voice codec"
~~~

### Task 3: Persist voice preferences and create Voice bus

**Files:**
- Modify: addons/SettingsManager/SettingsManager.cs, Scripts/Communication/VoicePolicy.cs
- Create: MultiplayerTemplate.Tests/VoiceSettingsPolicyTests.cs

**Interfaces:** Produce VoiceTransmissionMode { PushToTalk, VoiceActivity }; SettingsManager get/set APIs for input device, mic gain, mode, VAD threshold, outgoing VoiceChannel, and overall voice volume.

- [ ] **Step 1: Write failing policy tests**

~~~csharp
[Theory] [InlineData(-1f, 0f)] [InlineData(2f, 1f)]
public void VadThresholdIsClamped(float input, float expected) =>
    Assert.Equal(expected, VoiceSettingsPolicy.ClampVadThreshold(input));
[Fact] public void DefaultsArePttAndProximity() { Assert.Equal(VoiceTransmissionMode.PushToTalk, VoiceSettingsPolicy.DefaultTransmissionMode); Assert.Equal(VoiceChannel.Proximity, VoiceSettingsPolicy.DefaultChannel); }
~~~

- [ ] **Step 2: Verify RED**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~VoiceSettingsPolicyTests

Expected: missing VoiceSettingsPolicy.

- [ ] **Step 3: Implement**

Add pure defaults/clamps to VoicePolicy.cs. Add voice_input_device, voice_gain, voice_transmission_mode, voice_vad_threshold, voice_channel, and voice_volume to SettingsManager's audio config. Create a Voice bus sent to Master during existing audio setup; only SetVoiceVolume changes it. Invalid stored values load as Default, PTT, and proximity.

- [ ] **Step 4: Verify GREEN**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~VoiceSettingsPolicyTests && godot --headless --path . --quit-after 3

Expected: PASS and no audio/settings errors.

- [ ] **Step 5: Commit**

~~~bash
git add addons/SettingsManager/SettingsManager.cs Scripts/Communication/VoicePolicy.cs MultiplayerTemplate.Tests/VoiceSettingsPolicyTests.cs
git commit -m "feat: persist voice preferences"
~~~

### Task 4: Replace client PCM pipeline with Opus playback

**Files:**
- Modify: Scripts/Communication/PositionalVoiceChat.cs, MultiplayerTemplate.Tests/VoiceResamplerTests.cs

**Interfaces:** Consume Tasks 1–3; preserve ClientCaptureEnabled, IsSpeaking, GetPeerVolume, SetPeerVolume; produce StartMicTest(), StopMicTest(), and read-only VoiceCaptureMeter.

- [ ] **Step 1: Add failing frame-boundary test**

~~~csharp
[Fact] public void FractionalRateBuffersStillEmitExactVoiceFrames() {
    var source = new VoiceResampler(44100, 16000);
    var frames = new VoiceFrameAssembler();
    Assert.All(frames.Push(source.Process(new float[960])), frame => Assert.Equal(320, frame.Length));
}
~~~

- [ ] **Step 2: Verify RED**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~VoiceResamplerTests

- [ ] **Step 3: Implement local behavior**

Replace PCM packing with resample → frame assembly → PTT/VAD → OpusVoiceCodec.Encode. Capture starts only for a match or explicit local test; sending is only Playing and never GameplaySuppressed. Add spatial/global speaker maps keyed by peer/channel, jitter/PLC decoding, Voice-bus output, and existing peer gain/mute. Clear matching state on match end, disconnect, peer loss, device switch, and mic-test stop. Do not change RPC signatures.

- [ ] **Step 4: Verify GREEN**

Run: dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj --filter FullyQualifiedName~Voice && bash Tests/run_multiplayer_smoke.sh

Expected: unit tests pass and dedicated capture remains disabled.

- [ ] **Step 5: Commit**

~~~bash
git add Scripts/Communication/PositionalVoiceChat.cs MultiplayerTemplate.Tests/VoiceResamplerTests.cs
git commit -m "feat: add opus voice client pipeline"
~~~

### Task 5: Implement protocol-10 authoritative routing

**Files:**
- Modify: Scripts/Communication/PositionalVoiceChat.cs, Scripts/Network/NetworkBuildInfo.cs, Scripts/Game/GameWorld.cs, Tests/NetworkSmoke.cs

**Interfaces:** Replace submit/receive RPCs with primitive (int formatVersion, int channel, uint sequence, byte[] payload); consume Task 1 routing and limiter.

- [ ] **Step 1: Write failing multiprocess smoke proof**

Add ClientA/ClientB probes: proximity at 36 m, proximity inside 35 m, then global. Latch receive counts by channel and snapshot sender roster before wrong-version, oversized, and 51-frame burst submissions.

- [ ] **Step 2: Verify RED**

Run: bash Tests/run_multiplayer_smoke.sh

Expected: new assertions fail under old global PCM relay.

- [ ] **Step 3: Implement server relay**

Use RequireServer, RequireKnownSender, RequireState(Playing), VoiceWirePolicy.IsValid, then the byte limiter. Reject budget overflow through RpcSecurity.Reject; silently drop invalid format or absent world/body. Convert server NetworkPlayer.GlobalPosition to VoicePoint and RpcId each policy-selected recipient. Receive RPC includes authoritative sender ID plus packet primitives; clients never relay. Bump 9→10 once.

- [ ] **Step 4: Verify GREEN**

Run: bash Tests/run_multiplayer_smoke.sh

Expected: proximity exclusion/delivery, global delivery, invalid/burst state preservation, and incompatible-protocol rejection pass.

- [ ] **Step 5: Commit**

~~~bash
git add Scripts/Communication/PositionalVoiceChat.cs Scripts/Network/NetworkBuildInfo.cs Scripts/Game/GameWorld.cs Tests/NetworkSmoke.cs
git commit -m "feat: route authoritative opus voice"
~~~

### Task 6: Build voice settings UI and mic-test surface

**Files:**
- Modify: Scripts/UI/Main.cs, Scripts/Communication/PositionalVoiceChat.cs, Tests/NetworkSmoke.cs

**Interfaces:** Consume Task 3 settings and Task 4 mic-test API. Produce controls named VoiceInputDevice, VoiceGain, VoiceTransmissionMode, VoiceVadThreshold, VoiceChannel, VoiceVolume, VoiceMicTest.

- [ ] **Step 1: Write failing engine smoke check**

Find every named Audio-tab control, select VAD and Global, start mic test, assert Options keeps the gameplay blocker, close Options, and assert mic test stopped without changing outgoing packet count.

- [ ] **Step 2: Verify RED**

Run: bash Tests/run_multiplayer_smoke.sh

Expected: controls absent.

- [ ] **Step 3: Implement**

Use existing SliderRow, OptionButton, and CheckButton patterns in Main.BuildOptions. Populate devices from AudioServer.GetInputDeviceList plus Default and bind to Task 3. Mic test is Task 4 local-only; ShowOptions(false) stops it. Add no input action.

- [ ] **Step 4: Verify GREEN**

Run: bash Tests/run_multiplayer_smoke.sh

Expected: controls persist local intent, mic test is local-only, and Options close clears it.

- [ ] **Step 5: Commit**

~~~bash
git add Scripts/UI/Main.cs Scripts/Communication/PositionalVoiceChat.cs Tests/NetworkSmoke.cs
git commit -m "feat: add voice audio settings"
~~~

### Task 7: Document and prove P3

**Files:**
- Modify: docs/EXTENDING.md, docs/TESTING.md, docs/HANDOFF.md, docs/superpowers/plans/2026-09-07-voice-overhaul.md

- [ ] **Step 1: Document final limits and ownership**

Document protocol 10, 16 kHz/320-sample/20-ms frames, 512-byte payload, 50-frame/30-KiB budgets, 35-m radius, PTT/VAD privacy, the Concentus-only exception, and server/client/headless boundaries. Mark P3 shipped only after Step 3 passes.

- [ ] **Step 2: Confirm smoke has explicit markers**

Confirm PASS markers cover runtime Concentus loading, proximity exclusion/delivery, global delivery, abuse rejection, cleanup, and dedicated routing/client-only split.

- [ ] **Step 3: Run final proof**

Run: bash -c 'cd -P /data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate; exec bash Tests/run_all_tests.sh'

Expected: all unit tests, every multiplayer scenario, coverage, and MULTIPLAYER HARNESS PASS.

- [ ] **Step 4: Check diff**

Run: git diff --check HEAD~1..HEAD

Expected: no whitespace errors.

- [ ] **Step 5: Commit**

~~~bash
git add docs/EXTENDING.md docs/TESTING.md docs/HANDOFF.md docs/superpowers/plans/2026-09-07-voice-overhaul.md
git commit -m "docs: document voice overhaul"
~~~
