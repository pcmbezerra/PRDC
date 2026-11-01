<div align="center">

**English** · [Português (Brasil)](docs/readme/README_pt-BR.md)

<img src="src/PRDC/PRDC.png" alt="PRDC logo" width="180" />

# PRDC

### A focused, open-source Remote Desktop connection manager for Windows.

[![Build](https://github.com/pcmbezerra/PRDC/actions/workflows/build.yml/badge.svg)](https://github.com/pcmbezerra/PRDC/actions/workflows/build.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![MIT license](https://img.shields.io/badge/license-MIT-f4c430?style=flat-square)](LICENSE)

**[Quick start](#quick-start) · [Features](#features) · [Documentation](#documentation) · [Contributing](CONTRIBUTING.md)**

</div>

> [!IMPORTANT]
> The official source for PRDC is [pcmbezerra/PRDC](https://github.com/pcmbezerra/PRDC).
> PRDC is an independent project and is not affiliated with or endorsed by Microsoft.

PRDC organizes Remote Desktop endpoints in RDCMan-compatible `.rdg` files and opens sessions inside a native Windows desktop interface. Traditional `.rdg` servers connect directly through the Windows Remote Desktop ActiveX control (`mstscax.dll`), while connections imported from an Apache Guacamole portal open inside PRDC session tabs via Microsoft Edge WebView2 through the official Guacamole web client and `guacd`.

## Features

- Create, open, edit, and save `.rdg` files.
- Authenticate and import RDP server groups and connections from an Apache Guacamole portal ("Login to Guacamole..." / `Ctrl+Shift+G`) and open those sessions inside PRDC tabs via Microsoft Edge WebView2.
- Keep traditional `.rdg` connections running natively on `mstscax.dll`.
- Organize servers into nested groups.
- Connect to multiple hosts simultaneously in independent session tabs.
- Reconnect, disconnect, log off native RDP sessions without blocking the UI, or close an individual session tab with its `×` button.
- Show color-coded status indicators (red when disconnected, yellow while authenticating or waiting for the Guacamole tunnel, green for the active authenticated session, and blue for background authenticated sessions) in both the server tree and session tabs.
- Automatically resize the connection list width to fit visible tree items and expand session tabs to show the full server name when space permits.
- Keep a live thumbnail of each native RDP session in the details view.
- Show, hide, or automatically hide the RDG connection list.
- Resize the remote desktop resolution automatically with the application window (supported on both native ActiveX RDP sessions and Apache Guacamole WebView2 sessions via debounced resize sensor events).
- Use the native Windows credential and certificate dialogs for `.rdg` connections and the official Guacamole web portal inside WebView2 for Guacamole connections.
- Enable CredSSP, server authentication, clipboard redirection, and smart sizing on native RDP sessions.
- Follow the Windows light or dark application theme automatically.
- Preserve unsupported XML elements when editing an existing RDG document.

## Quick start

### Requirements

- Windows 10 or Windows 11, x64.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run the standard framework-dependent release (an experimental self-contained package pending validation on a Windows installation without the .NET 10 Desktop Runtime is also available).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source.
- Microsoft Edge WebView2 Evergreen Runtime (x64) to open Apache Guacamole sessions inside PRDC.
- Network access and authorization for the target Remote Desktop host or Apache Guacamole portal.

### Build and run

```powershell
git clone https://github.com/pcmbezerra/PRDC.git
cd PRDC
dotnet restore PRDC.sln
dotnet run --project src/PRDC/PRDC.csproj
```

Create or open an `.rdg` file, select a server, then double-click it or press `Ctrl+Enter`. Disconnect with `Ctrl+Shift+D`.

## Apache Guacamole Integration

PRDC provides integrated support for Apache Guacamole portals, enabling seamless organization, local persistence, and remote access to RDP servers through `guacd`.

### Overview

Users can authenticate directly against an Apache Guacamole server to discover data sources and import all authorized RDP endpoints. The connection hierarchy is automatically synchronized, persisted locally for offline use, and rendered in session tabs powered by Microsoft Edge WebView2.

### Import and Persistence Flow

1. **Direct Login**: Select `File > Login to Guacamole...` (`Ctrl+Shift+G`) to enter the portal URL, username, and password.
2. **Discovery & Hierarchy Mapping**: PRDC queries the Guacamole REST API across all available data sources, extracts nested connection groups and RDP endpoints, and resolves display names and hostnames into an in-memory RDCMan document tree.
3. **Local Cache Persistence**: The imported inventory is automatically saved to a canonical local cache at `%LOCALAPPDATA%\PRDC\Guacamole\inventory.json` using an atomic transactional pipeline (UTF-8 without BOM, hardware `WriteThrough`, integrity verification, and safe `File.Replace`).
4. **Offline Startup**: On subsequent launches, PRDC automatically detects and loads the cached inventory from disk in milliseconds, without requiring network access, background HTTP requests, or credential prompts.
5. **Inventory Refresh & Removal**:
   - `File > Refresh Guacamole list` (`Ctrl+Shift+U`): Re-authenticates against the configured portal and atomically updates the local cache and connection tree.
   - `File > Remove Guacamole inventory...`: Safely deletes the local inventory cache and resets the tree without affecting WebView2 browser profiles or active settings.
6. **Export to RDCMan (.rdg)**:
   - Users can optionally export the active inventory to a standalone `.rdg` file via `File > Save as...`.
   - The local cache (`inventory.json`) is never overwritten by `Ctrl+S` or `File > Save`.
   - During the current application run, exported connections maintain their Guacamole web session links; when reopened from disk in a future session, they behave as traditional RDCMan endpoints (`mstscax.dll`).
7. **Session Execution via WebView2**:
   - Guacamole connections run inside dedicated session tabs using Microsoft Edge WebView2, connecting directly to the official web client and `guacd`.
   - Dynamic remote desktop resizing automatically adjusts display resolutions on window resize using debounced DOM sensor events without reconnecting or interrupting the tunnel.

### Features

- Direct login to Apache Guacamole
- Automatic import of groups and connections
- Local cache for offline operation
- Manual inventory refresh (`Ctrl+Shift+U`)
- Optional export to RDCMan (`.rdg`)
- Remote sessions executed via Microsoft Edge WebView2

### Limitations

- Support is limited strictly to RDP connections; SSH and VNC connections are filtered out during import.
- Credentials (passwords, auth tokens, session cookies, RDP passwords) are never persisted in the local cache.
- The cache stores only necessary inventory metadata (`schemaVersion`, `savedAtUtc`, `baseUrl`, data sources, groups, and connection metadata).
- Exported `.rdg` files are treated as traditional RDCMan documents when reopened in subsequent runs (`mstscax.dll`).

## RDG compatibility and Guacamole session metadata

PRDC implements the file, group, server, display-name, host-name, and expanded-state portions currently needed by the application. Unknown XML elements are retained during load/save, but full behavioral parity with every RDCMan setting is not guaranteed. See [RDG compatibility](docs/rdg-format.md).

Apache Guacamole connection metadata (`BaseUri`, `DataSource`, and `ConnectionIdentifier`) exists only in memory for the current imported session and is not written to `.rdg` XML files. If you save an imported Guacamole inventory to an `.rdg` file and later reopen that `.rdg` file, the Guacamole link is not preserved: the nodes are treated as traditional `.rdg` servers (`mstscax.dll`), and direct RDP connections may fail in segmented networks where hosts are only reachable through `guacd`.

Do not save or commit real `.rdg` files containing internal hostnames, usernames, encrypted credentials, or other infrastructure metadata.

## Project structure

```text
PRDC.sln
src/PRDC/
  MainForm.cs                      Main window and commands
  RdgDocument.cs                   RDG XML persistence
  RdpClientHost.cs                 Windows RDP ActiveX host
  GuacamoleInventoryCache.cs       Local atomic cache manager for Apache Guacamole inventory
  GuacamoleInventoryProvider.cs    Apache Guacamole RDP inventory importer
  GuacamoleConnectionReference.cs  In-memory Guacamole connection reference and client URI builder
  GuacamoleSessionHost.cs          WebView2 session host for Apache Guacamole connections
  WebView2EnvironmentProvider.cs   Shared CoreWebView2Environment provider
  GuacamoleLoginDialog.cs          Apache Guacamole credentials dialog
  NodeDialog.cs                    Group and server editor
  AboutDialog.cs                   Project and author information
```

The main technical boundaries are documented in [Architecture](docs/architecture.md). Build and validation instructions are in [Development](docs/development.md).

## Documentation

- [Documentation index](docs/index.md)
- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [RDG compatibility](docs/rdg-format.md)
- [Security policy](SECURITY.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)

## Security and privacy

PRDC does not directly persist passwords, `authToken` values, cookies, or RDP credentials. For native `.rdg` connections, credential prompts are owned by the Windows RDP control (`mstscax.dll`). For Apache Guacamole sessions, WebView2 uses a local browser profile at `%LOCALAPPDATA%\PRDC\WebView2\` initialized with `--no-proxy-server`; the WebView2 profile itself may persist cookies, `localStorage` (such as `GUAC_AUTH`), cache, and site permissions across tabs and runs. Camera, microphone, geolocation, and notification permissions are denied in the MVP. Clipboard permissions remain governed by the default WebView2 policy; PRDC does not automatically grant clipboard-read access. RDG files can still contain sensitive infrastructure metadata; protect them accordingly and report vulnerabilities through the process in [SECURITY.md](SECURITY.md).

## Roadmap

- Additional RDG connection and display settings.
- RD Gateway configuration.
- Release packaging and signed binaries.

Roadmap items describe intended direction, not committed release dates.

## Contributing

Focused issues and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and use the repository templates before submitting a change.

## Origins and independence

PRDC was initially informed by static behavioral analysis of Remote Desktop Connection Manager. The PRDC contains an independent implementation and does not distribute RDCMan or `mstscax.dll`. See [DISCLAIMER.md](DISCLAIMER.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License

PRDC is available under the [MIT License](LICENSE).

## Author

[Paulo Bezerra (@pcmbezerra)](https://github.com/pcmbezerra)
