# Security policy

## Supported project versions

Security fixes are applied to the current development branch. Older downloadable builds are not guaranteed to receive fixes; upgrade to the latest published release or prerelease when a fix is announced.

The project currently targets Godot 4.7.2 .NET and .NET 10. See the Microsoft [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) and the [development setup guide](docs/development/SETUP.md) for the current toolchain.

## Reporting a vulnerability

Do not publish an exploitable vulnerability, credential, private save, or sensitive crash dump in a public issue.

Use GitHub's private contact channel to contact the repository owner. Include:
- Affected release/commit and operating system.
- Godot and .NET versions.
- A concise reproduction procedure and expected/actual behavior.
- Impact and any known workaround.
- A minimized project or log with secrets and personal data removed.

Please allow time for triage and a fix before public disclosure. This is a solo-maintained project; no formal response-time SLA is promised.

## Scope

Relevant reports include remote/code execution, unsafe archive import/export, path traversal, save corruption, data disclosure and build/release supply-chain problems. Do not test against other users' worlds or systems.
