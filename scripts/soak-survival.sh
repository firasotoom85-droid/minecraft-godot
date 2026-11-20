#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT_BIN="${GODOT:-godot}"
DURATION="${1:-3600}"

if ! command -v "$GODOT_BIN" >/dev/null 2>&1; then
  echo "Godot 4.7.2 .NET was not found. Set GODOT=/path/to/Godot_v4.7.2_mono." >&2
  exit 2
fi

dotnet build "$ROOT" -c Debug --nologo

exec "$GODOT_BIN" --headless --path "$ROOT" \
  res://tests/SurvivalSoakRunner.tscn -- --duration="$DURATION"
