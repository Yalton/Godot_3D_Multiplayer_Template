# Stretch Features

Potential cross-game systems deliberately left outside the current core template. These are
ordered roughly by foundation value, not implementation urgency.

## Lobby settings framework

Skipped for now by design.

- Replicated map and game-mode selection
- Player limits
- Join-in-progress policy
- Extensible game-specific fields
- Host-only mutation with server validation

Steam lobby privacy is already implemented as part of the social flow; it does not depend on
a generalized match-settings framework.

## Disconnect and reconnect framework

- Reconnect grace period
- Reclaim a prior player slot and identity
- Configurable mid-match disconnect policy
- Host-loss recovery screen
- Explicit no-host-migration policy initially

## Dedicated server support

Core implementation complete. Headless startup skips client UI, console UI, microphone
capture, and chat UI. `server.cfg` plus command-line overrides cover portable configuration.

- Headless startup without constructing gameplay UI
- Server configuration file
- Server name, port, initial map, and player limit
- Graceful shutdown and signal handling
- Remote or local console administration
- Server-specific structured logging

## Network clock

Implemented in `Scripts/Network/NetworkClock.cs`.

- Synchronized server time
- Authoritative simulation tick
- Countdown and deadline helpers
- Latency-aware event scheduling
- Round and match timer primitives

## Connection-quality monitoring

Implemented in `Scripts/Network/ConnectionQualityMonitor.cs` and the lobby UI.

- Round-trip ping
- Jitter
- Packet-loss estimate
- Timeout/degraded-connection state
- Debug overlay
- Lobby connection-quality indicators

## RPC security helpers

Implemented in `Scripts/Network/RpcSecurity.cs` and applied to core request RPCs.

- Sender and owner validation
- Session-state gating
- Per-RPC rate limiting
- Distance and line-of-sight validation helpers
- Cooldown enforcement
- Consistent rejection logging

## Text chat framework

- Lobby, match, team, and system channels
- Message length and rate limits
- Integration with the existing persistent mute/block profile
- Safe rich-text handling
- Steam and LAN identity display

## Multiplayer pause policy

Implemented by `Scripts/UI/LocalInputContext.cs`. Modal UI suppresses local gameplay input
without pausing network simulation.

- Local escape menu without pausing network simulation
- Optional host-controlled true pause
- Input suppression while menus, rebinding, or the developer console are active
- Clear pause behavior for dedicated servers

## Local profile abstraction

- Preferred display name for LAN sessions
- Identity and accessibility preferences
- First-run and onboarding flags
- Portable profile versioning and migration
- No genre-specific progression in the core

The persistent social mute/block state already lives in `PlayerIdentityService`; a broader
profile service could absorb it later.

## Expanded testing and network simulation

Core lifecycle coverage now includes incompatible builds, synchronized loading, clock sync,
host loss, headless startup, and deterministic security-helper checks. Transport simulation
and Steam relay/invite automation remain environment-specific follow-up work.

- Late join during `Loading`
- Disconnect during `Loading`
- Host disconnect
- Reconnect and slot reclamation
- Simulated latency, jitter, duplication, reordering, and packet loss
- Dedicated-server test
- Three-peer Steam relay test
- Steam invite cold-launch test
- Privacy/browser visibility test

## Optional modules, not core

These may be useful in many projects but are too large or genre-dependent to make mandatory:

- Host migration
- Mod and Workshop integration
- Cloud save
- Achievements and platform statistics
- Replay and spectator recording

## Friendslop phases

A five-phase expansion adding social-expression features on top of the core template: visible
characters, emotes, cosmetic identity, a richer voice layer, and in-match social controls.
Each phase ships standalone and gets its own spec, plan, and implementation cycle under
`docs/superpowers/specs/`.

- P1 (implemented): avatar and presence. Rigged character, networked animation state,
  third-person camera, Tab player list, speaking indicator, AFK detection, host kick.
- P2a (implemented): expression. Held emote wheel (B) and held quick-chat wheel (Z), both
  server-validated against an id-keyed catalogue.
- P2b (next): identity. Appearance, a cosmetic catalogue, a customization screen, and profile
  persistence. See `docs/superpowers/specs/` for the phase spec.
- P3 (planned): voice overhaul. Opus codec, voice activity detection, per-player volume mixer,
  proximity/global channels, microphone settings UI.
- P4 (planned): social physics. Player push and ragdoll, carrying players, prop spawner.
- P5 (planned): world toys and polish. Ping markers, spray tags, boombox, photo mode, Steam
  rich presence and party.
