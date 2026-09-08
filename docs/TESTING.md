# Testing

The template uses two complementary test layers.

## Unit tests and managed coverage

`MultiplayerTemplate.Tests` is an xUnit project for deterministic policies and calculations.

Voice policy tests cover wire validation, 16-kHz framing, VAD, 35-m routing, sequence handling,
and the 50-frame/30-KiB sender limits. The multiplayer harness covers client/server lifecycle and
headless client-service suppression; dedicated servers route voice but create no capture, decode,
playback, or voice UI nodes.
It currently covers:

- RPC rate limiting and peer/action isolation
- RPC cooldown enforcement
- connection-quality averages, jitter, loss, thresholds, and report sanitization
- chat normalization, length limits, and BBCode escaping
- network clock offset, tick, countdown, and progress calculations
- Steam/LAN identity keys
- protocol and tick-rate invariants

Run it with coverage:

```bash
dotnet test MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj \
  --settings Tests/coverage.runsettings \
  --collect:"XPlat Code Coverage"
```

Coverlet writes Cobertura and JSON reports under `TestResults/`.

The full managed-assembly percentage is intentionally reported without exclusions for
untested game code. Godot callbacks and RPC methods exercised by separate engine processes do
not appear as covered in this number, so use it as a conservative unit-coverage baseline—not
as the sole quality signal.

## Godot multiplayer integration tests

`Tests/run_multiplayer_smoke.sh` launches independent headless Godot processes and verifies:

- ENet host plus two clients
- registration, ready-up, synchronized loading, and playing state
- incompatible protocol rejection
- clock synchronization and connection-quality sampling
- text-chat relay
- physics-object interaction replication
- host-loss recovery
- dedicated-server startup and client-service suppression

## Complete suite

Run both layers and print the measured managed coverage:

```bash
bash Tests/run_all_tests.sh
```

The script fails when any unit test, Godot process, smoke assertion, or logged Godot error
fails. Coverage is reported but not yet used as a repository-wide failure threshold; raising
the baseline should come from testing behavior rather than excluding engine-facing files.
