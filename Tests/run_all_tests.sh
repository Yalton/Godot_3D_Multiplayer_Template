#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
results_dir="$project_dir/TestResults/$(date +%Y%m%d-%H%M%S)"

dotnet test "$project_dir/MultiplayerTemplate.Tests/MultiplayerTemplate.Tests.csproj" \
  --settings "$project_dir/Tests/coverage.runsettings" \
  --collect:"XPlat Code Coverage" \
  --results-directory "$results_dir" \
  --verbosity minimal

bash "$project_dir/Tests/run_multiplayer_smoke.sh"

coverage_file="$(find "$results_dir" -name coverage.cobertura.xml -print -quit)"
if [[ -n "$coverage_file" ]]; then
  coverage_line="$(sed -n '2p' "$coverage_file")"
  line_rate="$(sed -n 's/.*line-rate="\([^"]*\)".*/\1/p' <<<"$coverage_line")"
  branch_rate="$(sed -n 's/.*branch-rate="\([^"]*\)".*/\1/p' <<<"$coverage_line")"
  awk -v lines="$line_rate" -v branches="$branch_rate" \
    'BEGIN { printf "MANAGED COVERAGE: %.2f%% line, %.2f%% branch\n", lines * 100, branches * 100 }'
  echo "Coverage report: $coverage_file"
fi
