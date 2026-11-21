#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT_BIN="${GODOT:-godot}"
DURATION="${1:-3600}"

if [[ ! "$DURATION" =~ ^[0-9]+$ ]] || (( DURATION < 60 || DURATION > 86400 )); then
  echo "Soak duration must be an integer from 60 to 86400 seconds." >&2
  exit 2
fi
SOAK_TIMEOUT="${SOAK_TIMEOUT_SECONDS:-$((DURATION + 300))}"
if [[ ! "$SOAK_TIMEOUT" =~ ^[1-9][0-9]*$ ]]; then
  echo "SOAK_TIMEOUT_SECONDS must be a positive integer number of seconds." >&2
  exit 2
fi
if ! command -v "$GODOT_BIN" >/dev/null 2>&1; then
  echo "Godot 4.7.2 .NET was not found. Set GODOT=/path/to/Godot_v4.7.2_mono." >&2
  exit 2
fi
if ! command -v timeout >/dev/null 2>&1; then
  echo "The 'timeout' utility is required to bound the soak process." >&2
  exit 2
fi

dotnet build "$ROOT" -c Debug --nologo

# Headless mode is otherwise uncapped (max_fps=0). On Godot 4.7.2 this can
# overwhelm Jolt's fixed-size job pool during sustained scene/physics startup.
# A render cap preserves real-time delta without forcing fixed-FPS simulation.
exec timeout --signal=TERM --kill-after=15 "$SOAK_TIMEOUT" \
  "$GODOT_BIN" --headless --max-fps 60 --path "$ROOT" \
  res://tests/SurvivalSoakRunner.tscn -- --duration="$DURATION"
