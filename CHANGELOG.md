# Changelog

All notable changes to PRDC will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use semantic versioning.

## [Unreleased]

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

[Unreleased]: https://github.com/pcmbezerra/PRDC/commits/main
