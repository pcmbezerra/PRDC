# Development

## Requirements

- Windows x64.
- .NET 10 SDK.
- The Windows Remote Desktop ActiveX control (`mstscax.dll`).

No external NuGet packages are required.

## Restore and build

```powershell
dotnet restore PRDC.sln
dotnet build PRDC.sln --configuration Release --no-restore
```

## Run

```powershell
dotnet run --project src/PRDC/PRDC.csproj
```

The Release executable is written to:

```text
src/PRDC/bin/Release/net10.0-windows/PRDC.exe
```

## Manual validation

For changes that affect persistence:

1. Create a document with nested groups and servers.
2. Save and reopen the `.rdg` file.
3. Confirm names, hosts, hierarchy, and expansion state.
4. Confirm unrelated XML elements remain present.

For changes that affect RDP:

1. Use only a server you are authorized to access.
2. Confirm connecting, connected, and disconnected states.
3. Confirm the Windows credential and certificate dialogs remain native.
4. Confirm disconnect and application shutdown release the session.

Never commit production RDG files or credentials as test fixtures.

## Pull-request validation

Record only commands and runtime checks that actually ran. If no authorized RDP endpoint was available, state that limitation explicitly.
