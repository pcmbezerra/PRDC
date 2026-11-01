# Architecture

PRDC is a Windows-only .NET 10 WinForms application with three primary boundaries.

## User interface

`MainForm` owns menus, the RDG tree, details, session controls, and application status. `NodeDialog` edits groups and servers. `AboutDialog` displays project, author, and repository information.

The application uses `SystemColorMode.System`, so WinForms inherits the Windows application color mode before the first window is created.

## RDG document model

`RdgDocument` owns the loaded `XDocument` and all supported mutations. `RdgNode` projects `file`, `group`, and `server` elements for the tree.

The original XML document remains authoritative. Unknown elements are not normalized into a second model and are preserved when possible. Supported mutations currently cover names, hosts, hierarchy, and expansion state.

## Component structure and integration boundaries

The architecture separates user interface orchestration, document mutation, local cache persistence, and session host execution:

```text
MainForm
 ├─ GuacamoleLoginDialog
 ├─ GuacamoleInventoryProvider
 │   └─ REST API Apache Guacamole
 ├─ GuacamoleInventoryCache
 │   └─ inventory.json
 ├─ GuacamoleConnectionReference
 └─ GuacamoleSessionHost
     └─ WebView2
```

## Session hosts (Native RDP and Apache Guacamole WebView2)

`MainForm` routes each server connection based on whether its `XElement` has an in-memory `GuacamoleConnectionReference`:

1. **Traditional RDG sessions (`SessionKind.NativeRdp` -> `RdpClientHost`)**:
   `RdpClientHost` embeds the Windows `MsRdpClient10NotSafeForScripting` ActiveX control (`mstscax.dll`) through `AxHost`. It configures the selected server and supported advanced settings, invokes connect/disconnect, and consumes `IMsTscAxEvents` to distinguish transport connection from completed login. Logoff is requested inside the active session by focusing the ActiveX control and sending the `logoff` command through native keyboard input, avoiding the separate administrative RPC channel required by the WTS APIs. Polling the control's native connection state remains as a fallback. Window resize events are debounced before `UpdateSessionDisplaySettings` updates the remote desktop resolution; Smart Sizing remains the compatibility fallback.

2. **Apache Guacamole sessions (`SessionKind.GuacamoleWeb` -> `GuacamoleSessionHost`)**:
   When a user logs in and imports an inventory through `GuacamoleInventoryProvider`, PRDC builds a pure RDG XML tree in memory and associates each imported server `XElement` with a `GuacamoleConnectionReference` (`BaseUri`, `DataSource`, `ConnectionIdentifier`, `DisplayName`, `Protocol`). `GuacamoleSessionHost` embeds Microsoft Edge WebView2 (`Microsoft.Web.WebView2`) using a shared `CoreWebView2Environment` managed by `WebView2EnvironmentProvider` at `%LOCALAPPDATA%\PRDC\WebView2\` with `--no-proxy-server`. It navigates to `#/client/{ClientIdentifier}` on the official Guacamole frontend and transitions to `RdpConnectionState.LoggedIn` only when the tunnel protocol endpoint (`/api/session/tunnels/{uuid}/protocol`) returns `HTTP 200`. When the PRDC window is resized, maximized, restored, or the splitter is moved, `GuacamoleSessionHost` uses a 250 ms debounce timer to dispatch DOM `resize` events to `window` and to the `contentDocument.defaultView` of all `<object class="resize-sensor">` elements created by Guacamole's native `guacResize` directive. This invokes `$scope.mainElementResized()` and sends the new display resolution (`c.sendSize(W, M)`) over the active tunnel to `guacd` without page reload, reconnection, CSS injection, zoom scaling, or session invalidation.

## Local persistence and caching architecture

The persistent inventory model is managed by `GuacamoleInventoryCache` independently of the RDG XML document model.

### Canonical Cache Location
The active inventory is stored at `%LOCALAPPDATA%\PRDC\Guacamole\inventory.json` using UTF-8 without BOM. The cache is governed by Windows local user profile ACLs.

### Startup Flow (Fluxo de Inicialização)

```text
PRDC
 -> Load Cache
 -> Validate
 -> Materialize RDG
 -> Populate Tree
```

1. **Detection**: On form `Shown`, PRDC checks `GuacamoleInventoryCache.Exists()` once (`_startupInventoryLoadAttempted`).
2. **Local Read**: If present, the file is read and deserialized from disk without triggering any HTTP requests, network access, or credentials dialogs.
3. **Validation**: The JSON payload is validated against structural limits (<= 50 MB, <= 64 nesting levels, <= 100,000 groups, <= 100,000 connections).
4. **Materialization**: `RdgDocument.CreateNew()` builds the in-memory tree, maps server `XElement` identities to `GuacamoleConnectionReference` instances via `ReferenceEqualityComparer.Instance`, and populates the `TreeView`.
5. **Clean State**: `_isGuacamoleInventory = true` and `_isGuacamoleInventoryModified = false`. The title bar shows `PRDC - Guacamole` without an asterisk, and closing the application exits without prompting to save.

### Update Flow (Fluxo de Atualização)

```text
Login
 -> Download Inventário
 -> Validar
 -> Persistir Cache
 -> Atualizar Estado em Memória
 -> Atualizar TreeView
```

1. **Authentication & Discovery**: `GuacamoleInventoryProvider.ImportAsync` queries the REST API across all available data sources and retrieves RDP connection hierarchies.
2. **Validation**: The data is mapped to `GuacamoleCacheRoot` and validated in memory.
3. **Session Confirmation**: `ConfirmCloseSessions()` and `ConfirmDiscardChanges()` ensure existing sessions or unsaved edits are confirmed.
4. **Atomic Save Pipeline**:
   - Creates directory if needed.
   - Cleans up residual `.tmp` files.
   - Writes `inventory.json.tmp` using `FileOptions.WriteThrough`.
   - Flushes stream to physical storage (`Flush(flushToDisk: true)`).
   - Re-reads and validates `.tmp` file integrity before touching the live cache.
   - Replaces live cache using `File.Replace(tmp, target, bak, ignoreMetadataErrors: true)` with `File.Move` fallback.
   - Cleans up `.bak` backup upon confirmed swap.
5. **State Commit**: Swaps in-memory `_document` and `_guacamoleReferences`, updates `PopulateTree()`, title, and commands.

### Resiliency, Quarantine, and Schema Handling
- **Corrupt File Quarantine**: If `inventory.json` is zero bytes, truncated, or structurally malformed, it is moved to `inventory.corrupt-{yyyyMMddHHmmss}.json`. The application falls back to a clean new document with a non-blocking status message.
- **Unsupported Schema Quarantine**: If `schemaVersion` does not match the supported version (`1`), the file is moved to `inventory.unsupported-v{version}-{yyyyMMddHHmmss}.json`.
- **No Silent Deletions**: Diagnostic files are never deleted automatically.

### Separation Between JSON Cache and RDG Export
- `Ctrl+S` (`Save`) and `Save As` export the in-memory tree as a standalone `.rdg` file and **never** overwrite `inventory.json`.
- Dirty state tracking is specialized (`HasUnsavedChanges`): unmodified Guacamole inventories do not prompt for saving on close, while deliberate user edits (adding, updating, or removing nodes) set `_isGuacamoleInventoryModified = true` and prompt to save as an `.rdg` file.
- When an exported `.rdg` is reopened from disk in a future session, it is treated as a traditional `.rdg` document and connections use `RdpClientHost` (`mstscax.dll`).

### Security, privacy, and persistence boundaries

- PRDC does not directly persist plaintext passwords, `authToken` values, cookies, or RDP credentials.
- The shared WebView2 profile at `%LOCALAPPDATA%\PRDC\WebView2\` may persist browser cookies, `localStorage` (including `GUAC_AUTH`), cache, and permissions so subsequent Guacamole tabs reuse the authenticated web session without prompting for credentials again.
- `GuacamoleSessionHost` restricts navigation to `about:blank` and the configured Guacamole `BaseUri` path prefix (`/guacamole/`), disables browser accelerator keys (`AreBrowserAcceleratorKeysEnabled = false`), context menus, autofill, password autosave, and DevTools in `Release` builds, and denies camera, microphone, geolocation, notifications, and sensor permissions in the MVP. Clipboard permissions remain governed by the default WebView2 policy; PRDC does not automatically grant clipboard-read access.
- Guacamole metadata exists only in memory (`_guacamoleReferences`). If an imported inventory is saved to an `.rdg` file and later reopened, the Guacamole link is not preserved and those nodes are treated as traditional `.rdg` servers (`mstscax.dll`), where direct RDP access may fail in segmented networks. Do not save or commit real `.rdg` files containing internal hostnames.

## Current constraints

- Windows x64 only (requires Microsoft Edge WebView2 Evergreen Runtime x64 for Guacamole sessions).
- No RD Gateway editor yet.
- No full mapping for every RDCMan setting.
- No installer or automatic updater yet.
