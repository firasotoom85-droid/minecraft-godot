#!/usr/bin/env bash
set -euo pipefail

: "${GH_REPOSITORY:?GH_REPOSITORY must be set (owner/repo)}"

create_label() {
  local name="$1"
  local color="$2"
  local description="$3"
  gh label create "$name" --repo "$GH_REPOSITORY" --color "$color" --description "$description" --force
}

create_label "type: bug" "d73a4a" "A reproducible defect."
create_label "type: feature" "a2eeef" "A proposed new capability."
create_label "type: enhancement" "84b6eb" "An improvement to existing behavior."
create_label "type: documentation" "0075ca" "Documentation-only work."

create_label "priority: critical" "b60205" "Release, security or data-loss blocker."
create_label "priority: high" "d93f0b" "High-impact defect or release risk."
create_label "priority: medium" "fbca04" "Important but non-blocking work."
create_label "priority: low" "cfd3d7" "Low-impact or optional work."

create_label "status: needs-triage" "ededed" "Needs reproduction, scope or ownership review."
create_label "status: confirmed" "c2e0c6" "Reproduced or confirmed by source evidence."
create_label "status: ready" "0e8a16" "Scoped and ready for implementation."
create_label "status: in-progress" "1d76db" "Implementation is underway."
create_label "status: blocked" "b60205" "Blocked by a dependency or unresolved decision."
create_label "status: needs-testing" "fbca04" "Implementation exists; required validation is outstanding."
create_label "status: needs-review" "5319e7" "Ready for code/release review."

create_label "area: worldgen" "c5def5" "Terrain, structures and generation versions."
create_label "area: streaming" "bfdadc" "Chunk streaming, scheduling, LOD and worker lifecycle."
create_label "area: meshing" "d4c5f9" "Mesh generation, materials and geometry."
create_label "area: persistence" "f9d0c4" "World saves, backups and import/export."
create_label "area: gameplay" "c2e0c6" "Player, mobs, survival and progression."
create_label "area: ui-input" "fef2c0" "Interface, settings and input."
create_label "area: audio-visual" "f9d0c4" "Audio, shaders, lighting and visual polish."
create_label "area: performance" "e99695" "Profiling, memory and performance."
create_label "area: tests-ci" "bfd4f2" "Tests, automation and build infrastructure."
create_label "area: docs" "cfd3d7" "Repository documentation and contributor experience."
