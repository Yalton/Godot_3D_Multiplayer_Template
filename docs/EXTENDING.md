# Extending the template

## Replace the example game

Keep `Session` alive and replace `GameWorld.cs` / `NetworkPlayer.cs`. The stable session
surface is:

- `SessionManager.Instance.Players`: `{ peer_id: {name, ready} }`
- `HostEnet()`, `JoinEnet()`, `HostSteam()`, `JoinSteamLobby()`
- `SetReady()`, `StartMatch()`, and `DisconnectSession()`
- `RosterChanged`, `MatchStarted`, and `ConnectionStateChanged`

The server is always multiplayer peer `1`. Player nodes are named by peer ID and receive
authority before `_ready`, which makes ownership deterministic for late joiners.

## Add authoritative gameplay

Use this pattern:

1. A client sends an intent RPC with `@rpc("any_peer")`.
2. The server reads `multiplayer.get_remote_sender_id()`.
3. The server validates ownership, cooldown, distance, and current match phase.
4. The server mutates canonical state.
5. The server broadcasts the resulting state.

Do not trust a peer ID included in client payloads. Derive identity from the RPC sender.

## Shipping checklist

- Replace the Steam development App ID and unique lobby tag.
- Pin and test one GodotSteam version on every export platform.
- Test 3+ peers; two-peer tests do not expose relay failures.
- Add protocol/build compatibility to lobby data before public releases.
- Add NAT traversal or a relay service if direct internet ENet is required.
- Rate-limit gameplay, chat, and lobby RPCs.
- Decide reconnect and host migration policy. This template returns clients to the menu when
  the host leaves and intentionally does not attempt host migration.
- Run exported builds through Steam, not only editor instances.
