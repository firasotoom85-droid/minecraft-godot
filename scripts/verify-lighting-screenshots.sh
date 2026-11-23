#!/usr/bin/env bash
# Build and capture all fixed lighting scenes. The capture runner compares each
# output PNG to its committed reference and exits non-zero on severe dark regions.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

# Prefer the project's provisioned .NET SDK when present (global.json pins 9.x).
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$PATH"
fi

if [[ -z "${GODOT:-}" ]]; then
    if command -v godot >/dev/null 2>&1; then
        GODOT="$(command -v godot)"
    else
        for candidate in \
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
    echo "lighting screenshots: Godot 4.7.2 .NET binary not found; set GODOT=/path/to/Godot." >&2
    exit 2
fi
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    echo "lighting screenshots: a desktop display is required for GPU image validation." >&2
    exit 2
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "lighting screenshots: .NET SDK not found; install the SDK from global.json." >&2
    exit 2
fi
if ! command -v timeout >/dev/null 2>&1; then
    echo "lighting screenshots: the timeout utility is required." >&2
    exit 2
fi

SEED="${SEED:-1337}"
PRESET="${PRESET:-LOW}"
CAPTURE_TIMEOUT="${LIGHTING_CAPTURE_TIMEOUT:-180}"
OUT_DIR="${OUT_DIR:-${TMPDIR:-/tmp}/minecraft-godot-lighting-screenshots}"
case "$PRESET" in
    LOW|BALANCED|HIGH|ULTRA) ;;
    *) echo "lighting screenshots: unsupported preset '$PRESET'." >&2; exit 2 ;;
esac

mkdir -p "$OUT_DIR"
dotnet build -c Debug

scenarios=(
    forest-canopy:ForestCanopyLighting
    torch-cave:TorchCaveLighting
    river-valley:RiverValleyLighting
    village-street:VillageStreetLighting
    dungeon-arena:DungeonArenaLighting
)

for entry in "${scenarios[@]}"; do
    scenario="${entry%%:*}"
    scene="${entry#*:}"
    output="$OUT_DIR/lighting-$scenario.png"
    echo "=== lighting screenshot: $scenario (seed=$SEED preset=$PRESET) ==="
    timeout --signal=TERM --kill-after=10 "$CAPTURE_TIMEOUT" \
        "$GODOT" --path "$ROOT_DIR" "res://tests/$scene.tscn" -- \
        "$SEED" "$output" "$PRESET"
    if [[ ! -s "$output" ]]; then
        echo "lighting screenshots: capture did not produce $output" >&2
        exit 1
    fi
done

echo "PASS: all five lighting screenshots passed their image-space audits."
echo "Artifacts: $OUT_DIR"
