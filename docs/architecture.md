# Architecture

PRDC is a Windows-only .NET 10 WinForms application with three primary boundaries.

## User interface

`MainForm` owns menus, the RDG tree, details, session controls, and application status. `NodeDialog` edits groups and servers. `AboutDialog` displays project, author, and repository information.

The application uses `SystemColorMode.System`, so WinForms inherits the Windows application color mode before the first window is created.

## RDG document model

`RdgDocument` owns the loaded `XDocument` and all supported mutations. `RdgNode` projects `file`, `group`, and `server` elements for the tree.

The original XML document remains authoritative. Unknown elements are not normalized into a second model and are preserved when possible. Supported mutations currently cover names, hosts, hierarchy, and expansion state.

## RDP session host

`RdpClientHost` embeds the Windows `MsRdpClient10NotSafeForScripting` ActiveX control through `AxHost`. It configures the selected server and supported advanced settings, invokes connect/disconnect, and polls the control's native connection state.

PRDC does not implement the RDP protocol and does not store a plaintext password. Network, authentication, certificates, and remote-session rendering remain owned by the Windows control.

## Current constraints

- Windows x64 only.
- One active RDP session at a time.
- No RD Gateway editor yet.
- No full mapping for every RDCMan setting.
- No installer or automatic updater yet.
