#!/usr/bin/env bash
# Build the game and run the headless content/save/gameplay tests.
# Override the editor path with GODOT=/path/to/godot.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -z "${GODOT:-}" ]]; then
    if command -v godot >/dev/null 2>&1; then
        GODOT="$(command -v godot)"
    else
        for candidate in \
            "$HOME/Desktop/anki-linux/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" \
            "$HOME/Desktop/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" \
            "$HOME/Downloads/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" \
            "$HOME/.local/bin/godot" \
            "/usr/local/bin/godot" \
            "/usr/bin/godot"; do
            if [[ -x "$candidate" ]]; then
                GODOT="$candidate"
                break
            fi
        done
    fi
fi

if [[ -z "${GODOT:-}" || ! -x "$GODOT" ]]; then
    echo "verify: Godot mono binary not found." >&2
    echo "verify: set GODOT=/path/to/Godot_v4.x-stable_mono_linux.x86_64" >&2
    echo "verify: or install the .NET editor and put 'godot' on PATH." >&2
    exit 2
fi

TEST_TIMEOUT="${GODOT_TEST_TIMEOUT:-300}"
if [[ ! "$TEST_TIMEOUT" =~ ^[1-9][0-9]*$ ]]; then
    echo "verify: GODOT_TEST_TIMEOUT must be a positive integer number of seconds." >&2
    exit 2
fi
if ! command -v timeout >/dev/null 2>&1; then
    echo "verify: the 'timeout' utility is required to bound a failed headless run." >&2
    exit 2
fi

dotnet build -c Debug
timeout --signal=TERM --kill-after=15 "$TEST_TIMEOUT" \
    "$GODOT" --headless --path . res://tests/TestRunner.tscn
