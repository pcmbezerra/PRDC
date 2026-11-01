<div align="center">

[English](../../README.md) · **Português (Brasil)**

<img src="../../src/PRDC/PRDC.png" alt="Logotipo do PRDC" width="180" />

# PRDC

### Gerenciador open source de conexões de Área de Trabalho Remota para Windows.

[![Build](https://github.com/pcmbezerra/PRDC/actions/workflows/build.yml/badge.svg)](https://github.com/pcmbezerra/PRDC/actions/workflows/build.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![Licença MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-f4c430?style=flat-square)](../../LICENSE)

**[Início rápido](#início-rápido) · [Funcionalidades](#funcionalidades) · [Documentação](#documentação) · [Como contribuir](../../CONTRIBUTING.md)**

</div>

> [!IMPORTANT]
> A fonte oficial do PRDC é [pcmbezerra/PRDC](https://github.com/pcmbezerra/PRDC).
> O PRDC é um projeto independente, sem afiliação ou endosso da Microsoft.

O PRDC organiza endpoints de Área de Trabalho Remota em arquivos `.rdg` compatíveis com o RDCMan e abre sessões dentro de uma interface nativa do Windows. Conexões `.rdg` tradicionais utilizam diretamente o controle ActiveX de Área de Trabalho Remota do Windows (`mstscax.dll`), enquanto conexões importadas de um portal Apache Guacamole são abertas dentro das abas de sessão do PRDC via Microsoft Edge WebView2 utilizando o frontend oficial do Guacamole e `guacd`.

## Funcionalidades

- Criar, abrir, editar e salvar arquivos `.rdg`.
- Realizar login e importar grupos e conexões de servidores RDP a partir de um portal Apache Guacamole ("Login no Guacamole..." / `Ctrl+Shift+G`) e abrir essas sessões dentro de abas do PRDC via Microsoft Edge WebView2.
- Manter conexões `.rdg` tradicionais utilizando `mstscax.dll`.
- Organizar servidores em grupos aninhados.
- Conectar simultaneamente a vários hosts em abas de sessão independentes.
- Reconectar, desconectar, fazer logoff em sessões RDP nativas sem bloquear a interface ou fechar uma aba de sessão pelo botão `×`.
- Exibir indicadores visuais por cor (vermelho quando desconectado, amarelo durante autenticação ou abertura do túnel Guacamole, verde para a sessão autenticada ativa e azul para sessões autenticadas em segundo plano) tanto na árvore de servidores quanto nas abas de sessão.
- Ajustar automaticamente a largura da lista de conexões aos itens visíveis na árvore e expandir as abas de sessão para exibir o nome completo do servidor enquanto houver espaço disponível.
- Manter uma miniatura atualizada de cada sessão RDP nativa na tela de detalhes.
- Mostrar, ocultar ou ocultar automaticamente a lista de conexões do RDG.
- Redimensionar automaticamente a resolução remota junto com a janela do aplicativo (suportado tanto em sessões RDP nativas ActiveX quanto em sessões WebView2 do Apache Guacamole via debounce e sensores nativos de redimensionamento).
- Usar os diálogos nativos de credenciais e certificados do Windows para conexões `.rdg` e o portal oficial do Guacamole via WebView2 para sessões Guacamole.
- Habilitar CredSSP, autenticação do servidor, clipboard e Smart Sizing nas sessões RDP nativas.
- Acompanhar automaticamente o tema claro ou escuro do Windows.
- Preservar elementos XML ainda não interpretados ao editar um RDG existente.

## Início rápido

### Requisitos

- Windows 10 ou Windows 11 x64.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) para executar a versão framework-dependent padrão (um pacote self-contained experimental, pendente de validação em uma instalação do Windows sem o .NET 10 Desktop Runtime, também está disponível).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) para compilar o código-fonte.
- Microsoft Edge WebView2 Evergreen Runtime (x64) para abrir sessões do Apache Guacamole dentro do PRDC.
- Acesso de rede e autorização para o servidor RDP de destino ou portal Apache Guacamole.

### Compilar e executar

```powershell
git clone https://github.com/pcmbezerra/PRDC.git
cd PRDC
dotnet restore PRDC.sln
dotnet run --project src/PRDC/PRDC.csproj
```

Crie ou abra um `.rdg`, selecione um servidor e dê um duplo clique ou pressione `Ctrl+Enter`. Para desconectar, use `Ctrl+Shift+D`.

## Integração com Apache Guacamole

O PRDC oferece integração nativa com portais Apache Guacamole, permitindo centralizar a organização, a persistência local e o acesso remoto a servidores RDP através do `guacd`.

| Recurso | Status |
| :--- | :--- |
| Login Guacamole | ✅ |
| Cache Local | ✅ |
| Atualização de Inventário | ✅ |
| Exportação RDG | ✅ |
| Sessões WebView2 | ✅ |
| SSH | ❌ |
| VNC | ❌ |

### Visão Geral

Os usuários podem se autenticar diretamente em um servidor Apache Guacamole para descobrir fontes de dados e importar todos os servidores RDP autorizados. A hierarquia de conexões é sincronizada, persistida localmente para operação offline e executada em abas de sessão aceleradas via Microsoft Edge WebView2.

### Fluxo de Importação e Persistência

1. **Login Direto**: Selecione `Arquivo > Login no Guacamole...` (`Ctrl+Shift+G`) e informe a URL do portal, usuário e senha.
2. **Descoberta e Mapeamento de Hierarquia**: O PRDC consulta a API REST do Guacamole em todos os datasources disponíveis, extrai os grupos aninhados e conexões RDP, resolvendo nomes e hostnames em um documento RDCMan em memória.
3. **Persistência em Cache Local**: O inventário importado é gravado automaticamente no cache canônico em `%LOCALAPPDATA%\PRDC\Guacamole\inventory.json` através de pipeline transacional atômico (UTF-8 sem BOM, escrita direta em hardware com `WriteThrough`, revalidação de integridade e `File.Replace`).
4. **Operação Offline no Startup**: Nas próximas inicializações, o PRDC detecta e carrega o inventário em milissegundos a partir do disco, sem necessidade de rede, chamadas HTTP ou telas de login.
5. **Atualização e Remoção de Inventário**:
   - `Arquivo > Atualizar lista do Guacamole` (`Ctrl+Shift+U`): Reautentica no portal com a URL base preenchida e atualiza atomicamente o cache local e a árvore.
   - `Arquivo > Remover inventário do Guacamole...`: Exclui com segurança o cache local e reseta a árvore, sem afetar o perfil de navegação do WebView2.
6. **Exportação Opcional para RDG**:
   - O usuário pode exportar o inventário ativo para um arquivo `.rdg` independente via `Arquivo > Salvar como...`.
   - O atalho `Ctrl+S` / `Salvar` **nunca** sobrescreve o cache local (`inventory.json`).
   - Durante a execução atual, as conexões exportadas mantêm o vínculo com o Guacamole; quando o arquivo `.rdg` for reaberto em uma execução futura, será tratado como documento tradicional (`mstscax.dll`).
7. **Execução de Sessões via WebView2**:
   - Conexões Guacamole operam dentro de abas dedicadas via WebView2, conectando-se diretamente ao cliente web oficial e ao túnel `guacd`.
   - O redimensionamento dinâmico da resolução remota ajusta a tela automaticamente ao tamanho da janela usando eventos de sensor do DOM com debounce de 250 ms, sem reconectar ou interromper o túnel.

### Características

- Login direto no Guacamole
- Importação automática de grupos e conexões
- Cache local para operação offline
- Atualização manual do inventário
- Exportação opcional para RDCMan (.rdg)
- Sessões executadas via WebView2

### Limitações

- Suporte somente para conexões RDP (conexões SSH e VNC são ignoradas na importação)
- Credenciais nunca são persistidas (senhas, tokens e cookies de autenticação não vão para o disco)
- Cache armazena apenas metadados necessários do inventário
- Arquivos exportados RDG são tratados como documentos tradicionais quando reabertos (`mstscax.dll`)

## Compatibilidade RDG e metadados de sessão Guacamole

O PRDC implementa as partes de arquivo, grupo, servidor, nome de exibição, hostname e estado de expansão atualmente usadas pelo aplicativo. Elementos XML desconhecidos são preservados durante leitura e gravação, mas não há garantia de paridade com todas as configurações do RDCMan. Consulte [Compatibilidade RDG](../rdg-format.md).

Os metadados de conexão do Apache Guacamole (`BaseUri`, `DataSource` e `ConnectionIdentifier`) existem apenas em memória durante a sessão importada atual e não são persistidos no XML `.rdg`. Após salvar e reabrir um arquivo `.rdg` importado do Guacamole, o vínculo Guacamole não é preservado: os nós passam a ser tratados como servidores `.rdg` tradicionais (`mstscax.dll`), e o acesso RDP direto pode falhar em redes segmentadas nas quais os servidores só são alcançáveis via `guacd`.

Não salve nem faça commit de arquivos `.rdg` reais contendo hostnames internos, usuários, credenciais criptografadas ou outros metadados de infraestrutura.

## Segurança e privacidade

O PRDC não persiste diretamente senhas, `authToken`, cookies ou credenciais RDP. Para sessões `.rdg` tradicionais, a coleta de credenciais é conduzida pelo controle RDP nativo do Windows (`mstscax.dll`). Para sessões do Apache Guacamole, o WebView2 utiliza um perfil local em `%LOCALAPPDATA%\PRDC\WebView2\` inicializado com `--no-proxy-server`; esse perfil do WebView2 pode persistir cookies, `localStorage` (como `GUAC_AUTH`), cache e permissões entre abas e execuções. As permissões de câmera, microfone, geolocalização e notificações permanecem bloqueadas no MVP. As permissões da área de transferência permanecem sob a política padrão do WebView2; o PRDC não concede automaticamente acesso de leitura ao clipboard.

## Documentação

- [Índice da documentação](../index.md)
- [Arquitetura](../architecture.md)
- [Desenvolvimento](../development.md)
- [Compatibilidade RDG](../rdg-format.md)
- [Política de segurança](../../SECURITY.md)
- [Como contribuir](../../CONTRIBUTING.md)
- [Histórico de alterações](../../CHANGELOG.md)

## Origem e independência

O desenvolvimento inicial foi orientado por análise comportamental estática do Remote Desktop Connection Manager. O PRDC possui implementação independente e não distribui o RDCMan nem `mstscax.dll`.

## Licença

PRDC é disponibilizado sob a [Licença MIT](../../LICENSE).

## Autor

[Paulo Bezerra (@pcmbezerra)](https://github.com/pcmbezerra)
