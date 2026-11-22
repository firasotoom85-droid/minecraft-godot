# Player-Journey Audit — Repair Candidates

**Repository:** https://github.com/firasotoom85-droid/minecraft-godot  
**Branch:** `main`  
**Reviewed commit:** `55d79087af6a0caade435f18ae7a6a60d66bfdca`  
**Review date:** 2026-11-21  
**Scope:** First-time Survival player journey, from world creation through the first night, mining, crafting, exploration, village progression, and resource retention.

## Review limitations

This is a source-code review of the stated commit. It was not a hands-on playthrough, and no Godot build, automated test, or runtime reproduction was executed during this review. Items below are therefore separated into confirmed code-path defects and design/UX decisions that should be validated before implementation. No files in the repository were changed.

---

## Summary

| ID | Priority | Finding | Classification |
|---|---|---|---|
| PJ-01 | P1 | Survival starts with a full hotbar of building blocks | Design/progression conflict |
| PJ-02 | P1 | Recipe-book crafting bypasses the crafting-table station | Progression rule gap; confirm intended design |
| PJ-03 | P1 | First Night tutorial does not teach the complete torch-material path | Onboarding gap |
| PJ-04 | P1 | Crafting-table tutorial step can miss a quick craft-and-place sequence | Confirmed event-timing defect |
| PJ-05 | P1 | Merchant biome quest ignores discoveries made before acceptance | Confirmed quest-state defect |
| PJ-06 | P1 | Mining Raw Iron does not report the Gather event used by the Blacksmith quest | Confirmed quest wiring defect |
| PJ-07 | P1 | Village-generated wheat is not registered with the crop-growth registry | Confirmed generation/system integration gap |
| PJ-08 | P1 | Inventory overflow can destroy uncollected mining and mob rewards | Confirmed reward-retention defect; overlaps earlier reliability audit |
| PJ-09 | P2 | Wrong-tier mining removes ore without a clear harvest explanation | UX improvement candidate |
| PJ-10 | P2 | Difficulty is global and defaults to Peaceful, while some progression assumes hostile enemies | Design/configuration inconsistency |
| PJ-11 | P2 | New worlds begin partway through daytime, leaving less than four real minutes before night | Pacing concern to validate in playtest |

---

## Detailed findings

### PJ-01 — Survival starts with a full hotbar of building blocks

**Priority:** P1  
**Classification:** Design/progression conflict — requires a design decision, not an automatic code fix.

**Player journey:** Create a fresh Survival world and inspect the hotbar. The initial inventory contains 64 each of Grass, Dirt, Stone, Sand, Log, Planks, Cobblestone, Glass, and Leaves. All nine hotbar slots are occupied.

**Player impact:**
- The opening task asks the player to gather wood although they already have a full stack of logs.
- The tutorial later asks them to mine stone although they already have a full stack of cobblestone.
- Early crafted tools are likely to enter the backpack rather than the hotbar, adding inventory-management friction before the player understands the controls.
- The initial experience feels closer to a building sandbox than a resource-gathering Survival start.

**Relevant code:**
- `scripts/ItemDb.cs:260` — `StarterKit()` returns the nine 64-item building stacks.
- `scripts/Inventory.cs` — `LoadFrom(null)` applies the starter kit when no saved inventory exists.
- `scripts/OnboardingSystem.cs:11-33` — first tasks teach gathering wood and mining stone.

**Repair direction:** Define a deliberate Survival starter kit with a small number of useful supplies and free hotbar slots. Keep the large construction kit for Creative or make it an explicit world-creation option. Preserve legacy-save behavior; do not silently change how old saves with missing inventory data are migrated.

**Regression test:** Create a new Survival world and assert the intended starting items, stack counts, and number of free hotbar slots. Separately verify legacy saves still receive their compatibility defaults.

### PJ-02 — Recipe-book crafting bypasses the crafting-table station

**Priority:** P1  
**Classification:** Progression rule gap — confirm whether unrestricted recipe-book crafting is intentional.

**Player journey:** Open the inventory, select a recipe-book entry, and craft a recipe that normally uses a 3×3 pattern, such as a Furnace, Chest, armor piece, or Enchanting Table.

**Player impact:** The recipe-book route can craft directly from inventory ingredients without using the crafting grid or requiring the crafting-table station. This weakens the progression milestone of making and placing a crafting table. It also makes the visible 2×2 versus 3×3 grid distinction misleading.

**Relevant code:**
- `scripts/InventoryUi.cs:468-499` — recipe-book UI is available alongside the inventory crafting grid.
- `scripts/InventoryUi.cs:957-977` — `CraftFromBook()` crafts directly from the inventory.
- `scripts/Recipes.cs:263-275` — `Craft()` checks ingredients and output capacity, but has no station/grid-size requirement.

**Repair direction:** If 3×3 recipes are intended to require a crafting table, add recipe station/size metadata and enforce it in both the recipe book and grid paths. Do not infer requirements from recipe names. If the bypass is intentional for accessibility or convenience, document it and adjust tutorial/progression expectations instead.

**Regression test:** Verify a 2×2-only recipe is craftable without a table, a 3×3 recipe is unavailable without a table, and the same 3×3 recipe becomes available when the player uses a crafting table. Also test every recipe against its declared station requirement.

### PJ-03 — First Night tutorial does not teach the complete torch-material path

**Priority:** P1  
**Classification:** Onboarding gap.

**Player journey:** Follow the displayed steps in order: gather wood, craft a crafting table, craft a wooden pickaxe, mine stone, place a torch.

**Player impact:** The final preparation step asks for a torch, but the torch recipe needs Coal and a Stick. The tutorial does not explicitly tell the player to turn planks into sticks or find and mine Coal Ore. A beginner can reach the instruction without knowing where the remaining materials come from.

**Relevant code:**
- `scripts/OnboardingSystem.cs:11-33` — six-step tutorial sequence and hint text.
- `scripts/Recipes.cs:25-30` — Planks, Sticks, and Torch recipes.

**Repair direction:** Expand the tutorial into actionable substeps: make planks, craft sticks, mine Coal Ore, craft torches, and place one. Keep hints contextual and avoid requiring a specific biome or fixed world location.

**Regression test:** Run the tutorial in a fresh world with no Coal or Sticks and verify each hint explains the next required action. Complete the sequence using normal gameplay only.

### PJ-04 — Crafting-table tutorial step can miss a quick craft-and-place sequence

**Priority:** P1  
**Classification:** Confirmed event-timing defect.

**Player journey:** Craft a Crafting Table and immediately place it before the onboarding system's next 0.3-second progress check.

**Player impact:** The tutorial checks whether a Crafting Table is currently in the player's inventory. If the player crafts and places it between checks, inventory count returns to zero before the tutorial observes it. The player has completed the intended action but the hint can remain stuck.

**Relevant code:**
- `scripts/OnboardingSystem.cs:83-112` — progress is polled every 0.3 seconds.
- `scripts/OnboardingSystem.cs:118-123` — step 1 checks `Inventory.CountOf(ItemId.CraftingTable) > 0`.
- `scripts/Player.cs` — placement emits the `BlockPlaced` event; onboarding already subscribes to this event for torch placement.

**Repair direction:** Track the Crafting Table craft event or placement event. Prefer event-based completion over transient inventory state. Decide whether the intended milestone is crafting the table or placing it, then make the hint and completion condition agree.

**Regression test:** Craft and place a Crafting Table within one frame/update interval and assert the tutorial advances exactly once. Also test crafting without placing if crafting is the intended milestone.

### PJ-05 — Merchant biome quest ignores discoveries made before acceptance

**Priority:** P1  
**Classification:** Confirmed quest-state defect.

**Player journey:** Explore three different biomes, then meet the Merchant and accept “Roads Beyond the Village.”

**Player impact:** The world stores each biome discovery as `explored/<biome>`. The quest only receives progress when a new discovery is reported. Accepting it after exploration initializes progress at zero, and revisiting already-discovered biomes does not report them again. The first stage can become impossible to complete in that world.

**Relevant code:**
- `scripts/QuestSystem.cs:88-103` — biome discovery sets the world flag and reports progress only on first discovery.
- `scripts/QuestSystem.cs:125-133` — `Accept()` creates progress at stage 0 with zero progress.

**Repair direction:** When accepting an ExploreBiome objective, reconcile it against the existing discovered-biome flags and count unique matches. Keep the count capped at the objective target and avoid double counting newly discovered biomes afterward.

**Regression test:** Discover three biomes before quest acceptance; accept the quest and verify stage 1 completes. Also test one/two previous discoveries, additional discoveries after acceptance, repeated visits, and save/load.

### PJ-06 — Mining Raw Iron does not advance the Blacksmith quest

**Priority:** P1  
**Classification:** Confirmed quest wiring defect.

**Player journey:** Accept “A Smith's Request,” obtain a sufficiently strong pickaxe, mine four Iron Ore blocks, and inspect quest progress.

**Player impact:** Iron Ore drops Raw Iron through a direct `Inventory.Add()` call. The Blacksmith quest listens to the player's `ItemGathered` event, but this normal mining path does not emit that event. The player can collect the requested item without gaining quest progress.

**Relevant code:**
- `scripts/QuestSystem.cs:76-86` — quest system subscribes to `Player.ItemGathered`.
- `scripts/Player.cs:1517-1528` — normal block drops are added directly to inventory.
- `scripts/Player.cs:1544-1555` — `GiveDrop()` records the successfully acquired amount, but the normal block-drop path does not use it.
- `scripts/BlockDb.cs:118-120` — Iron Ore's drop is Raw Iron.

**Repair direction:** Route standard block drops through one shared reward helper that both adds items and reports the actual quantity gained. Preserve harvest-tier behavior and ensure failed/overflowed drops do not count as gathered.

**Regression test:** Accept the Blacksmith quest, mine Iron Ore with valid and invalid tool tiers, and verify only successfully acquired Raw Iron advances the quest. Include partial inventory capacity and full inventory cases.

### PJ-07 — Village-generated wheat is not registered with crop growth

**Priority:** P1  
**Classification:** Confirmed generation/system integration gap.

**Player journey:** Find a generated village, wait through multiple daytime crop-growth intervals, and compare its wheat with player-planted wheat.

**Player impact:** Village farm wheat is stamped directly into generated terrain data. The crop system iterates the world's crop registry, which is populated through the normal block mutation/edit path. Generated wheat does not necessarily enter that registry, so the village farm can remain at its initial growth stage and fail to behave like a renewable food source.

**Relevant code:**
- `scripts/VillageStructures.cs:109-139` — generated farm layout stamps wheat blocks.
- `scripts/CropSystem.cs:39-64` — crop growth iterates `CropSnapshot()` and advances registered wheat.
- `scripts/VoxelWorld.cs` — crop registry is maintained separately from base generated chunk data.

**Repair direction:** Integrate generated farm crops with the existing crop-registration lifecycle when a chunk is initialized or its generated structures are applied. Do not turn generated structure blocks into persisted player edits, and do not change frozen world-generation output for existing generator versions.

**Regression test:** Generate a village in a fresh supported generator version, confirm its wheat is registered, advance daytime crop ticks, and verify growth. Confirm old generator golden hashes and saved edits remain unchanged.

### PJ-08 — Inventory overflow can destroy earned mining and mob rewards

**Priority:** P1  
**Classification:** Confirmed reward-retention defect; also identified in the previous reliability/gameplay audits.

**Player journey:** Fill the inventory, then mine a block or defeat a creature that drops an item.

**Player impact:** The block or mob can be removed/defeated even when the inventory cannot hold all drops. The unaccepted remainder is reported as inventory-full but is not represented as recoverable world loot. The player permanently loses earned resources.

**Relevant code:**
- `scripts/Player.cs:1523-1528` — normal block drops call `Inventory.Add()` and only notify when items remain.
- `scripts/Player.cs:1544-1555` — `GiveDrop()` also reports leftovers but does not create a world pickup.
- `scripts/Mobs.cs:981-1006` — mob loot is added to inventory and leftover amounts are not spawned as pickups.

**Repair direction:** Implement or extend one reusable dropped-item pickup path for overflow from mining, mobs, fishing, and other reward sources. It must have deterministic ownership/collection rules, save compatibility, chunk-unload behavior, and sensible entity caps. Avoid introducing one physics node per item when stack merging or chunk-local pickup storage can achieve the same result.

**Regression test:** With a full inventory, mine a block, defeat a mob, and catch a fish. Verify every reward remains recoverable, persists through save/load and chunk streaming, merges compatible stacks, and is not duplicated.

### PJ-09 — Wrong-tier mining removes ore without a clear harvest explanation

**Priority:** P2  
**Classification:** UX improvement candidate; verify all existing HUD/tooltips before changing behavior.

**Player journey:** Attempt to mine Iron Ore or another tier-gated ore with an insufficient pickaxe.

**Player impact:** The mining completion path can remove the block while `CanHarvest()` prevents its drop. The rule may be familiar to experienced players, but a beginner may interpret it as a broken drop system because the game does not clearly explain the required tool at the moment of failure.

**Relevant code:**
- `scripts/BlockDb.cs:253-255` — harvest requirement check.
- `scripts/Player.cs:1483-1528` — block removal and conditional drop handling.

**Repair direction:** Preserve the current harvest rule, but surface a contextual message identifying the required tool and tier. Consider showing the requirement when the crosshair targets the block, so the player is informed before mining.

**Regression test:** Try each tier-gated ore with hand, wrong tool class, insufficient tier, and valid tool. Verify drops and feedback are correct and no invalid tool yields ore.

### PJ-10 — Difficulty configuration conflicts with the progression content

**Priority:** P2  
**Classification:** Design/configuration inconsistency.

**Player journey:** Start a new world with default settings, then try to complete the Guard's hostile-creature objectives.

**Player impact:** Difficulty defaults to Peaceful, which suppresses/removes hostile mobs. The Guard quest asks the player to defeat Shades and the Ember Warden. Difficulty is configured globally in settings rather than selected and recorded with the new world. A player may encounter a quest whose required enemies are absent until they discover and change the global setting.

**Relevant code:**
- `scripts/Settings.cs:29-34` — difficulty names and Peaceful default.
- `scripts/SettingsPanel.cs:103-108` — difficulty is exposed in general Settings.
- `scripts/MainMenu.cs` — new-world creation selects Survival mode but does not capture a world-specific difficulty.
- `scripts/Mobs.cs:183-201, 258-270` — Peaceful suppresses hostiles and hostile spawning.
- `scripts/QuestSystem.cs:34-38` — Guard quest requires Shade kills and Ember Warden defeat.

**Repair direction:** Decide whether difficulty belongs to each world or is intentionally global. Prefer a per-world difficulty choice at creation if worlds are expected to have independent rules. In Peaceful, disable the incompatible quest, explain its requirement, or provide a Peaceful-compatible alternative. Preserve existing worlds' difficulty behavior during migration.

**Regression test:** Create two worlds with different selected difficulties and verify their settings remain independent if per-world difficulty is adopted. Verify every quest remains completable or clearly unavailable under each difficulty.

### PJ-11 — New-world daytime gives beginners less than four minutes before night

**Priority:** P2  
**Classification:** Pacing concern; validate with a real playtest before changing timing.

**Player journey:** Start a fresh world and follow the complete First Night tutorial without rushing.

**Player impact:** The world clock starts at `0.38`, the full day cycle lasts 600 seconds, and night begins at `0.75`. This leaves approximately 222 seconds (3 minutes 42 seconds) until the first night threshold. A new player may still be learning controls, crafting, mining, and shelter construction when darkness arrives.

**Relevant code:**
- `scripts/VoxelWorld.cs` — initial `TimeOfDay` is `0.38`.
- `scripts/DayNight.cs:18` — `DayLength` is 600 seconds.
- `scripts/DayNight.cs:39-40` — night begins at time values below `0.25` or at/above `0.75`.

**Repair direction:** Use a playtest to determine whether the opening daylight is sufficient. Possible solutions include starting earlier in the day, lengthening only the first day, or adding an explicit “first-day grace” period. Do not globally slow the day/night cycle without evaluating survival pacing and existing worlds.

**Regression test:** Run a first-time-player scenario with no prior knowledge of recipes and record time to complete the tutorial and prepare shelter. Confirm the intended percentage of new players can reach the first night with reasonable preparation.

---

## Recommended repair order

1. **Fix progression blockers first:** PJ-04, PJ-05, PJ-06, PJ-07.
2. **Protect player-earned resources:** PJ-08.
3. **Resolve the intended Survival progression design:** PJ-01 and PJ-02.
4. **Improve first-time instruction and feedback:** PJ-03 and PJ-09.
5. **Align world settings and pacing:** PJ-10 and PJ-11.

## Cross-cutting implementation requirements

- Extend existing C# systems; do not create parallel inventory, quest, crop, or pickup systems.
- Preserve existing save compatibility and persisted IDs.
- Route all voxel mutations through the existing `SetBlock` / `ApplyEdit` architecture.
- Do not change world-generation output for existing generator versions. If generated crop registration requires generation behavior changes, use a new generator version and preserve old hashes.
- Add integration/regression tests for every fixed finding, not only unit tests of isolated helper methods.
- Run the full headless test suite, a fresh-world playthrough, save/reload tests, and a performance check after implementation.
- Update `README.md` and `ROADMAP.md` only after the chosen behavior is implemented and verified.

## Explicit non-actions

No implementation was performed, no save data was modified, no branch or commit was created, and no runtime test result is claimed by this report.