# Gameplay polish — Milestone 5 implementation increment

Date: 2026-11-22

This increment improves the first-hour experience and two identified interaction
clarity/reliability gaps. It extends the current onboarding, quest and modal UI
systems; it does not create replacement frameworks.

## Implemented

### Contextual first-hour guide

The existing OnboardingSystem now presents 15 ordered contextual steps:

1. Move and look around.
2. Gather wood by mining a tree.
3. Open inventory.
4. Craft a crafting table.
5. Craft a wooden pickaxe.
6. Mine stone.
7. Place a torch.
8. Gather food.
9. Eat food.
10. Build a shelter by placing eight solid blocks.
11. Use a furnace.
12. Defeat a hostile creature.
13. Explore a natural cave.
14. Visit a village and interact with a resident.
15. Sleep through the night or stay alive until dawn.

Progress is based on existing successful gameplay events. For example, starter
inventory does not satisfy the gather/craft steps, a passive creature does not
satisfy the hostile step, and tilling farmland does not satisfy shelter
construction. Cave discovery checks the player's current generated cave cell
below the surface instead of relying on a coordinate-only depth heuristic.

The guide respects the existing hints setting and Creative mode. Per-world facts
are stored in the existing VoxelWorld.Flags dictionary. The six original
First Night progress keys and the completion key are retained, and the new guide
recognizes those old flags when restoring progress. No save format/version
migration is introduced.

### Quest progression feedback

QuestSystem.StageCompleted is raised only when an objective advances to the
next stage or completes a quest. Main uses it to present a HUD message for the
new stage or to tell the player to return to the village to claim the reward.
Inventory rewards, claim-once handling and transaction checks remain unchanged.

### First-person animation polish

The existing camera-child arm and held-item sprite now use reusable action
profiles for mining, melee, placement, generic use, eating, bow release,
shield-block feedback, landing and hotbar selection. The profiles are local
pose offsets and do not move the camera, create scene nodes, or alter gameplay
timing.

Independent held-state blends raise the bow while the existing break input is
held and raise the shield while the existing place/use input is held. Actual bow
firing, shield direction checks, arrow consumption and equipment durability
remain in their original gameplay code paths. Locomotion bob, idle breathing,
climbing motion and swimming strokes are layered onto the action pose; the
existing HeadBob setting disables locomotion bob in both camera and viewmodel.

The current Sprite3D icon/material cache, arm meshes, camera attachment and
shadow-disabled viewmodel geometry are retained. No texture assets or persisted
settings were added.

### Nested pause/keybind input

PauseInputPolicy prevents the pause layer from handling the same back/Escape
input while the keybind modal is open over it. The keybind modal remains
responsible for cancelling a binding capture or closing itself. The normal
pause/resume path and protection against stacking pause over other modals
remain intact.

## Regression coverage

- FirstPersonAnimationTests: action-clock progression, distinct mining/melee/
  placement/eating/bow/shield/landing poses, safe pose envelopes, and layered
  movement/held-state offsets.
- OnboardingTests: 15-step contract, event-based wood/stone/craft/food
  progress, legacy and current flag restoration, partial shelter persistence,
  torch/farmland distinctions, hostile/passive distinction, village/cave/night
  progression.
- VillageTests: partial quest progress emits no stage event; each completed
  objective emits one stage event; the final stage marks completion.
- KeybindTests: normal pause, no modal stacking, normal resume and nested
  keybind Escape ownership.

## Compatibility

- No persisted BlockId or ItemId values changed.
- No save schema/version was changed. New guide progress uses the existing
  world flags dictionary; legacy onboarding flags remain recognized.
- No generator version or generation output changed.
- No world edits were introduced. All gameplay block interactions continue
  through the existing Player and VoxelWorld.SetBlock paths.
- Existing combat, mob AI, boat, inventory, quest-reward and streaming systems
  were extended or reused, not duplicated.

## Verification

Environment: Godot 4.7.2 .NET, .NET SDK 9.0.321.

- Debug build: 0 warnings, 0 errors.
- Release build: 0 warnings, 0 errors.
- scripts/verify.sh: 186,422 checks, 0 failures.

The full suite includes the existing save compatibility, world-generation,
streaming, fluids, combat/equipment, inventory, boats, villages and keybind
regressions. Expected warning output from save tests exercises invalid-write
and backup-recovery paths; the suite completed successfully.

See [docs/FIRST_PERSON_ANIMATION.md](FIRST_PERSON_ANIMATION.md) for the action
profile implementation and first-person visual sign-off checklist.

## Remaining manual review

This is an implementation increment, not a claim that every item in the
original gameplay-polish plan is complete. Still outstanding are a physical
controller navigation/rebinding pass, combat balance encounters across
equipment tiers, rendered first-person pose review on reference GPUs, boat
handling and lifecycle playtests, and a broader
village/trading/quest usability pass. Those require interactive gameplay
validation rather than headless unit assertions.
