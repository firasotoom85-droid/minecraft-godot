# Contributing

Issues and focused pull requests are welcome. The project is currently maintained as a solo project; contributors should expect review to prioritize compatibility, reproducibility and small, independently verifiable changes.

## Before changing code

1. Read [the architecture guide](docs/architecture/ARCHITECTURE.md) and [data lifecycle rules](docs/architecture/DATA_LIFECYCLE.md).
2. Check [ROADMAP.md](ROADMAP.md) and existing issues/PRs to avoid duplicating work.
3. Read [ADDING_CONTENT.md](docs/development/ADDING_CONTENT.md) before changing blocks, items, world generation or saves.
4. For world-generation, persisted-ID, save-format or mutation-path changes, open or update an issue before implementation.

## Toolchain

- Godot 4.7.2 **.NET / Mono** editor and matching export templates.
- .NET 10 SDK. The repository pins a 10.0 SDK baseline in global.json.
- Git, Bash and the standard timeout utility for verification scripts.

See [development setup](docs/development/SETUP.md) for installation and troubleshooting.

## Build and verify

    dotnet build -c Debug --nologo -warnaserror
    dotnet build minecraft.csproj -c Release --nologo -warnaserror
    GODOT=/path/to/Godot_v4.7.2-stable_mono ./scripts/verify.sh

The full registered headless suite runs through tests/TestRunner.tscn. For a long-running real-scene survival check:

    GODOT=/path/to/Godot_v4.7.2-stable_mono bash scripts/soak-survival.sh 3600

Do not claim GPU visual validation from a headless test. Use the documented production capture scenes for renderer checks and record the hardware/preset.

## Compatibility and architecture rules

- **Append-only IDs:** never renumber or reuse BlockId or ItemId values.
- **Version world generation:** preserve old versions byte-for-byte; increment the generator version for new generation rules.
- **Use the mutation seam:** block edits must go through VoxelWorld.SetBlock / ApplyEdit.
- **Preserve saves:** keep older worlds loadable and add regression coverage for serialization changes.
- **Respect worker ownership:** do not use the scene tree or physics server from mesher workers; reject stale payloads and release pooled buffers on every path.
- **Keep streaming boundaries:** do not add direct World.Player reads to streaming code; use the established observer pattern.
- **Use InputMap actions:** do not add raw physical-key gameplay checks.
- **Preserve the renderer architecture:** do not replace greedy meshing, chunk streaming or existing material pipelines as a shortcut.

## Code and formatting

The repository uses four-space indentation for C#, nullable reference types and the naming/formatting conventions in .editorconfig. Before opening a PR, format every changed C# file:

    dotnet format whitespace minecraft.sln --verify-no-changes --include scripts/ChangedFile.cs tests/ChangedTests.cs

The Formatting workflow checks changed C# files on pull requests. Do not run a whole-repository reformat as part of an unrelated change.

## Pull requests

- Keep one coherent change per PR and explain the player-visible or engineering reason.
- Link the issue(s) being addressed.
- Describe the implementation and any compatibility decisions.
- List the exact build/test commands and results; distinguish tests you ran from CI results.
- Include before/after screenshots or a short clip for visual/UI changes.
- Include benchmark conditions and measured before/after values for performance claims.
- Update active documentation and CHANGELOG.md when behavior, constraints or release-facing information changes.
- Never commit .godot/, build output, local world saves, secrets or generated temporary captures unless explicitly required as reviewed test references.

See the [pull request template](.github/PULL_REQUEST_TEMPLATE.md) for the complete checklist.
