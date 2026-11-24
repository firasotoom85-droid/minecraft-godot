# Code style and static checks

## Formatting contract

.editorconfig is the shared source of truth:
- UTF-8, LF line endings and final newline.
- Four spaces for C# and Godot script/shader source.
- Two spaces for YAML, JSON and XML project/configuration files.
- C# opening braces on a new line.
- System directives sorted first.
- Private fields use underscore-prefixed camel case.

Naming and style preferences marked as suggestions in EditorConfig are guidance, not new compiler-warning requirements. The repository's zero-warning build policy remains enforced separately.

## Format changed C# files

Before a pull request, run:

    dotnet format whitespace minecraft.sln --verify-no-changes --include scripts/ChangedFile.cs tests/ChangedTests.cs

The Formatting workflow runs this check for C# files changed by a pull request. It intentionally does not reformat the whole historical codebase.

To apply formatting to a file you own, remove --verify-no-changes:

    dotnet format whitespace minecraft.sln --include scripts/ChangedFile.cs

Review the resulting diff carefully. Formatting should not be mixed into unrelated gameplay or performance changes.

## C# conventions

- Keep the existing Minecraft namespace and Godot partial-class conventions.
- Preserve nullable reference types; avoid introducing null-forgiving operators without a lifecycle invariant.
- Prefer the existing subsystem owner over a new singleton, manager or parallel data store.
- Keep Godot scene-tree and physics-server access on the main thread.
- Use InputMap actions for user input.
- Reuse existing item/block metadata and mutation services.
- Add deterministic tests for pure logic and regression tests for lifecycle, concurrency, persistence and chunk-boundary behavior.

See CONTRIBUTING.md and the architecture docs for project-specific invariants.
