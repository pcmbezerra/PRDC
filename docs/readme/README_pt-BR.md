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

O PRDC organiza endpoints de Área de Trabalho Remota em arquivos `.rdg` compatíveis com o RDCMan e abre sessões RDP dentro de uma interface nativa do Windows. O aplicativo utiliza o controle ActiveX de Área de Trabalho Remota já fornecido pelo sistema operacional.

## Funcionalidades

- Criar, abrir, editar e salvar arquivos `.rdg`.
- Organizar servidores em grupos aninhados.
- Conectar e desconectar uma sessão RDP incorporada por vez.
- Exibir na árvore os estados desconectado, conectando, conectado e login concluído.
- Manter uma miniatura atualizada da sessão remota ativa na tela de detalhes.
- Redimensionar automaticamente a resolução remota junto com a janela do aplicativo.
- Usar os diálogos nativos de credenciais e certificados do Windows.
- Habilitar CredSSP, autenticação do servidor, clipboard e Smart Sizing.
- Acompanhar automaticamente o tema claro ou escuro do Windows.
- Preservar elementos XML ainda não interpretados ao editar um RDG existente.
- Executar sem pacotes NuGet externos.

## Início rápido

### Requisitos

- Windows 10 ou Windows 11 x64.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) para executar uma versão compilada.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) para compilar o código-fonte.
- Acesso de rede e autorização para o servidor RDP de destino.

### Compilar e executar

```powershell
git clone https://github.com/pcmbezerra/PRDC.git
cd PRDC
dotnet restore PRDC.sln
dotnet run --project src/PRDC/PRDC.csproj
```

Crie ou abra um `.rdg`, selecione um servidor e dê um duplo clique ou pressione `Ctrl+Enter`. Para desconectar, use `Ctrl+Shift+D`.

## Compatibilidade RDG

O PRDC implementa as partes de arquivo, grupo, servidor, nome de exibição, hostname e estado de expansão atualmente usadas pelo aplicativo. Elementos XML desconhecidos são preservados durante leitura e gravação, mas não há garantia de paridade com todas as configurações do RDCMan. Consulte [Compatibilidade RDG](../rdg-format.md).

Não publique arquivos `.rdg` contendo hostnames internos, usuários, credenciais criptografadas ou outros metadados de infraestrutura.

## Documentação

- [Índice da documentação](../index.md)
- [Arquitetura](../architecture.md)
- [Desenvolvimento](../development.md)
- [Compatibilidade RDG](../rdg-format.md)
- [Política de segurança](../../SECURITY.md)
- [Como contribuir](../../CONTRIBUTING.md)
- [Histórico de alterações](../../CHANGELOG.md)

## Origem e independência

O desenvolvimento inicial foi orientado por análise comportamental estática do Remote Desktop Connection Manager com o [REA](https://github.com/morluto/rea). O REA é uma ferramenta de desenvolvimento e não é dependência de execução. O PRDC possui implementação independente e não distribui o RDCMan nem `mstscax.dll`.

## Licença

PRDC é disponibilizado sob a [Licença MIT](../../LICENSE).

## Autor

[Paulo Bezerra (@pcmbezerra)](https://github.com/pcmbezerra)
