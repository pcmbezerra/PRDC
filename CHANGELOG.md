# Changelog

All notable changes to PRDC will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases use semantic versioning.

## [Unreleased]

## [1.4.0-RC1] - 2026-11-06

### Added

- **RFC-004 — Workspace Multiorigem & Persistente:**
  - Contrato de abstração `IConnectionSource` para gestão desacoplada de origens documentais.
  - Suporte concorrente a múltiplos arquivos `.rdg` (`RdgFileSource`), com salvamento individual (`Ctrl+S`), salvamento global (`Ctrl+Shift+S`) e fechamento isolado de origens.
  - Suporte a múltiplos portais Apache Guacamole (`GuacamoleInventorySource`) com diretórios de cache locais isolados (`%LOCALAPPDATA%\PRDC\Portals\{portalId}\`).
  - Persistência atômica de workspace em `%LOCALAPPDATA%\PRDC\workspace.json` preservando origens abertas, layout, limites de janela e estado de expansão da árvore.
  - Pipeline de recuperação tolerante a falhas com quarentena automática de workspaces corrompidos (`workspace.json.corrupt.*`) e fallback seguro para modo somente-leitura em esquemas futuros.
  - Vínculo imutável de contexto de execução por sessão (`SessionContext`) garantindo roteamento transparente para o host correto (`RdpClientHost` ou `GuacamoleSessionHost`).
  - Gerenciador compartilhado de instâncias WebView2 (`WebView2EnvironmentPool`) com isolamento por portal e reuso eficiente de processos do Microsoft Edge.

- **RFC-005 — Favoritos Globais:**
  - Camada de acesso rápido unificada através do nó raiz `★ Favoritos` no topo da árvore de conexões.
  - Mecanismo de resolução resiliente em dois níveis (`ServerLocator`): primário (por `NodePath` ou `ConnectionIdentifier`) e secundário/heurístico (por correspondência de `HostName` e `DisplayName`).
  - Suporte a apelidos personalizados (`CustomAlias`) preservados de forma independente do nome do servidor de origem (`F2` / `FavoriteAliasDialog`).
  - Tratamento visual e funcional de conexões órfãs (`FavoriteOrphanStatus`), detectando automaticamente origens ausentes ou servidores excluídos sem quebrar a árvore.
  - Atalhos de teclado dedicados: `Ctrl+D` (alternar favorito), `F2` (renomear/definir apelido), `Ctrl+G` (navegar até o nó de origem).

- **RFC-006 — Busca Global / Quick Open (`Ctrl+P`):**
  - Motor de busca instantâneo em memória (`SearchCatalogManager`) capaz de indexar e consultar mais de 20.000 servidores em menos de 3 ms.
  - Algoritmo de ranqueamento determinístico ponderado por correspondência de prefixo, termos múltiplos (ex.: `sql prod`), correspondência em grupo/caminho, com bônus de relevância para servidores favoritados.
  - Interface visual flutuante sem bordas (`QuickOpenOverlayForm`) acionada globalmente por `Ctrl+P`.
  - Navegação fluida por teclado (`Setas`, `Enter` para conexão imediata, `Ctrl+Enter` para foco e seleção na árvore, `Esc` para fechar).
  - Suporte total aos temas Claro e Escuro da aplicação.

### Changed

- Migração da arquitetura de documento único para Workspace Multiorigem.
- Otimização do carregamento da árvore (`PopulateTree`) com redução de tempo de renderização de 1.098 ms para 17 ms em 10.000 servidores.
- Atualização da versão do produto para `1.4.0-rc1` com suporte a compilação self-contained single-file.

### Fixed

- **Reentrância de Redimensionamento de Abas (`UpdateSessionTabSizes`):** Correção de ciclo recursivo no redimensionamento de `_sessionTabs` com adição de trava reentrante (`_updatingSessionTabSizes`) e substituição de alocações gráficas GDI por medição estática via `TextRenderer.MeasureText`.
- **Compatibilidade ActiveX RDP (mstscax.dll):** Estabilização do fallback em cascata dinâmico de versões de cliente RDP COM em ambientes legados e virtualizados.

### Performance

- **Warm Startup:** 118.75 ms (< 250 ms SLA).
- **Cold Startup:** 120.76 ms (< 1500 ms SLA).
- **PopulateTree (10k nós):** 17.27 ms (melhoria de 98,4% sobre a baseline v1.2.2).
- **Busca Global em 20k nós:** 2.12 ms (< 5 ms SLA).
- **Abertura do Quick Open:** 1.63 ms (< 30 ms SLA).
- **Total de Testes Aprovados:** 488/488 (100% de aprovação sem falhas).

## [1.2.2] - 2026-11-01

### Changed

- **Simplificação da Barra de Ações (UI Cleanup):** Remoção completa da barra horizontal de ferramentas de ações (`ToolStrip`: Novo, Abrir, Salvar, Grupo, Servidor, Editar, Remover, Conectar, Desconectar), centralizando toda a navegação e operações no `MenuStrip` principal para maior área útil de visualização de conexões e sessões.

### Added

- **Modo Tela Cheia (F11 / Esc):** Implementação de visualização imersiva em tela cheia acessível via menu `Exibir > Tela cheia` ou atalho `F11`. No modo ativo, `MenuStrip` e `StatusStrip` são ocultados e a janela expande para os limites do monitor sem bordas. Retorno transparente suportado via `ESC`, `F11` ou menu.
- **Gerenciamento e Persistência de Tema:** Adicionado menu `Exibir > Tema` com opções `Claro`, `Escuro` e `Usar tema do Windows` (com sincronização em tempo real via eventos `WM_SETTINGCHANGE` e chave `AppsUseLightTheme`). A escolha é salva em `%LOCALAPPDATA%\PRDC\settings.json`.
- **Tema Visual Abrangente em Controles e Diálogos:** Suporte a temas claro/escuro com integração ao DWM Immersive Dark Mode do Windows, estilização customizada de `MenuStrip`, `StatusStrip`, `TreeView`, `TabControl` (`_workspaceTabs` e `_sessionTabs` via owner-draw) e todos os diálogos do PRDC (`AboutDialog`, `NodeDialog`, `GuacamoleLoginDialog`).
- **Preservação de Subsistemas Nativos:** Isolamento estrito de componentes de renderização nativa de sessões, garantindo funcionamento inalterado do Apache Guacamole (WebView2) e do cliente RDP ActiveX (`mstscax.dll`).

## [1.2.1] - 2026-11-01

### Fixed

- **Resolução Dinâmica do Cliente ActiveX RDP (mstscax.dll):** Correção crítica de incompatibilidade em Windows 10, Windows Server 2016/2019/2022 e Windows 11 LTSC que causava a exceção COM `0x80040111 (CLASS_E_CLASSNOTAVAILABLE)` ao tentar abrir sessões RDP nativas.
- **Mecanismo de Fallback em Cascata:** Implementada detecção automática e seleção dinâmica da versão mais recente suportada pelo sistema operacional local (MsRdpClient11 -> MsRdpClient10 -> MsRdpClient9 -> MsRdpClient8 -> MsRdpClient7 -> MsRdpClient6).
- **Retrocompatibilidade de Configurações Avançadas:** Mapeamento progressivo de propriedades de teclado e tela (`SecuredSettings2`/`SecuredSettings`, `AdvancedSettings9` até `AdvancedSettings6`) garantindo integridade de parâmetros em clientes legados.
- **Tratamento Defensivo contra Erros de Registro COM:** Captura graciosa de `0x80040111 (CLASS_E_CLASSNOTAVAILABLE)` e `0x80040154 (CLASS_NOT_REGISTERED)` com orientações amigáveis ao usuário, impedindo o encerramento inesperado do PRDC e mantendo ativas as sessões Apache Guacamole e ferramentas de workspace.

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
