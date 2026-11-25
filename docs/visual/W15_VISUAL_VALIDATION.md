# W15 Visual Validation Plan

**Status:** Procedure prepared; production captures are still pending.  
**Engine:** Godot 4.7.2 .NET  
**Scope:** minimap invalidation, photo-mode camera parity and intra-chunk transparent glass sorting.

These checks require the running game and a real renderer. Headless unit tests do not replace them. Do not mark W15 visually verified until the screenshots and their environment metadata are reviewed.

## Capture metadata

Record the following beside every capture:

- Commit SHA and date.
- Godot version and renderer (Forward+ / Compatibility).
- GPU model and driver.
- Window resolution and graphics preset.
- World seed, generator version, view distance and Far LOD radius.
- Player/camera position and orientation.
- The exact action that triggered the capture.

Keep camera position, world seed and settings fixed when comparing before/after images.

## Minimap

Save separate images for these cases:

1. **Heading redraw:** wait for the terrain texture to finish, stand still, capture the current arrow, rotate the player by at least 90 degrees without translating, and capture again. The arrow must rotate while the terrain texture remains unchanged.
2. **Nearby mutation:** capture a completed map, place or remove a surface block inside the represented radius, remain stationary and capture after the sliced rebuild publishes. The edited terrain must be reflected.
3. **Distant mutation:** make an edit outside the represented radius. Confirm it does not trigger an unnecessary map rebuild.
4. **Interrupted rebuild:** move far enough to request a rebuild or change zoom, hide the minimap before all row slices finish, then reopen it without moving. It must restart and publish a complete current texture; it must not remain blank or indefinitely stale.
5. **Origin handling:** perform the initial capture with the player near world origin. A completed map at (0, 0) must not cause a continuous rebuild loop.

Suggested output names:

- w15-minimap-heading-before.png
- w15-minimap-heading-after.png
- w15-minimap-nearby-mutation.png
- w15-minimap-distant-mutation.png
- w15-minimap-interrupted-rebuild.png
- w15-minimap-origin.png

## Photo mode

1. Start a world with the mouse cursor visible (do not move or click the mouse first), enter photo mode, and capture the first successful mouse-look frame. The camera must acquire mouse ownership without a large recentering jump.
2. Set Field of View to 60, enter photo mode and capture. Repeat at 110. Photo mode must use the current setting rather than its scene-start value.
3. Use a high Far LOD configuration (for example, MAX with a 640 m LOD radius), aim at distant tiles, and capture in gameplay and photo mode from the same camera position. The photo camera must retain the same required far-plane range.
4. Repeat vertical look with Invert-Y both enabled and disabled using gameplay mouse, controller right stick and photo-mode mouse. All three paths must agree.

Suggested output names:

- w15-photo-entry-mouse-capture.png
- w15-photo-fov-60.png
- w15-photo-fov-110.png
- w15-photo-far-lod-gameplay.png
- w15-photo-far-lod.png

## Transparent glass sorting — issue #137

This is a visual investigation, not a confirmed defect. Preserve the existing per-chunk glass mesh and low-node-count batching while reproducing it.

1. In one chunk, place at least two glass panes at different depths whose projected screen bounds overlap.
2. Capture from one side, then move the camera through the depth-order transition and capture again.
3. Repeat from the opposite side of the panes.
4. Repeat once with panes close together and once with panes separated across the chunk.
5. Keep the camera, lighting, graphics preset and pane positions identical between comparisons.

Do not split every pane into an individual MeshInstance3D, alter greedy meshing, or change transparency policy merely because the chunk contains several panes. Record an actual incorrect blend and the smallest reproduction before choosing a sorting fix.

Suggested output names:

- w15-glass-overlap-front.png
- w15-glass-overlap-transition.png
- w15-glass-overlap-back.png
- w15-glass-separated.png

## Acceptance

Review all images side by side. Record the commit, capture environment and pass/fail decision. Any glass change must include before/after captures and a performance comparison demonstrating that the existing chunk batching strategy has not been regressed.
