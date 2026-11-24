## Summary

<!-- What changed and why? Link the issue(s) this addresses. -->

## Scope and ownership

- [ ] This change extends the existing subsystem rather than creating a duplicate system.
- [ ] Any cross-subsystem ownership changes are explained.
- [ ] No unrelated formatting or generated files are included.

## Compatibility

- [ ] BlockId and ItemId values are append-only.
- [ ] World-generation changes use a new version; old versions remain unchanged.
- [ ] Voxel mutations use SetBlock / ApplyEdit.
- [ ] Existing world/player saves remain compatible, with regression tests where relevant.
- [ ] Greedy meshing, chunk streaming and payload ownership are preserved.

## Verification

- [ ] Debug build passes with zero warnings.
- [ ] Release build passes with zero warnings.
- [ ] Relevant automated tests were added/updated and pass.
- [ ] Full headless verification passes.
- [ ] Performance impact was measured where relevant; hardware/profile/results are stated.
- [ ] Visual/UI changes include before/after captures at relevant resolutions and renderer presets.

## Documentation and release impact

- [ ] README / docs / ROADMAP / CHANGELOG were updated where needed.
- [ ] Third-party asset and dependency notices were updated where needed.
- [ ] Known limitations and unverified checks are stated explicitly.

## Evidence

**Commands run:**

**Results:**

**Screenshots / benchmark reports:**
