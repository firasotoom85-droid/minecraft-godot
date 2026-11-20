# Engine audit — 20 November 2026

## Scope

Static source review focused on first-party runtime systems and their tests:
world persistence and migration, voxel mutation and worker snapshots, chunk and
far-distance meshing, player survival state, inventory operations, game
systems, automated tests, and GitHub Actions. The bundled addons/godot_ai
editor integration was not audited line-by-line.

The existing main-branch CI run was green before this change. The historical
performance baseline in docs/PERF_BASELINE.md is a single Debug-build laptop
snapshot, not a repeatable benchmark.

## Changes in this audit branch

- Keep the world dirty until the world save, player save, and manifest have
  all reported successful writes. Failed writes are logged and remain eligible
  for the next autosave retry.
- Flush temporary save files, check write and rename errors, preserve the
  current save during backup rotation, and restore it if the final rename fails.
- Write manifests through the same atomic/backup path as world and player data.
- Remove partial world copies after a copy or manifest-write failure; do not
  archive a legacy save until its imported manifest has been written.
- Take the mesher snapshot lock before changing the source chunk byte array so
  mesh payloads cannot observe an edit outside the snapshot/epoch critical section.
- Mark world and far-LOD shutdown flags volatile because worker threads read them.
- Preserve zero hunger as a valid saved state while treating a missing hunger
  field in older player saves as the legacy default.
- Reset partial drowning time when the player surfaces or leaves survival mode.
- Reject negative inventory-consumption requests and make zero-count requests
  explicit no-ops.
- Add regression coverage for these save, survival, and inventory edge cases.

## Follow-up improvements

These require separate profiling or design work and are intentionally not
mixed into this correctness patch:

1. **Repeatable performance harness:** add fixed-seed camera paths and record
   p50/p1 frame time, mesh build time, collision creation time, worker queue
   depth, managed memory, and VRAM for Low/High/Ultra presets on MX230 and
   RTX-class hardware.
2. **Chunk memory budget:** measure peak data-cache, mesh-payload, and collider
   memory during fast flight and large view distances; consider byte budgets
   and eviction that account for in-flight mesher snapshots.
3. **Streaming backpressure:** replace polling sleeps with a signal/semaphore or
   bounded work channel; profile whether worker groups spend measurable time
   waiting for main-thread payload application.
4. **Save transaction identity:** world, player, and manifest files are still
   independently committed. A future generation/transaction ID or a combined
   save manifest could make recovery across a power loss between those files
   fully coordinated.
5. **Content and UX:** complete swimming and water interaction, gamepad input
   rebinding, onboarding/localization, and world-management failure UI.
6. **Visual regression:** capture deterministic reference frames for biome
   transitions, cave lighting, water, greedy-vs-per-face meshing, and far-LOD
   seams before further renderer changes.

## Verification

The GitHub Actions workflow builds with .NET 9 and runs the Godot 4.7.2 Mono
headless test runner. This audit adds tests to that workflow; check the branch
CI result before merging.
