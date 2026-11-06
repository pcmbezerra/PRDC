using System;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PRDC;

/// <summary>
/// Representa uma entrada indexada no Catálogo Global de Busca (RFC-006).
/// Unifica servidores provenientes de arquivos RDG, portais Apache Guacamole e Favoritos Globais.
/// </summary>
internal sealed class GlobalSearchEntry
{
    /// <summary>
    /// Chave identificadora única no formato "{SourceId}:{NodePath ?? ConnectionIdentifier}".
    /// </summary>
    public required string EntryKey { get; init; }

    /// <summary>
    /// Identificador da origem documental (RdgFileSource ou GuacamoleInventorySource).
    /// </summary>
    public required Guid SourceId { get; init; }

    /// <summary>
    /// Nome de exibição da origem (ex.: "Datacenter.rdg", "Guac-Prod").
    /// </summary>
    public required string SourceDisplayName { get; init; }

    /// <summary>
    /// Tipo da origem documental.
    /// </summary>
    public required ConnectionSourceKind SourceKind { get; init; }

    /// <summary>
    /// Nome de exibição atual (ou CustomAlias caso seja um favorito renomeado).
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Nome original do servidor no documento ou portal.
    /// </summary>
    public string? OriginalDisplayName { get; init; }

    /// <summary>
    /// Endereço IP ou hostname DNS de rede (para conexões RDP nativas).
    /// </summary>
    public string? HostName { get; init; }

    /// <summary>
    /// Caminho hierárquico estruturado (ex.: "/Datacenter/Produção/SQL01").
    /// </summary>
    public required string NodePath { get; init; }

    /// <summary>
    /// Referência direta em memória ao elemento XML no documento da origem.
    /// </summary>
    [JsonIgnore]
    public required XElement ServerElement { get; init; }

    /// <summary>
    /// Indica se o servidor faz parte da coleção de Favoritos Globais.
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Apelido customizado atribuído ao favorito (se houver).
    /// </summary>
    public string? FavoriteAlias { get; set; }

    /// <summary>
    /// Identificador do favorito (se favoritado).
    /// </summary>
    public Guid? FavoriteId { get; set; }

    /// <summary>
    /// Índice de ordenação na lista de favoritos.
    /// </summary>
    public int? FavoriteOrderIndex { get; set; }

    /// <summary>
    /// Referência de conexão Guacamole (se proveniente de portal web).
    /// </summary>
    [JsonIgnore]
    public GuacamoleConnectionReference? GuacamoleReference { get; init; }

    /// <summary>
    /// Alvo de busca textual pré-normalizado em caixa baixa para avaliação sem alocação em runtime.
    /// </summary>
    public string NormalizedSearchTarget { get; private set; } = string.Empty;

    public string DisplayNameLower { get; private set; } = string.Empty;
    public string? OriginalDisplayNameLower { get; private set; }
    public string? FavoriteAliasLower { get; private set; }
    public string? HostNameLower { get; private set; }
    public string NodePathLower { get; private set; } = string.Empty;
    public string SourceDisplayNameLower { get; private set; } = string.Empty;

    /// <summary>
    /// Atualiza a cadeia de busca normalizada combinando todos os atributos pesquisáveis.
    /// </summary>
    public void UpdateSearchTarget()
    {
        DisplayNameLower = DisplayName.ToLowerInvariant();
        OriginalDisplayNameLower = OriginalDisplayName?.ToLowerInvariant();
        FavoriteAliasLower = FavoriteAlias?.ToLowerInvariant();
        HostNameLower = HostName?.ToLowerInvariant();
        NodePathLower = NodePath.ToLowerInvariant();
        SourceDisplayNameLower = SourceDisplayName.ToLowerInvariant();

        string raw = $"{DisplayName} {OriginalDisplayName} {FavoriteAlias} {HostName} {NodePath} {SourceDisplayName}";
        NormalizedSearchTarget = raw.ToLowerInvariant();
    }
}

