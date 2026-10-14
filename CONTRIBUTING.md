# Contributing to PRDC

PRDC welcomes focused bug fixes, documentation improvements, and features that strengthen its role as a Windows Remote Desktop connection manager.

## Before opening an issue

- Search existing issues and pull requests.
- Report one primary problem or proposal per issue.
- Remove credentials, internal hostnames, private RDG content, and sensitive paths.
- For bugs, include the shortest reproduction, Windows version, PRDC version, and observed result.

Security vulnerabilities must follow [SECURITY.md](SECURITY.md) and must not be reported publicly.

## Development setup

Requirements:

- Windows x64.
- .NET 10 SDK.

```powershell
dotnet restore PRDC.sln
dotnet build PRDC.sln --configuration Release --no-restore
dotnet run --project src/PRDC/PRDC.csproj
```

## Change scope

- Keep each pull request focused on one outcome.
- Avoid unrelated dependencies, abstractions, or refactors.
- Preserve unknown RDG XML unless the change explicitly owns that schema.
- Do not embed or redistribute Microsoft binaries.
- Do not add passwords, production hosts, or private `.rdg` files as fixtures.
- Document user-visible behavior and platform requirements.

## Pull requests

Use a concise conventional title such as `fix(rdp): handle failed connection` or `docs: clarify RDG compatibility`.

In the pull request body, describe:

- The problem and observable outcome.
- The chosen implementation and intentionally excluded scope.
- Commands actually run and their results.
- Security, privacy, compatibility, or migration impact.

AI-assisted contributions are welcome. A human author must review the final diff, understand the behavior, and remain responsible for the submitted change.

## Validation

At minimum, run:

```powershell
dotnet build PRDC.sln --configuration Release
```

Changes to RDP behavior should also be tested on an authorized Windows host. State clearly when a real connection was not tested.
