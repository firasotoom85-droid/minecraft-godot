# Development setup

## Required toolchain

| Tool | Version |
|---|---|
| Godot editor | 4.7.2 .NET / Mono |
| Godot export templates | 4.7.2 .NET / Mono, matching the editor |
| .NET SDK | 10.0.x; global.json selects the 10.0 feature band |
| Runtime target | net10.0 |
| Git | Current stable version |
| Shell tools | Bash, unzip and timeout |

Use the .NET-enabled Godot download. The standard Godot editor cannot compile this C# project. The Godot .NET editor provides runtime components for already-compiled game exports; the SDK is needed to build and export the project.

Official downloads:
- [Godot 4.7.2 archive](https://godotengine.org/download/archive/4.7.2-stable/)
- [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)

.NET 9 reached end of support on November 10, 2026. The active project baseline has moved to .NET 10 LTS; do not switch the repository back to an unsupported target for convenience.

## Verify the toolchain

From the repository root:

    godot --version
    dotnet --version

The Godot executable must report 4.7.2 Mono/.NET. The SDK selected in the repository should report 10.0.x.

If multiple SDK versions are installed, global.json controls SDK selection from this directory. Install the requested 10.0 feature band or a later compatible feature band; do not remove the SDK pin to hide a missing local dependency.

## First import and build

    godot --headless --editor --path . --import
    dotnet build -c Debug --nologo
    godot --path .

For GUI work, open project.godot in the Godot Project Manager and let the editor finish importing before running the project.

## Environment variables

- GODOT may point scripts/verify.sh and scripts/soak-survival.sh at a specific Godot executable.
- GODOT_TEST_TIMEOUT changes the default headless-suite timeout (300 seconds).
- SOAK_TIMEOUT_SECONDS changes the outer timeout for the survival soak script.

## Common problems

**The project does not load C# scripts:** confirm that the .NET/Mono editor is installed; the standard editor cannot load this C# project.

**The wrong SDK is selected:** run dotnet --version from the repository root and check global.json. Install the .NET 10 SDK instead of changing the project target locally.

**A scene import is still running:** close duplicate Godot editor instances and allow the first import to finish before running tests.

**The executable is not found:** verification scripts accept an explicit GODOT path. Use the full path to the Godot 4.7.2 .NET binary.

**Build output appears in Git status:** do not commit .godot/, bin/, obj/, temporary captures or local world data. Check .gitignore before adding ignore rules.
