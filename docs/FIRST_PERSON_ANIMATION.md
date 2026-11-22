# First-person animation polish

Date: 2026-11-22

## Existing viewmodel retained

The implementation continues to use the current Player.BuildArm hierarchy:
a camera-child Node3D, the existing sleeve/hand meshes, and the existing
Sprite3D held-item icon. Item textures and MaterialOverride instances still
update only when the selected item changes. Arm meshes remain shadow-disabled.

No second viewmodel, AnimationPlayer, scene-node hierarchy, imported model,
animation asset or per-frame collection was introduced.

## Action profiles

FirstPersonAnimationState provides a small action clock. FirstPersonAnimation
evaluates an action and returns local arm/item offsets:

| Action | Trigger | Motion intent |
| --- | --- | --- |
| Mine | Initial target, target change, mining tick and block break | Short tool wind-up followed by a forward/down impact |
| Melee | A landed melee swing attempt | Larger wind-up and lateral follow-through |
| Place | Placement interaction, hoe/seed placement, boat placement | Compact forward/down placement tap |
| Use | Bucket, fishing, sleep, campfire and general interaction | Small responsive interaction pulse |
| Eat | Food is consumed | Raise food toward the face and return |
| Bow release | Existing bow fire event | Pull-back and short release recoil |
| Shield block | Existing directional shield block succeeds | Raise/brace the held shield |
| Landing | Existing landing event | Brief arm/item dip and recovery |
| Select | Hotbar selection changes | Small settle motion |

The animation does not change hit registration, mining progress, item consumption,
arrow cadence, shield blocking, damage, durability or voxel mutations.

## Layered movement and held states

- Ground locomotion adds a small viewmodel bob. The existing Settings.HeadBob
  option disables that motion.
- Idle breathing is deliberately subtle.
- Climbing adds a small alternating arm pose when vertical movement is present.
- Swimming retains the original stroke motion and blends in action feedback at
  reduced strength.
- Bow draw and shield guard are separate smoothed held-state blends layered
  over the action pose. They use the existing break_block and place_block
  input actions and do not add bindings.

All values are local offsets from the existing arm/item rest transforms. Profile
tests constrain offsets and scale to avoid large camera-space jumps.

## Performance and compatibility

- One small animation state object is allocated per Player instance.
- Per-frame evaluation uses value types and trigonometric/interpolation math;
  it does not create collections, materials, textures or scene nodes.
- No extra process callback or physics query was added.
- No item/block IDs, save fields, settings, generator versions, combat rules or
  world mutation paths changed.

## Automated verification

FirstPersonAnimationTests covers:
- action clock start, advance, replacement and expiry;
- distinct mining, melee, placement, eat, bow, shield and landing poses;
- bounded arm/item offsets and held-item scale;
- locomotion, idle, climb, bow-draw and shield-guard layers.

Godot 4.7.2 .NET validation:
- Debug build: 0 warnings, 0 errors.
- Release build: 0 warnings, 0 errors.
- Integrated scripts/verify.sh: 186,422 checks, 0 failures.

## Manual visual sign-off checklist

A headless run cannot establish the final visual pose or screen-space clipping.
Before calling this visual pass fully signed off, inspect the running Forward+
scene at 1280x720 and 1920x1080 on the MX230 and RTX 4060:

1. Empty hand, block, pickaxe, axe, sword, shovel, food, fishing rod, bow and
   shield idle poses.
2. Mine each tool category against stone, dirt, logs and ore; ensure tool impact
   does not obscure the crosshair or pull out of frame.
3. Test repeated mining and rapid melee strikes at each weapon cadence.
4. Place full blocks, partial shapes, torches, farmland and seeds.
5. Eat food; cast/reel the fishing rod; collect/place bucket fluids.
6. Hold/release bow fire and inspect draw/recoil.
7. Hold shield while facing threats; confirm shield pose aligns with the
   existing block-success feedback.
8. Jump/land, walk/sprint, climb and swim with HeadBob both enabled and disabled.
9. Verify the arm does not intersect the screen edge or cover important targets
   at different FOV settings.

These visual and physical-controller checks remain outstanding; no GPU screenshot
or interactive hardware sign-off is claimed by the automated test run.
