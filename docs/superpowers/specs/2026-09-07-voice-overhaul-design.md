# Voice Overhaul (Friendslop P3)

## Context

The current voice path captures microphone audio, downsamples it to 16 kHz PCM, and relays each
packet globally through peer 1. It has useful foundations—server relay, a dedicated voice
channel, per-peer gain persistence, and local speaking indicators—but raw PCM consumes too much
bandwidth, global fanout ignores proximity, and playback has no jitter or packet-loss strategy.

P3 replaces that transport with an Opus-based, server-authoritative voice system while preserving
the template's security and lifecycle rules. Push-to-talk remains the default. Voice activity
detection (VAD) is an opt-in hands-free mode, and each player selects whether their outgoing
voice is proximity or global in Audio settings.

## Goals

- Encode live voice as 16 kHz mono Opus frames with one portable managed dependency.
- Provide push-to-talk by default and an opt-in energy-based VAD mode.
- Give each player an Audio-settings selector for their outgoing proximity or global channel.
- Route proximity voice only to recipients within the server-owned hearing radius; route global
  voice to every other match peer.
- Play proximity voice spatially and global voice non-spatially, with overall voice gain and the
  existing per-peer volume controls.
- Smooth unordered/lost voice data with bounded per-speaker jitter buffering and Opus packet-loss
  concealment (PLC).
- Keep microphone preferences local and persistent, keep the relay authoritative, and retain
  headless dedicated-server support.

## Non-goals

- A native codec, P/Invoke layer, binary redistribution, addon, or Steam-only voice transport.
- Recording, moderation, transcription, encryption, voice chat rooms, teams, or party chat.
- Server-side audio decoding, speech classification, or storage.
- Splitting the existing social mute into separate text and voice mute data models.
- Arbitrary client-controlled hearing distance, target peers, positions, bitrates, or frame sizes.

## Dependency decision

P3 explicitly relaxes the prior no-NuGet rule for one dependency: add the managed-only
`Concentus` package, pinned to `2.2.2`. It is a portable C# Opus encoder/decoder compatible with
.NET 8. Do not add `Concentus.Native` or any other native companion package. This exception is
limited to the codec; no other NuGet packages or addons are introduced by P3.

## Voice wire contract

All voice is mono, signed 16-bit PCM at 16 kHz before encoding. The capture accumulator emits
exactly 320 samples per frame (20 ms); it does not rely on an engine capture-buffer size being a
valid codec frame. The local encoder produces one Opus payload per frame.

`VoicePacket` is an engine-free value contract with these fields:

- `FormatVersion = 1`;
- `Channel`: `Proximity` or `Global` only;
- a wrapping unsigned sequence number; and
- an Opus payload no larger than 512 bytes.

The client RPC carries only these primitive fields and the payload. Voice remains
`UnreliableOrdered` on ENet transfer channel 2. P3 bumps `NetworkBuildInfo.ProtocolVersion` once,
from 9 to 10, for this RPC contract. The frame duration, sample rate, and channel meanings are
fixed by this protocol version; a future format requires another explicit bump.

## Components and state ownership

`PositionalVoiceChat` remains the persistent Godot-facing autoload. It owns local microphone
capture, capture-bus setup, encoder lifetime, playback-node construction, and match/disconnect
lifecycle wiring. It must no-op its client-only work on a headless dedicated server.

New pure classes own framing, VAD, packet validation, unsigned sequence handling, rate/byte
budgeting, and routing policy. They may not reference the `Godot` namespace and receive ordinary
values only, so standalone xUnit can test them safely.

Codec work is isolated behind a small codec interface. The production implementation wraps
Concentus; tests may use deterministic fake codec results only at this boundary. The server does
not create decoders or playback state. Each graphical receiver owns a decoder and bounded jitter
state per remote peer and channel. These objects are cleared when the remote peer leaves, the
match ends, a session disconnects, or the local input device changes. Preferences have user
scope; capture/encoder state has local-player/match scope; remote decoder state has
remote-peer/match scope.

## Transmission, VAD, and privacy

The default transmission mode is push-to-talk using the existing rebindable action. In VAD mode,
a pure RMS-energy gate uses a persisted threshold plus a short hangover interval, preventing
speech from being clipped at syllable boundaries. It suppresses silent frames before Opus
encoding; P3 does not claim speech/noise classification beyond this energy gate.

Neither mode captures or sends while the session is outside `Playing` or
`LocalInputContext.GameplaySuppressed` is true. That makes chat, console, and options surfaces a
privacy mute even when VAD is selected. A local mic test is explicit, visibly active, uses no
network transport, and stops when its settings surface closes or the input device changes.

## Server validation and routing

`SubmitVoice` follows the repository RPC contract in this order:

1. Require server context; derive the sender exclusively from `GetRemoteSenderId()`.
2. Require a registered sender and `Playing` session state through `RpcSecurity`.
3. Validate format version, channel enum, sequence representation, and payload length before
   any relay.
4. Enforce both a 50-packet-per-second frame budget and a 30 KiB-per-second byte budget per
   sender. Repeated client-visible rejections use `RpcSecurity.Reject`, preserving its warning
   cooldown.
5. Resolve the sender's authoritative body and current positions from the server game world.

The server accepts no claimed peer ID, target list, position, distance, codec setting, or player
body path. For `Proximity`, it sends the untouched validated packet only to other active players
whose authoritative bodies are within the fixed 35 m hearing radius. For `Global`, it sends it to
every other active match peer. Missing bodies or unavailable routing state cause a silent drop;
they never cause a fallback global broadcast.

## Receive, jitter, and playback

Receivers reject self packets, muted or blocked peers, malformed packets, stale/duplicate
sequences, and packets for absent player bodies. These are local drops with no warning or
disconnect. A per-peer/channel jitter queue starts at a small target delay, is bounded to ten
20-ms frames, and drops excess late data rather than growing latency. A sequence gap produces one
PLC decode request before the next available frame. Decoder exceptions or invalid codec output
discard only that frame and reset the affected jitter state when recovery requires it.

Proximity packets feed an `AudioStreamPlayer3D` attached to the remote body and retain the 35 m
spatial hearing boundary. Global packets feed a non-spatial playback stream. Both use the same
per-peer volume/mute policy. A Voice bus provides an overall voice-volume control without
changing the existing Music, SFX, or Ambience controls. The speaking tracker continues to reflect
accepted local playback activity and expires naturally after its existing short decay.

## Audio settings and persistence

The existing Audio tab gains a Voice section containing input device, microphone gain,
transmission mode, VAD threshold, outgoing channel, overall voice volume, and the local mic-test
control/meter. Invalid or unavailable device names resolve to Godot's `Default` device. Switching
device, gain, or transmission mode clears capture/encoder state before the new configuration is
used.

These settings persist locally through the existing settings configuration approach. The outgoing
channel is an intent for packets a player transmits; it does not suppress proximity or global
packets received from other players. Existing stable-social-key per-peer gain persistence and
the player-list sliders remain the public per-peer volume API.

## Tests and verification

- Pure xUnit tests cover exact 320-sample framing, VAD threshold/attack/hangover behaviour,
  packet validation, sequence wraparound and staleness, byte/frame budgets, and recipient
  routing decisions.
- Codec tests cover valid round-trip encoding/decoding, fixed-frame sizes, bounded payloads, and
  PLC invocation without constructing Godot types.
- Engine smoke tests cover Opus relay through a listen server and dedicated server, proximity
  recipient exclusion/delivery, global delivery, invalid packets, oversized/burst submissions,
  no roster mutation on reject, match/disconnect cleanup, and graphical-client-only audio work.
- A smoke assertion proves Concentus loads inside the Godot runtime; unit tests alone are not
  sufficient for that deployment boundary.
- Run focused unit tests, the full unit suite, the multiplayer smoke harness, and the physical
  path form of `Tests/run_all_tests.sh`.

## Acceptance criteria

- On a graphical client, PTT produces intelligible Opus voice; VAD is opt-in and suppresses
  silence while preserving speech onset/release.
- A player can choose outgoing proximity or global voice independently in Audio settings.
- Proximity voice reaches only authoritative nearby peers and is spatial; global voice reaches
  all other match peers and is non-spatial.
- Malformed, wrong-version, oversized, over-budget, stale, duplicate, or out-of-state packets
  are not relayed and do not mutate session state or create repeated warning spam.
- Packet loss and jitter do not grow playback latency without bound; a missing frame uses PLC.
- Overall and per-peer voice gain work for both channels and survive their intended lifetimes.
- Dedicated servers route validated voice but create no microphone, decoder, playback, or UI
  nodes.
- The only new dependency is managed `Concentus` 2.2.2; no native companion, addon, or binary
  asset is added.
