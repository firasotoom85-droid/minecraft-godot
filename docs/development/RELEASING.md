# Release process

Release builds are created from version tags. Do not package arbitrary local working trees.

## Before tagging

1. Update CHANGELOG.md and ensure the Unreleased section is resolved into the intended version entry.
2. Confirm ROADMAP.md release gates and release limitations are current.
3. Confirm the default branch is green: Debug and Release warnings-as-errors, complete headless tests, formatting and documentation workflows.
4. Verify world-save compatibility and generator-version golden hashes.
5. Run the visual/performance checks required for the release candidate.
6. Review THIRD_PARTY_NOTICES.md and ensure distributed assets retain required notices.
7. Choose a semantic version tag. A tag containing a hyphen (for example v1.2.0-beta.1) is published as a prerelease.

## Automated packaging

Pushing a tag matching v* starts .github/workflows/release.yml. The workflow:
- Uses Godot 4.7.2 .NET editor and matching .NET export templates.
- Uses the repository-pinned .NET 10 SDK.
- Builds Debug and Release with warnings treated as errors and runs the headless suite.
- Exports the Linux/X11 and Windows Desktop presets.
- Performs a Linux headless startup smoke test and checks the Windows archive structure.
- Includes the project license and third-party notices in both packages.
- Generates SHA256SUMS.txt and creates the GitHub release attached to the tag.

The first run after changing the Godot version, export presets or release workflow must be reviewed manually. Windows archive validation is not a substitute for launching the build on a Windows machine.

## Release notes

The generated release body records the tag, commit, engine/SDK versions and automated validation performed. After publication, review the release page and add human-playtest, platform-specific or known-limitation notes not covered by CI.

Do not state that a platform was tested unless it was executed on that platform. Keep prerelease builds clearly marked until all 1.0 gates are satisfied.

## Rollback

Do not move or force-update an existing version tag. Publish a new patch/pre-release tag with corrected artifacts and explain the superseded build in release notes. Preserve old assets for provenance.
