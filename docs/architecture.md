# PRDC Architecture & Engineering Reference (v1.4.0)

PRDC is a focused, high-performance Remote Desktop connection manager for Windows built on .NET 10 (Windows Forms). It provides unified management for traditional RDCMan `.rdg` files and Apache Guacamole portals inside an enterprise-grade, persistent, multi-source workspace.

---

## 1. High-Level System Architecture

```mermaid
graph TD
    UI[MainForm / UI Layer] --> WM[WorkspaceManager]
    UI --> FM[FavoriteManager]
    UI --> SCM[SearchCatalogManager]
    UI --> QO[QuickOpenOverlayForm]
    UI --> TM[ThemeManager]

    WM --> WDoc[(workspace.json)]
    WM --> CS[IConnectionSource Collections]

    CS --> RFS[RdgFileSource]
    CS --> GIS[GuacamoleInventorySource]

    RFS --> RDG[RdgDocument / XML]
    GIS --> GPM[GuacamolePortalManager]
    GPM --> GIC[GuacamoleInventoryCache]
    GIC --> GCache[(Portals/{portalId}/inventory.json)]

    FM --> SL[ServerLocator Engine]
    SL --> CS

    SCM --> GSE[GlobalSearchEntry Index]
    GSE --> CS
    GSE --> FM

    UI --> SC[SessionContext Isolation]
    SC --> RDP[RdpClientHost: mstscax.dll ActiveX]
    SC --> GUAC[GuacamoleSessionHost: WebView2]
    GUAC --> WVP[WebView2EnvironmentPool]
```

---

## 2. Core Architectural Boundaries

### 2.1. The `IConnectionSource` Contract
`IConnectionSource` defines the foundational abstraction for any collection of remote endpoints:

```csharp
public interface IConnectionSource
{
    Guid Id { get; }
    string DisplayName { get; }
    ConnectionSourceKind Kind { get; }
    RdgDocument Document { get; }
    IReadOnlyList<RdgNode> Roots { get; }
    bool IsDirty { get; }
    bool IsReadOnly { get; }
    IReadOnlyDictionary<XElement, GuacamoleConnectionReference> GuacamoleReferences { get; }
    void MarkClean();
}
```

Two concrete implementations exist:
1. **`RdgFileSource`:** Represents a physical RDCMan `.rdg` document on disk. Tracks unsaved modifications, file paths, and handles load, save (`Ctrl+S`), and save-as operations. If a file is moved or deleted, it transitions to a graceful `IsMissing` state.
2. **`GuacamoleInventorySource`:** Represents an imported Apache Guacamole portal. Associated with a canonical `PortalId`, it holds an in-memory `RdgDocument` projection alongside a dictionary mapping server XML elements to their `GuacamoleConnectionReference` parameters.

### 2.2. Session Context Isolation (`SessionContext`)
To prevent session cross-talk, memory leaks, and routing errors in a multi-source environment, each active session holds an immutable `SessionContext`:

```csharp
public sealed record SessionContext(
    Guid SourceId,
    string SourceDisplayName,
    ConnectionSourceKind SourceKind,
    Guid? PortalId = null,
    string? NodePath = null,
    string? ConnectionIdentifier = null);
```

- When a connection is initiated (via double-click in the tree or through Quick Open), `MainForm` attaches the `SessionContext` to the session tab.
- Native RDP sessions use `RdpClientHost` (`mstscax.dll`) with cascading dynamic COM fallback (MsRdpClient11 down to MsRdpClient6).
- Guacamole sessions use `GuacamoleSessionHost` (Microsoft Edge WebView2).

### 2.3. Shared WebView2 Environment Pool (`WebView2EnvironmentPool`)
Apache Guacamole sessions communicate with `guacd` over WebSocket tunnels embedded in WebView2. Spawning independent browser environments for each tab wastes significant RAM and handles.
`WebView2EnvironmentPool` maintains a pooled dictionary of `CoreWebView2Environment` instances keyed by portal ID, storing user data safely under `%LOCALAPPDATA%\PRDC\Portals\{portalId}\WebView2\`:
- Initializes environments with `--no-proxy-server`.
- Reuses browser processes efficiently across multiple concurrent Guacamole tabs.
- Ensures total cookie and local storage isolation between different Guacamole portals.

---

## 3. Multi-Portal Guacamole Management (`GuacamolePortalManager`)

Unlike legacy single-portal implementations, PRDC 1.4.0 manages any number of concurrent Guacamole connections:
- **Canonical Storage:** Each portal is assigned a stable `Guid` and stores metadata (`portal.json`) and its inventory cache (`inventory.json`) under `%LOCALAPPDATA%\PRDC\Portals\{portalId}\`.
- **Atomic Transactional Pipeline:** All inventory saves utilize hardware `WriteThrough`, `.tmp` file generation, in-memory structural validation, and atomic replacement via `File.Replace`.
- **Defensive Limits:** Strict validation safeguards against malicious or malformed payloads (maximum 50 MB, maximum 64 tree depth levels, maximum 100,000 groups and connections).

---

## 4. Persistent Workspace Subsystem (`WorkspaceManager`)

The workspace represents the complete operating state of PRDC:

### 4.1. `workspace.json` Structure
```json
{
  "schemaVersion": 1,
  "workspaceId": "f7d3a241-...",
  "lastModifiedUtc": "2026-11-06T04:00:00Z",
  "sources": [
    { "id": "...", "type": "RdgFile", "path": "C:\\infra\\prod.rdg", "displayName": "prod.rdg" },
    { "id": "...", "type": "GuacamolePortal", "portalId": "...", "displayName": "Guac-Prod" }
  ],
  "favorites": [ ... ],
  "layout": {
    "window": { "x": 100, "y": 100, "width": 1280, "height": 800, "isMaximized": false },
    "connectionPane": { "isVisible": true, "width": 280 }
  },
  "state": { "activeSourceId": "..." }
}
```

### 4.2. Crash Recovery and Quarantine
1. **Atomic Saving:** Written via `workspace.json.tmp` and atomic rename to guarantee consistency even during sudden OS power failure.
2. **Quarantine:** If `workspace.json` becomes corrupt or contains truncated JSON, it is automatically moved to `workspace.json.corrupt.{timestamp}` and an empty workspace is booted without crashing.
3. **Forward Schema Protection:** If a future schema version is encountered, PRDC enters Read-Only Recovery Mode to prevent data destruction.

---

## 5. Global Favorites Subsystem (`FavoriteManager`)

The Favorites subsystem provides quick access to connections spanning different `.rdg` files and Guacamole portals.

### 5.1. Resilient Server Resolution (`ServerLocator`)
Favorites do not store hardcoded pointers. Instead, each `FavoriteEntry` carries a `ServerLocator`:
- **Primary Resolution:** Resolves by exact `NodePath` (for RDG files) or `ConnectionIdentifier` (for Guacamole portals).
- **Heuristic Fallback Resolution:** If a server was moved between groups or renamed within the source document, `ServerLocator` performs a fallback search matching by `HostName` and `OriginalDisplayName`.
- **Custom Aliases (`CustomAlias`):** Users can assign custom display names (`F2`) to favorites without mutating the underlying `.rdg` document or portal inventory.
- **Orphan Lifecycle:** If an entire source is removed or deleted from disk, favorites pointing to it transition to `FavoriteOrphanStatus.MissingSource` or `MissingNode`, marked visually in the tree without dropping the favorite reference.

---

## 6. Global Search & Quick Open Subsystem (`SearchCatalogManager`)

The Quick Open subsystem (`Ctrl+P`) provides instantaneous navigation and connection capabilities.

### 6.1. In-Memory Indexing
`SearchCatalogManager` maintains a lightweight, flat index of `GlobalSearchEntry` records across all open sources and favorites:
- Indexes display names, hostnames, IP addresses, group hierarchies, and origin names.
- Automatically synchronized upon document loading, node addition/removal, or favorite changes.

### 6.2. Scoring and Ranking Algorithm
Queries are tokenized by whitespace and evaluated against candidates using a weighted scoring model:
- **Prefix Exact Match on Display Name:** Highest base priority.
- **Multi-term Token Match:** Each token matching display name, host, or group hierarchy increases candidate score.
- **Favorite Relevance Boost:** Entries marked as favorites receive a `+10,000` score bonus, ensuring they float to the top of matches.
- **Performance:** Evaluates and ranks 20,000 servers in ~2.1 ms.

### 6.3. Floating Overlay (`QuickOpenOverlayForm`)
- Modalless, borderless overlay centered dynamically over `MainForm`.
- Intercepts keyboard navigation: `Up`/`Down` keys for selection, `Enter` to connect directly via `ExecuteQuickOpenConnectAsync`, `Ctrl+Enter` to navigate to the origin in the tree via `ExecuteQuickOpenGoToSource`, and `Esc` to dismiss.

---

## 7. UI Threading & Performance Architecture

1. **Reentrancy Protection in Tab Resizing:** `UpdateSessionTabSizes` employs an `_updatingSessionTabSizes` guard and uses GDI `TextRenderer.MeasureText` directly, preventing `StackOverflowException` recursion loops.
2. **Debounced Operations:** Window resize events and workspace auto-saving utilize debounce timers (150 ms to 300 ms) to eliminate UI stutter and disk thrashing.
3. **Theme Management (`ThemeManager`):** Listens to Windows `WM_SETTINGCHANGE` messages to dynamically toggle between Dark and Light modes across all custom-rendered WinForms components (`MenuStrip`, `StatusStrip`, `TreeView`, `TabControl`, and dialogs).

---

## 8. Security & Privacy Guarantees

- **No Plaintext Credential Persistence:** Plaintext passwords, auth tokens, and private keys are never written to `workspace.json`, local caches, or logs.
- **Isolated User Profiles:** Guacamole WebView2 profiles are strictly partitioned per portal under `%LOCALAPPDATA%\PRDC\Portals\{portalId}\WebView2\`.
- **Native Security Controls:** Remote RDP sessions use the native Windows `mstscax.dll` credential dialogs and support CredSSP and NLA (Network Level Authentication).
