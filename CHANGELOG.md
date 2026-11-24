# Changelog

Notable repository and game changes are recorded here. The historical engineering diary remains in docs/history and subsystem audit folders; this file is for release-facing changes.

## Unreleased

### Repository and build infrastructure

- Reorganized the top-level README, roadmap and project decisions document; preserved previous long-form documents under docs/history.
- Added architecture, data-lifecycle, setup, testing, code-style, content-authoring and release guides.
- Added EditorConfig conventions, changed-file C# formatting checks and active-documentation link validation.
- Added issue/PR templates, contributor standards, a security policy and third-party notices.
- Moved the project target and CI toolchain from unsupported .NET 9 to .NET 10.
- Added tag-driven Linux/Windows export packaging with build metadata and SHA-256 checksums.

## v1.1.0-beta.1 — 2026-11-23

- Prerelease build based on commit b9fea0a and Godot 4.7.2 .NET.
- Linux x86_64 and Windows x86_64 downloadable archives were published with SHA-256 checksums.
- The Linux export passed a headless startup smoke test. The Windows archive passed integrity/PE checks but was not executed on Windows in the recorded release validation.
- The release notes link to the full v1.0.0...v1.1.0-beta.1 comparison for the complete change set.

## v0.1 — 2026-10-14

- First packaged Linux x86_64 and Windows x86_64 builds.
- Introduced the initial procedural streamed voxel world, survival loop, campfires, companion sheep, day/night, weather, adaptive audio, minimap and photo mode.

## Versioning

Use semantic version tags. Pre-release identifiers such as beta.1 remain prereleases; do not label them stable.
