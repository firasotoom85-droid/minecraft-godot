# Integration report — 2026-11-21

This document records the merge of the outstanding feature branches into one
playable build. It is the technical companion to the progress notes in
`ROADMAP.md`.

## Branches

### Integrated

| Branch | PR | What it adds |
|---|---|---|
| `worldgen/v7-release-candidate` | #29 | Versioned WorldGen V7: 13 biomes, continents, smoothed rivers, mountain ridges, aquifers, magma, depth-aware ores, biome canopies, surface landmarks, underground chambers, golden-hash tests |
| `ui/pixel-art-redesign-ready` | #24 | Pixel-art interface pass, controller bindings and focus navigation, inventory drag-and-drop/stack-transfer rules, first-person held-item viewmodel, responsive panels |
| `feature/survival-equipment-progression` | #22 | Swords, bow + ballistic arrows, directional shields, XP/levels, Enchanting Table, enchantments, gear upgrades, Ember Blade, weapon durability and critical hits |

`fix/save-survival-hardening` (PR #18) was already merged into `main` before
this integration and is part of the base.

### Superseded, not merged

These branches were earlier iterations of the same three features. Their final
content is contained in the integrated branches above; merging them again would
reintroduce older implementations.

- `worldgen/v7-biome-expansion` (PR #20, closed)
- `worldgen/v7-validation` (PR #28, closed)
- `worldgen/v7-final` (PR #25, closed)
- `ui/pixel-art-redesign` (PR #21, closed)
- `ui/pixel-art-redesign-merge-ready` (PR #23, closed)
- `ui/pixel-art-redesign-current` (PR #27, closed 2026-11-21) — older snapshot:
  pre-refactor inventory transfer, no V7/fluid/mob test suites, no branch-only
  files.
- `ui/pixel-art-redesign-final` (PR #26, closed 2026-11-21) — older snapshot:
  removes FluidTests/FluidWorldTests/SurvivalSoakRunner and lacks the shared
  `InventoryStackTransfer` module.
- `redesign/ui-overhaul` (PR #19, draft, closed 2026-11-21) — an alternative
  visual direction for the same surfaces. It conflicts conceptually with the
  merged pixel-art pass and still carries the obsolete `BlockLights` OmniLight
  pool replaced by the voxel lighting engine. Its two functional fixes are
  already in main. The design notes remain on the branch for reference.

All branches were kept on GitHub after closing; nothing was deleted or
force-pushed.

## Conflicts resolved

| File | Conflict | Resolution |
|---|---|---|
| `scripts/BlockId.cs` | Both branches claimed id 38 | Flowing water keeps 38–44; `EnchantingTable` appended as 45. No persisted id moved. |
| `scripts/BlockDb.cs` | Same id collision in the definition table | Flowing water entries first, enchanting table last, matching the enum order. |
| `scripts/Player.cs` | Attack/place/hold paths rewritten on both sides | Kept main's DDA mining and target-cancel path; layered the bow fire path ahead of it. Right-click fires a held bow; left-click/hold fires with cadence. |
| `scripts/InventoryUi.cs` | Slot styling vs enchantment tooltip | Kept the shared `MenuSkin.SlotStyle` styling and the new hover state; merged name, description, durability/count and enchantment lines into one tooltip; removed the branch's local style helper. |
| `README.md`, `ROADMAP.md` | Parallel progress logs | Both histories kept; branch sections renumbered (15, 16). |

## Deliberate integration decisions

- **Mining guard.** `main` required a captured mouse before hold-to-mine. The
  controller pass makes mining possible without a captured mouse, and every
  modal UI sets `InputLocked`, so the `InputLocked` guard is retained without
  the mouse-mode requirement. This preserves controller mining and still blocks
  mining while any panel is open.
- **Armor.** Base armor points remain additive up to 80 %, and enchantment
  Protection applies multiplicatively afterwards (`ArmorReduction`), so both
  systems contribute without double-counting.
- **Experience.** `PlayerSave.Experience` and the three enchantment fields on
  `ItemStackSave` are optional JSON fields defaulting to zero. Old player and
  world files load unchanged.
- **Generator version.** New worlds use V7. Versions 1–6 keep their frozen code
  paths and golden chunk hashes; only newly generated worlds see V7 terrain.

## Verification performed

- `dotnet build -c Debug`: 0 warnings, 0 errors.
- `scripts/verify.sh` equivalent headless suite: **1,182 checks, 0 failures**
  across content, lighting, mining, inventory, containers, furnaces, armor,
  equipment, farming, mobs, achievements, dungeon, fishing, keybinds, recipes,
  stats, fluids, fluid-world, save and worldgen.
- Save/worldgen regressions: legacy v1 anchor hash, v3/v5/v6 golden hashes,
  save round-trips, backup rotation and corruption recovery all pass.
- Real-scene soak: 300 s headless run of `main.tscn` with herds, hostiles,
  cave/surface alternation, storms and a save; no script errors, 22 passive
  mobs, 4 hostiles at night, 8 cave encounters.
- Performance: 30.6 FPS average at 1280×720, HIGH/VD8 on the MX230, with stable
  p99 frame times. See `docs/PERF_BASELINE.md`.

## Not verified

- Rendered V7 terrain (biome blending, river banks, mountain silhouettes) was
  not visually reviewed on a GPU.
- No RTX 4060-class measurement.
- The soak's juvenile-breeding observation was not hit in the 300 s run; the
  breeding rules themselves are covered by unit tests.
- Controller hardware was not available; bindings and focus navigation are
  covered by code and keybind tests, not a physical pad.

## Known gaps

- Partial-height building blocks (slabs, stairs) do not exist in any branch.
  The mesher and collision path remain cube-only.
- The draft `redesign/ui-overhaul` direction is not merged (see above).


## Follow-up: survival progression audit — 2026-11-21

The independent survival/combat balance audit is documented in
[`SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md`](SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md).
It fixes combat wear/invulnerability edge cases, peaceful-mode hostile cleanup,
normal/hard damage consistency, lapis access and furnace time carry. Persisted
IDs, generator versions and save schemas are unchanged. Its final 60-minute
real-scene soak outcome is recorded in that report.
