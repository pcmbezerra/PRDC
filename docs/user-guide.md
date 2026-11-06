# Guia do Usuário — PRDC (v1.4.0)

Bem-vindo ao Guia Oficial do Usuário do **PRDC (Remote Desktop Connection Manager)**. Este documento apresenta todas as funcionalidades, atalhos e fluxos operacionais para administração ágil de conexões remotas.

---

## 1. Tabela de Atalhos de Teclado

O PRDC foi projetado para permitir operação rápida orientada a teclado:

| Atalho | Ação | Contexto de Uso |
| :--- | :--- | :--- |
| **`Ctrl+P`** | Abrir Busca Global / Quick Open | Global em qualquer tela da aplicação |
| **`Ctrl+D`** | Adicionar / Remover dos Favoritos | Servidor selecionado na árvore de conexões |
| **`Ctrl+G`** | Ir para a Origem do Servidor | Nó selecionado dentro de `★ Favoritos` |
| **`F2`** | Renomear / Definir Apelido Amigável | Servidor, grupo ou favorito selecionado |
| **`Ctrl+N`** | Criar Novo Arquivo RDG | Menu Arquivo |
| **`Ctrl+O`** | Abrir Arquivo RDG no Workspace | Menu Arquivo |
| **`Ctrl+S`** | Salvar Documento Ativo | Menu Arquivo |
| **`Ctrl+Shift+S`** | Salvar Todos os Documentos Abertos | Menu Arquivo |
| **`Ctrl+Shift+G`** | Fazer Login no Apache Guacamole | Menu Arquivo |
| **`Ctrl+Shift+U`** | Atualizar Inventário Guacamole | Portal Guacamole selecionado |
| **`Ctrl+Shift+D`** | Desconectar Sessão Remota Ativa | Aba de sessão ativa |
| **`F11`** | Alternar Modo Tela Cheia | Global |
| **`Esc`** | Fechar Quick Open ou Sair de Tela Cheia | Overlay de busca ou tela cheia ativa |
| **`Enter`** | Conectar Imediatamente | Quick Open (`Ctrl+P`) |
| **`Ctrl+Enter`** | Localizar e Selecionar na Árvore | Quick Open (`Ctrl+P`) ou árvore |

---

## 2. Fluxos de Trabalho

### 2.1. Workspace Multiorigem
O PRDC opera sobre o conceito de **Workspace Multiorigem**, permitindo que múltiplos arquivos de infraestrutura coexistam na mesma janela.

- **Abrindo Múltiplas Origens:**
  - Use `Ctrl+O` para abrir um ou mais arquivos `.rdg` (ex.: `DataCenter_SP.rdg` e `Cloud_AWS.rdg`).
  - Cada arquivo aparece como uma raiz independente na árvore de conexões.
- **Salvamento Individual vs. Global:**
  - `Ctrl+S`: Salva exclusivamente as modificações do arquivo selecionado no momento.
  - `Ctrl+Shift+S`: Salva em lote todas as origens que possuem alterações pendentes (`*`).
- **Persistência Automática:**
  - Ao fechar o PRDC, a lista de arquivos abertos, portais Guacamole conectados, largura dos painéis e posição da janela são salvos atomicamente em `%LOCALAPPDATA%\PRDC\workspace.json`.
  - Ao reabrir a aplicação, todas as origens e preferências visuais são restauradas instantaneamente.
- **Recuperação de Falhas:**
  - Caso ocorra uma queda de energia ou fechamento inesperado durante o salvamento, o PRDC isola cópias corrompidas em quarentena (`workspace.json.corrupt.*`) e inicializa um novo workspace seguro sem travar o sistema.

---

### 2.2. Gestão de Favoritos Globais (`★ Favoritos`)
A coleção de favoritos permite acesso direto aos servidores críticos do seu dia a dia, mesmo que estejam distribuídos em arquivos ou portais diferentes.

- **Favoritando um Servidor:**
  - Localize o servidor na árvore (seja em um arquivo `.rdg` ou em um portal Guacamole).
  - Pressione `Ctrl+D` ou clique com o botão direito e escolha **Adicionar aos Favoritos**.
  - O servidor passa a figurar sob a raiz **★ Favoritos (n)** no topo da árvore.
- **Definindo Apelidos (`F2`):**
  - Selecione o favorito e pressione `F2`.
  - Digite um nome amigável (ex.: `Banco de Produção Primário`).
  - Esse apelido é exclusivo do seu favorito e não altera o arquivo `.rdg` original.
- **Navegando até a Origem (`Ctrl+G`):**
  - Para encontrar onde o servidor favorito está localizado fisicamente na estrutura da sua infraestrutura, selecione-o e pressione `Ctrl+G` (ou clique em **Ir para a Origem**). A árvore expande automaticamente e foca no nó original.
- **Tratamento de Órfãos:**
  - Se um arquivo `.rdg` for renomeado ou excluído do disco, o favorito correspondente é marcado com o indicador de alerta `(Origem ausente)` ou `(Não encontrado)`. O favorito não é apagado, permitindo recuperação posterior quando o arquivo for restabelecido.

---

### 2.3. Busca Global e Quick Open (`Ctrl+P`)
O Quick Open permite localizar e abrir qualquer conexão em microssegundos sem navegar manualmente por árvores complexas.

- **Pesquisa Inteligente Multitermo:**
  - Pressione `Ctrl+P` para abrir a caixa flutuante.
  - Digite termos parciais separados por espaço. Exemplos:
    - `banco prod`: localiza conexões que contenham "banco" e "prod" no nome ou no grupo.
    - `10.10.1`: localiza servidores pelo endereço IP ou DNS.
    - `guac linux`: localiza servidores Linux hospedados em portais Guacamole.
  - Servidores favoritados recebem prioridade máxima no ranqueamento e aparecem no topo.
- **Ações Imediatas:**
  - **Pressione `Enter`:** O PRDC inicia a conexão imediatamente com o host correto (ActiveX nativo ou Guacamole WebView2).
  - **Pressione `Ctrl+Enter`:** O Quick Open fecha, a árvore de conexões expande os grupos pai e seleciona visualmente o nó na lista.
  - **Pressione `Esc`:** Fecha a barra de pesquisa sem realizar nenhuma ação.

---

### 2.4. Integração Corporativa com Apache Guacamole
O PRDC conecta-se diretamente a servidores Apache Guacamole via API REST e executa sessões através do túnel `guacd` nativo em abas WebView2.

- **Conectando a um Portal Guacamole:**
  - Pressione `Ctrl+Shift+G` (ou acesse `Arquivo > Login no Guacamole...`).
  - Informe a URL base (ex.: `https://guac.minhaempresa.com.br/guacamole/`) e suas credenciais.
  - O PRDC importa automaticamente os grupos e conexões autorizados para o seu usuário.
- **Persistência de Cache e Acesso Offline:**
  - O inventário importado é gravado em cache transacional em `%LOCALAPPDATA%\PRDC\Portals\{portalId}\inventory.json`.
  - Nas próximas inicializações, o inventário é carregado instantaneamente do disco sem necessidade de conexão imediata com a rede.
- **Atualização de Inventário (`Ctrl+Shift+U`):**
  - Para sincronizar novos servidores criados no Guacamole, selecione o nó raiz do portal e pressione `Ctrl+Shift+U`.
- **Múltiplos Portais Concorrentes:**
  - Você pode conectar-se a múltiplos portais Guacamole distintos (ex.: Produção, Homologação e Nuvem). Cada portal opera com perfis de isolamento independentes.

---

### 2.5. Sessões Remotas e Customização Visual
- **Gerenciamento de Abas de Sessão:**
  - Cada conexão abre em uma aba dedicada.
  - O botão `×` na aba encerra a sessão individualmente.
  - Pressione `Ctrl+Shift+D` para desconectar a sessão em foco.
- **Modo Tela Cheia (`F11`):**
  - Ideal para sessões de longa duração. Pressione `F11` para ocultar os menus e barras de status do PRDC. Para retornar, pressione `Esc` ou `F11`.
- **Temas Claro e Escuro:**
  - Acesse o menu `Exibir > Tema` e escolha entre `Claro`, `Escuro` ou `Usar tema do Windows`. A aplicação adapta-se em tempo real sem necessidade de reinício.
