# Contributing

Thanks for taking a look! This is a solo project, but issues and pull requests
are welcome. Please keep changes focused and describe the player-visible or
technical reason in the PR.

## Before you start

- Read `ROADMAP.md` §3 (non-negotiable constraints) and the current milestone.
- For worldgen, save-format, or content-id work, open an issue first. Those
  changes have compatibility rules that are easy to break silently.

## Build and test

```bash
dotnet build
GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 ./scripts/verify.sh
```

Requirements: the Godot 4.7.2 **.NET/mono** editor and the .NET 9 SDK
(`global.json` pins the feature band). `scripts/verify.sh` discovers `godot` on
`PATH` or in common locations; you can always override it with `GODOT=…`.

The gate for a change is:

1. `dotnet build` with **0 warnings**.
2. All headless checks pass, with **0 failures**.
3. Saves still round-trip; old worlds still load.
4. New behavior has a test when it is testable outside the scene tree.

## Compatibility rules

- **Append-only ids.** Never renumber `BlockId` or `ItemId`; they are persisted
  in save files.
- **Gate worldgen by version.** Existing generator versions must regenerate
  byte-identically. Re-verify the v1 anchor chunk hash after worldgen changes.
- **One mutation seam.** Route block changes through `SetBlock` / `ApplyEdit`.
- **No new `World.Player` reads** in streaming code; use observers.
- **Determinism.** Never use `GD.Hash` or an unseeded RNG for worldgen.
- **InputMap actions only** for gameplay input, so key rebinding keeps working.
- **Release mesher buffers.** Any new payload path must end in
  `PayloadPool.Release(...)`.
- **Keep the 0-warning policy** and match the surrounding code style.

## Testing notes

The suite runs via `tests/TestRunner.tscn` in headless Godot. Tests are plain
C# with `TestAssert`; add a new suite file and register it in
`tests/TestRunner.cs`. The game is C#-first, so avoid relying on GDScript
introspection for custom classes.

## Commit and PR style

- Small commits with a clear “why” in the message.
- Reference the issue number when one exists (`Fixes #12`).
- Include a screenshot or short clip for UI/visual changes.
- Do not commit generated `.godot/`, build output, or local world saves.
