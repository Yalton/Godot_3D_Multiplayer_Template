#!/usr/bin/env bash
set -u

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
log_dir="$(mktemp -d)"
pids=()

cleanup() {
  for pid in "${pids[@]}"; do
    kill "$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT

godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=server >"$log_dir/server.log" 2>&1 &
pids+=("$!")
sleep 0.25

godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=incompatible >"$log_dir/incompatible.log" 2>&1 &
pids+=("$!")
sleep 0.35

godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
  --smoke-role=client --smoke-name=ClientA >"$log_dir/client_a.log" 2>&1 &
pids+=("$!")
sleep 0.4

godot --headless --path "$project_dir" tests/network_smoke.tscn -- \
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
  echo "MULTIPLAYER HARNESS PASS"
else
  echo "MULTIPLAYER HARNESS FAIL — logs retained at $log_dir"
fi
exit "$result"
