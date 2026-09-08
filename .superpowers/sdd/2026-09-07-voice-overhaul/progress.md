# SDD ledger — plan: docs/superpowers/plans/2026-09-07-voice-overhaul.md

## Baseline

Physical-path `Tests/run_all_tests.sh` at 7daab92: 128 unit tests passing; 13 multiplayer scenarios passing; `MULTIPLAYER HARNESS PASS`.

## Pre-flight interface scan

| Tasks | Shared interface/files | Finding |
| --- | --- | --- |
| 1 → 2 | `VoicePacket`, `VoiceWirePolicy.FrameSamples` | Task 1 supplies the fixed 16 kHz/320-sample wire contract; Task 2 consumes it for codec and jitter. Consistent. |
| 1 → 4/5 | VAD, frame assembler, routing, budget | Task 4 owns local production; Task 5 owns server validation/routing. Both consume Task 1 without redefining wire values. Consistent. |
| 2 → 4 | `IVoiceCodec`, `VoiceJitterBuffer` | Task 2 supplies the managed codec boundary and bounded queue; Task 4 owns their Godot lifecycle. Consistent. |
| 3 → 4/6 | SettingsManager voice APIs | Task 3 owns persistence/defaults and Voice bus; Task 4 applies them to runtime; Task 6 binds UI. Consistent. |
| 4 → 5/6 | `PositionalVoiceChat` public mic-test/runtime API | Task 4 adds only local APIs; Task 5 changes RPCs; Task 6 binds mic test. Consistent. |
| 5 → 7 | Protocol 10 and smoke markers | Task 5 makes final network contract; Task 7 documents and proves it. Consistent. |

Ruling: The plan's initial internal-worktree default conflicts with the project handoff's explicit Godot duplication warning. Use sibling worktree `/data/fast/Projects/Tech/Games/Godot/3DMultiplayerTemplate-p3-voice-overhaul` instead — it preserves isolation without project scanning risk — cost if wrong: only local disk use.

Ruling: The plan requires a managed Opus dependency and the approved spec explicitly authorizes the single Concentus 2.2.2 exception. Pin it only in the main project and do not add native acceleration — cost if wrong: pure C# performance may need later measurement, but packaging remains portable.

Task 1: fix round 1/5 (overflow-safe frame/byte arithmetic and VAD/window boundary proof; byte-overflow proof still open; commits 900d511..6b1ebb4)
Task 1: fix round 2/5 (byte-overflow proof addressed; commits 6b1ebb4..794d0e5)
Task 1: complete (commits 7daab92..794d0e5, review clean)

Task 2: fix round 1/5 (reordered jitter startup selected first arrival; corrected to wrap-aware oldest and added regression proof; commits e3d2ae3..cda6f17)
Task 2: complete (commits 794d0e5..cda6f17, review clean)

Task 3: complete (persisted voice preferences, portable VoiceSettingsPolicy, and Master-routed Voice bus; scoped review/fix/re-review clean). Deferred integration note: Task 4 owns removing PositionalVoiceChat's current input-device reset so it applies SettingsManager's persisted device.

Task 4: complete (Opus capture/playback pipeline, PTT/VAD, Voice bus, mic-test/meter, bounded receive state; privacy and headless review fixes re-reviewed clean). Task 5 owns the primitive packet RPC, channel-aware playback, and authoritative routing.
