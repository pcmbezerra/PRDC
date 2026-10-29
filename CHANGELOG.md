# Changelog

All notable changes to PRDC will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use semantic versioning.

## [Unreleased]

## [1.0.2] - 2026-10-29

### Added

- Color-coded status indicator circles in the server tree and session tabs (red when disconnected, yellow while authenticating, green when authenticated and active, blue when authenticated in a background tab).
- Automatic width adjustment of the connection list pane based on visible tree nodes.
- Dynamic session tab width expansion to display full server names, truncating with an ellipsis only when space is constrained across many open tabs.

### Fixed

- Eliminated continuous session tab flickering caused by redundant `PollState` notifications, background `PrintWindow` thumbnail captures while the sessions view is active, and unnecessary `TabPage`/`TreeNode` text reassignments.

## [1.0.0] - 2026-10-19

### Added

- Open, create, edit, and save RDG files.
- Group and server management.
- Embedded RDP connection and disconnection through the Windows ActiveX control.
- Connection and login state displayed per server in the tree.
- Automatically refreshed thumbnail of the active remote session.
- Remote desktop resolution automatically resized with the application window.
- Multiple simultaneous RDP sessions in independent tabs.
- Session commands for reconnect, disconnect, non-blocking Windows logoff, and closing individual tabs from a visible `×` button.
- Visible, hidden, and auto-hide modes for the RDG connection list.
- Automatic Windows light and dark theme integration.
- Application icon and open-source repository documentation.

### Fixed

- ActiveX cleanup no longer raises a JIT exception when its COM wrapper was already released.

[Unreleased]: https://github.com/pcmbezerra/PRDC/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/pcmbezerra/PRDC/compare/3540546...v1.0.2
[1.0.0]: https://github.com/pcmbezerra/PRDC/commits/3540546
