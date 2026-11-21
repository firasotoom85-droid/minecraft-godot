# Reliability audit — 2026-11-21

Project-specific bug hunt performed on `integration/v1.0-full` while the
60-minute survival soak ran against the pre-audit runtime (`db4f026`). This is
an audit of the integrated build, not a claim that every feature branch has the
same fixes.

## Scope and method

- Read the voxel core, chunk streaming/meshing, lighting, fluids, shapes,
  inventory, saves/archives, survival, mobs, villages/quests, boats, UI, audio
  and world storage paths.
- Traced ownership and lifecycle across `VoxelWorld`, `Player`, `Mobs`,
  `VillageSystem`, `WorldStore` and the UI controllers.
- Reproduced every confirmed defect with an executable regression where the
  defect is reachable in the headless harness.
- Kept persisted `BlockId`/`ItemId` values, generator V1–V7 output and save
  compatibility untouched.
- Did not merge, rewrite or delete any feature branch.

## Confirmed and fixed

| ID | Severity | System | File | Defect and correction | Regression |
|---|---|---|---|---|---|
| RA-01 | High | Boats / respawn | `scripts/Player.cs:767-789`, `1586-1610` | Death while seated left the player parented under the boat with the body collider disabled, so respawn produced a dead passenger with no movement. `Respawn` now clears the rider, restores the pre-boat parent, re-enables collision and clears boat state before teleporting. | `tests/BoatBucketTests.cs` death-in-boat case |
| RA-02 | High | Mob death / loot | `scripts/Mob.cs:142-152` | Sunlight burn and dawn retreat called `Die` directly, and a queued-for-deletion mob could die again. Each path raised `Died`, loot and XP. Death is now idempotent and clamps health to zero. | `tests/MobContentTests.cs` `MobDeathProbe` |
| RA-03 | High | Inventory | `scripts/Inventory.cs:69-95` | `Add(ItemStack)` stored a multi-count non-stackable stack in one slot, hiding the real leftover and allowing malformed/transferred tools to duplicate durability-bearing items. It now fills one slot per item and returns the true leftover. | `tests/InventoryTests.cs` |
| RA-04 | High | Cooking / inventory | `scripts/Player.cs:1366-1397` | Cooking consumed raw food first; if the cooked result did not fit, the raw food was gone. `TryCook` now rolls the raw item back on a failed add, and `UseCampfire` warns instead of deleting it. | `tests/FishingTests.cs` campfire conservation cases |
| RA-05 | Medium | World storage | `scripts/Worlds.cs:221-241` | The prefix-only check accepted `user://worlds/../outside`, allowing deletion outside the worlds root. Empty, `.` and `..` segments are now rejected. | `tests/SaveTests.cs` outside-marker case |
| RA-06 | Medium | Save / villages | `scripts/VoxelWorld.cs:77`, `2583-2586`; `scripts/VillageSystem.cs:20-25`, `145-150` | `VillageSystem` copied live resident positions/trades into `world.Villagers` only every four seconds and on `_ExitTree`. Quit-to-menu saves before children exit, so the last trades could be written from stale state. `VoxelWorld.SaveNow` now raises `BeforeSave`; the village system flushes residents synchronously before serialization and unsubscribes on exit. | `tests/IntegrationTests.cs` live-resident save assertion |
| RA-07 | Low | Modal input | `scripts/DevConsole.cs:88`, `scripts/InventoryUi.cs:529`, `scripts/PauseMenu.cs:109` | Console/inventory/pause could open on top of another modal; closing the new panel unlocked input while the first panel stayed visible. Each now refuses to open while `Player.InputLocked` is set (unless it is already open). | Code-path review; no headless input harness |

All seven fixes are in the Debug assembly used for the final 4,217-check run.

## Confirmed but not fixed

None at the time of writing. RA-07's keybind-over-pause ordering remains a
narrow untested case described below.

## Suspected or unverified

These are not reported as bugs and no production change was made for them.

- **Villager saved position outside loaded chunks.** `VillageSystem` applies a
  saved resident transform before proving that the destination chunk is loaded
  or that the value is finite. The values originate from the project's own
  saves, not player input, so this needs a deliberately corrupted/legacy save
  before it is a defect rather than a robustness gap.
- **Pause vs. keybind Escape ordering.** `KeybindUi` handles `ui_cancel`, while
  `PauseMenu` also treats Escape as `pause`; if the pause controller receives
  the event first, the keybind panel could remain open while pause resumes.
  Escape ordering is engine/scene-order dependent and was not reproduced on a
  physical input path, so no speculative change was made.
- **Negative village-site caching.** Early negative results are not memoized;
  repeated `GetVillageSite` calls in empty cells re-sample placement noise. This
  is a measured performance opportunity, not an observed gameplay failure.
- **Physical controller, GPU rendering and archive adversarial inputs.** The
  headless suite exercises logic, not a physical pad, every GPU driver path or
  a hostile ZIP supplied by an attacker. Prior GPU captures and the archive
  suite passed; those surfaces remain less covered than the voxel core.

## System assessment

| Area | Status |
|---|---|
| Voxel coordinates/storage | No new defect; negative-coordinate and boundary tests pass. |
| Chunk streaming | No new defect; stale-result, unload and shutdown paths reviewed. |
| Greedy meshing / shapes | No new defect; shape-boundary and save tests pass. |
| Collision / raycasts | No new defect; partial shapes and unloaded chunks covered. |
| World generation | No new defect; V1–V8 golden hashes pass; V8 remains frozen. |
| Lighting | No new defect; incremental and boundary cases pass. |
| Fluids | No new defect; unloaded-neighbour and shape-boundary tests pass. |
| Inventory/crafting | RA-03 fixed; atomicity regressions added. |
| Saves/backups/archives | RA-05 and RA-06 fixed; traversal, corruption and ZIP suites pass. |
| Survival/combat | RA-04 fixed; no double-damage or failed-action durability defect confirmed. |
| Mobs/AI | RA-02 fixed; duplicate registration and freed-object paths reviewed. |
| Villages/quests | RA-06 fixed; deterministic generation, residents and quest rewards pass. |
| Boats/buckets | RA-01 fixed; stale-boat, freeing and fluid boundaries pass. |
| UI/input | RA-07 fixed; physical controller and keybind ordering remain manual. |
| Audio/visual | Prior playback/stream leaks remain fixed; no new leak observed. |
| Performance | No regression measured; see the 60-minute soak result below. |

## Verification

- Debug build: **0 warnings, 0 errors**.
- Release build (`minecraft.csproj -c Release`): **0 warnings, 0 errors**.
- Headless suite: **4,217 checks, 0 failures** across 27 suites.
- The suite includes 4,217 assertions, V1–V8 golden chunk hashes, legacy saves,
  pinned IDs, combined shape/villager/quest/boat save round-trip and the new
  regression cases above.
- No leaked ObjectDB instances or RIDs were reported by the final suite.
- 60-minute soak on `db4f026` (pre-audit runtime): **PASS** at 3,600 s with
  10 passive and 51 hostile creatures, 1,758 successful feeds, 48 cave
  encounters, 43,181 juvenile frame observations, 720 noise stimuli, 572
  investigations, 0 invalid positions, save/reload true and temporary-world
  cleanup true. No error, warning, leaked-object or leaked-RID lines appeared.
  RSS peaked at ~251 MB and settled to ~158 MB, with no monotonic growth.
  The soak therefore did not exercise the seven fixes above.
- Post-audit five-minute soak on the fixed assembly: **PASS** at 300 s with 10
  passive / 7 hostile creatures, 139 feeds, 4 cave encounters, 17,982 juvenile
  frame observations, 60 noise stimuli, 200 investigations, 0 invalid
  positions, save/reload true and temporary-world cleanup true. No runtime
  errors, warnings, leaked objects or leaked RIDs appeared.
- Final warmed 8-chunk worldgen sample: V6 4.30, V7 7.69 and V8 7.87 ms/chunk
  (**1.02×** V8/V7). A concurrent-load sample in the same audit measured 1.10×;
  absolute timings and the ratio move with host load, so both are recorded in
  `docs/PERF_BASELINE.md`.

## Remaining work

- Run a full 60-minute soak on the post-audit assembly before calling the
  reliability work complete for a release tag.
- Exercise keybind/pause Escape ordering with a physical keyboard and a
  corrupted-save villager position with a disposable world if those robustness
  gaps are to be closed.
