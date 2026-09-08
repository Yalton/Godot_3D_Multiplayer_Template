#!/usr/bin/env bash
set -eu

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet build "$project_dir/MultiplayerTemplate.csproj" --nologo >/dev/null
log_dir="$(mktemp -d)"
pids=()

cleanup() {
  # ${pids[@]+...} guards the expansion: macOS bash 3.2 treats an empty array as
  # unset, which trips `set -u` inside the EXIT trap.
  for pid in ${pids[@]+"${pids[@]}"}; do
    kill "$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT

timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=server >"$log_dir/server.log" 2>&1 &
pids+=("$!")
sleep 0.5

timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=incompatible >"$log_dir/incompatible.log" 2>&1 &
pids+=("$!")
sleep 0.7

timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=client --smoke-name=ClientA >"$log_dir/client_a.log" 2>&1 &
pids+=("$!")
sleep 0.8

timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=client --smoke-name=ClientB >"$log_dir/client_b.log" 2>&1 &
pids+=("$!")

result=0
for pid in "${pids[@]}"; do
  wait "$pid" || result=1
done
pids=()

grep -h "SMOKE" "$log_dir"/*.log || true
if grep -E "ERROR:|SMOKE .* FAIL" "$log_dir"/*.log; then result=1; fi

if (( result == 0 )); then
  loss_dir="$(mktemp -d)"
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=host-loss-server >"$loss_dir/server.log" 2>&1 &
  loss_server=$!
  pids+=("$loss_server")
  sleep 0.3
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=host-loss-client >"$loss_dir/client.log" 2>&1 &
  loss_client=$!
  pids+=("$loss_client")
  wait "$loss_server" || result=1
  wait "$loss_client" || result=1
  grep -h "SMOKE" "$loss_dir"/*.log || true
  if grep -E "ERROR:|SMOKE .* FAIL" "$loss_dir"/*.log; then result=1; fi

  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --server --port=18912 --smoke-role=dedicated >"$loss_dir/dedicated.log" 2>&1 || result=1
  grep -h "SMOKE" "$loss_dir/dedicated.log" || true
  if grep -E "ERROR:|SMOKE .* FAIL" "$loss_dir/dedicated.log"; then result=1; fi

  kick_dir="$(mktemp -d)"
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-server >"$kick_dir/server.log" 2>&1 &
  kick_server=$!
  pids+=("$kick_server")
  sleep 0.3
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-target >"$kick_dir/target.log" 2>&1 &
  kick_target=$!
  pids+=("$kick_target")
  sleep 0.2
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=kick-rogue >"$kick_dir/rogue.log" 2>&1 &
  kick_rogue=$!
  pids+=("$kick_rogue")
  wait "$kick_server" || result=1
  wait "$kick_target" || result=1
  wait "$kick_rogue" || result=1
  grep -h "SMOKE" "$kick_dir"/*.log || true
  if grep -E "ERROR:|SMOKE .* FAIL" "$kick_dir"/*.log; then result=1; fi

  emote_dir="$(mktemp -d)"
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=emote-server >"$emote_dir/server.log" 2>&1 &
  emote_server=$!
  pids+=("$emote_server")
  sleep 0.3
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=emote-a >"$emote_dir/a.log" 2>&1 &
  emote_a=$!
  pids+=("$emote_a")
  sleep 0.3
  timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=emote-b >"$emote_dir/b.log" 2>&1 &
  emote_b=$!
  pids+=("$emote_b")
  # EmoteSitter requests its looping emote 0.4s after reaching Playing and holds it for
  # 5.5s before cancelling — this stagger lands emote-late's join comfortably inside
  # that window so it snapshots the sit in progress rather than racing the cancel.
  sleep 2.5
  SMOKE_SUPERVISED=1 timeout --kill-after=5s 45s godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
    --smoke-role=emote-late >"$emote_dir/late.log" 2>&1 &
  emote_late=$!
  pids+=("$emote_late")
  for _ in $(seq 1 100); do
    grep -q "SMOKE EMOTE LATE" "$emote_dir/late.log" && break
    sleep 0.1
  done
  if ! grep -q "SMOKE EMOTE LATE PASS" "$emote_dir/late.log"; then result=1; fi
  kill -9 "$emote_late" 2>/dev/null || true
  wait "$emote_late" 2>/dev/null || true
  emote_late=""
  wait "$emote_server" || result=1
  wait "$emote_a" || result=1
  wait "$emote_b" || result=1
  [[ -z "$emote_late" ]] || wait "$emote_late" || result=1
  grep -h "SMOKE" "$emote_dir"/*.log || true
  if grep -E "ERROR:|SMOKE .* FAIL" "$emote_dir"/*.log; then result=1; fi
fi

if (( result == 0 )); then
  echo "MULTIPLAYER HARNESS PASS"
else
  echo "MULTIPLAYER HARNESS FAIL — logs retained at $log_dir"
fi
exit "$result"
