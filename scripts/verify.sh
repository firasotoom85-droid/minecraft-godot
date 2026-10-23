#!/usr/bin/env bash
# Build the game and run the headless content/save/gameplay tests.
# Override the editor path with GODOT=/path/to/godot.
set -euo pipefail
cd "$(dirname "$0")/.."

GODOT="${GODOT:-$HOME/Desktop/anki-linux/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64}"
if [[ ! -x "$GODOT" ]]; then
    echo "verify: Godot mono binary not found at $GODOT" >&2
    echo "verify: set GODOT=/path/to/Godot_v4.x-stable_mono_linux.x86_64" >&2
    exit 2
fi

dotnet build -c Debug
"$GODOT" --headless --path . res://tests/TestRunner.tscn
