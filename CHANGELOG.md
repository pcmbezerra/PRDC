# Changelog

All notable changes to PRDC will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use semantic versioning.

## [Unreleased]

## [1.2.0] - 2026-10-30

### Added

- Integração com Apache Guacamole via cache local canônico (`%LOCALAPPDATA%\PRDC\Guacamole\inventory.json`) para persistência offline de inventário.
- Carregamento automático e transparente do inventário Guacamole na inicialização do PRDC sem requisições HTTP e sem modais bloqueantes.
- Comando `Arquivo > Atualizar lista do Guacamole` (`Ctrl+Shift+U`) com URL base pré-preenchida e revalidação atômica de credenciais.
- Comando `Arquivo > Remover inventário do Guacamole...` para expurgo seguro do cache local sem afetar perfis ou dados de navegação do WebView2.
- Pipeline de gravação atômica transacional de 16 etapas com gravação direta em hardware (`WriteThrough`), revalidação prévia de integridade e substituição segura via `File.Replace` com cópia de segurança (`.bak`).
- Mecanismo de quarentena automática para diagnóstico: isolamento de arquivos corrompidos (`inventory.corrupt-*`) e esquemas incompatíveis (`inventory.unsupported-v*`) sem travar a aplicação.
- Limites de segurança estruturais defensivos (tamanho máximo de 50 MB, profundidade máxima de 64 níveis, até 100.000 grupos e 100.000 conexões com travessia iterativa imune a `StackOverflowException`).

### Changed

- Novo fluxo de persistência baseado em cache local canônico desacoplado do modelo de documento RDG.
- Comando `Salvar` (`Ctrl+S`) e `Salvar como...` atuam exclusivamente como exportadores para arquivos `.rdg` tradicionais, sem jamais sobrescrever o arquivo de cache JSON.
- Abertura de novos arquivos ou documentos RDG preserva o arquivo de cache local intacto no disco para futuras inicializações.

### Fixed

- Correção definitiva do controle de alterações não salvas (`IsDirty`): inventários Guacamole carregados do cache ou recém-importados abrem limpos (`HasUnsavedChanges = false`), sem asterisco no título e sem prompts indevidos de salvamento ao fechar.
- Marcação de alterações (`*`) restrita a mutações manuais deliberadas pelo usuário na árvore, desconsiderando expansão de nós, seleção, redimensionamento ou conexões.
- Tratamento resiliente de falhas de gravação e inicialização com mensagens específicas mapeadas por tipo de erro (`NotFound`, `Corrupt`, `UnsupportedSchema`, `ValidationFailed`, `AccessDenied`, `IoFailure`).

## [1.1.0] - 2026-10-30

### Added

- Apache Guacamole authentication and inventory import (`File > Login to Guacamole...` / `Arquivo > Login no Guacamole...` / `Ctrl+Shift+G`) that authenticates against a Guacamole portal across all available data sources, extracts RDP connection hierarchies, resolves hostnames from connection display names, and generates a native RDG document in memory.
- Embedded Apache Guacamole web session host (`GuacamoleSessionHost`) powered by Microsoft Edge WebView2 (`Microsoft.Web.WebView2` `1.0.4314.35`) with direct WebView2 connectivity support for internal Guacamole portals (`--no-proxy-server`) and a shared profile under `%LOCALAPPDATA%\PRDC\WebView2\`, opening imported Guacamole connections inside PRDC session tabs via `guacd` while preserving `RdpClientHost` (`mstscax.dll`) for traditional `.rdg` servers.
- Dynamic remote session resizing for Apache Guacamole WebView2 tabs with a 250 ms debounce timer, dispatching DOM resize events to the frontend's native `guacResize` sensor objects so that `guacd` resizes the remote desktop without page reloads, reconnections, CSS injection, zoom manipulation, or tunnel interruption.
- Secure WebView2 sandbox configuration with browser accelerator keys, DevTools, autofill, and password saving disabled, and camera, microphone, geolocation, and notification permissions denied by default. Clipboard permissions remain governed by the default WebView2 policy; PRDC does not automatically grant clipboard-read access.
- Multi-tab support for concurrent Apache Guacamole sessions with color-coded status tracking (yellow while connecting/waiting for tunnel, green for active session, blue for inactive connected background sessions).
- Standard framework-dependent distribution package for Windows x64 requiring the .NET 10 Desktop Runtime, alongside an experimental self-contained package pending validation on a Windows installation without the .NET 10 Desktop Runtime.

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

[Unreleased]: https://github.com/pcmbezerra/PRDC/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/pcmbezerra/PRDC/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/pcmbezerra/PRDC/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/pcmbezerra/PRDC/compare/3540546...v1.0.2
[1.0.0]: https://github.com/pcmbezerra/PRDC/commits/3540546
