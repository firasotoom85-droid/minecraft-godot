# Adding content safely

This checklist applies to blocks, items, recipes, generated structures, creatures and persisted gameplay state.

## Blocks and items

1. Inspect BlockId.cs, ItemId.cs, BlockDb.cs and ItemDb.cs before adding definitions.
2. Append numeric BlockId and ItemId values only. Never reorder, reuse or reinterpret a persisted number.
3. Update block/item mappings, stack sizes, harvest/drop behavior, materials, recipes and tooltips as appropriate.
4. Keep atlas tile assignments unique and within the validated atlas. Add original artwork or record the source, author and license of imported assets.
5. Extend ContentTests and relevant item-icon, recipe, material-gallery or interaction tests.

BlockId and ItemId are persistent save identifiers, not an alphabetical or visual ordering mechanism.

## World generation

1. Identify the exact generator version that currently owns the behavior.
2. Never change the output of an existing generator version. Add a new version module and route only newly created worlds to it.
3. Ensure deterministic coordinate hashing and seeded randomness. Do not use GD.Hash or unseeded random sources for world generation.
4. Test positive and negative coordinates, chunk-order independence, seams and the exact intended delta against the previous generator.
5. Add or update golden chunk hashes and visual validation references.

If a change only affects presentation, prove that generation output and persisted IDs remain unchanged.

## World mutations

Use VoxelWorld.SetBlock / ApplyEdit for block changes. Do not write directly into Chunk.Blocks or the world's edit dictionaries from gameplay systems. Verify that lighting, shape state, meshing, save dirtiness and cross-chunk neighbours are updated by the existing mutation path.

## Saves

Prefer optional fields with safe defaults. Update the relevant save records and tests for current-format round-trips, old-save loading, atomic-write failures, backup recovery and world import/export where applicable. Never silently discard unknown or malformed player data.

## New systems

Extend the current owning subsystem and event flow. Document lifecycle and ownership, keep worker/main-thread boundaries intact, and add tests for cancellation, scene exit, stale results and repeated setup/teardown where relevant.

See DATA_LIFECYCLE.md for the underlying invariants.
