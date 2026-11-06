using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PRDC;

/// <summary>
/// Gerenciador do Catálogo Global de Busca (RFC-006 Backend).
/// Responsável pela indexação particionada por origem, atualizações incrementais,
/// cálculo de relevância (score) com prioridade para favoritos e seleção Top-K via Min-Heap.
/// </summary>
internal sealed class SearchCatalogManager
{
    private readonly object _lock = new();

    // Partições de entradas indexadas por origem (SourceId -> Lista de Entradas)
    private readonly Dictionary<Guid, List<GlobalSearchEntry>> _entriesBySource = [];

    // Mapeamento direto de chave única para busca O(1) e atualizações rápidas
    private readonly Dictionary<string, GlobalSearchEntry> _entryByKey = new(StringComparer.Ordinal);

    // Lista plana cacheada para varreduras de busca lineares de alta velocidade
    private List<GlobalSearchEntry> _flatEntries = [];
    private bool _isDirty;

    /// <summary>
    /// Total de servidores indexados no catálogo global.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entryByKey.Count;
            }
        }
    }

    /// <summary>
    /// Total de servidores favoritados no catálogo.
    /// </summary>
    public int FavoritesCount
    {
        get
        {
            lock (_lock)
            {
                EnsureFlatListLocked();
                return _flatEntries.Count(e => e.IsFavorite);
            }
        }
    }

    /// <summary>
    /// Constrói o catálogo completo a partir das origens fornecidas e vincula favoritos existentes.
    /// </summary>
    public void BuildCatalog(IEnumerable<IConnectionSource> sources, FavoriteManager? favoriteManager = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        lock (_lock)
        {
            _entriesBySource.Clear();
            _entryByKey.Clear();
            _flatEntries.Clear();
            _isDirty = true;

            foreach (IConnectionSource source in sources)
            {
                IndexSourceInternal(source);
            }

            if (favoriteManager is not null)
            {
                foreach (FavoriteEntry fav in favoriteManager.Favorites)
                {
                    UpdateFavoriteInternal(fav, isRemoved: false);
                }
            }

            EnsureFlatListLocked();
        }
    }

    /// <summary>
    /// Indexa incrementalmente uma nova origem adicionada ao workspace.
    /// </summary>
    public void IndexSource(IConnectionSource source, FavoriteManager? favoriteManager = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_lock)
        {
            // Remove eventual partição anterior se já existir
            RemoveSourceInternal(source.Id);

            IndexSourceInternal(source);

            if (favoriteManager is not null)
            {
                foreach (FavoriteEntry fav in favoriteManager.Favorites.Where(f => f.SourceId == source.Id))
                {
                    UpdateFavoriteInternal(fav, isRemoved: false);
                }
            }

            _isDirty = true;
        }
    }

    /// <summary>
    /// Reconstrói incrementalmente os servidores de uma única origem (ex.: atualização de portal Guacamole).
    /// </summary>
    public void RebuildSource(IConnectionSource source, FavoriteManager? favoriteManager = null)
    {
        IndexSource(source, favoriteManager);
    }

    /// <summary>
    /// Remove incrementalmente todos os servidores associados à origem descarregada/fechada.
    /// </summary>
    public void RemoveSource(Guid sourceId)
    {
        lock (_lock)
        {
            RemoveSourceInternal(sourceId);
            _isDirty = true;
        }
    }

    /// <summary>
    /// Atualiza em O(1) o status, apelido ou remoção de um favorito sem reconstruir o catálogo.
    /// </summary>
    public void UpdateFavorite(FavoriteEntry favorite, bool isRemoved = false)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        lock (_lock)
        {
            UpdateFavoriteInternal(favorite, isRemoved);
        }
    }

    /// <summary>
    /// Atualiza em lote uma coleção de favoritos no catálogo.
    /// </summary>
    public void UpdateFavorites(IEnumerable<FavoriteEntry> favorites)
    {
        ArgumentNullException.ThrowIfNull(favorites);

        lock (_lock)
        {
            foreach (FavoriteEntry fav in favorites)
            {
                UpdateFavoriteInternal(fav, isRemoved: false);
            }
        }
    }

    /// <summary>
    /// Obtém uma entrada pelo identificador único (se existir).
    /// </summary>
    public GlobalSearchEntry? GetEntry(string entryKey)
    {
        ArgumentNullException.ThrowIfNull(entryKey);

        lock (_lock)
        {
            return _entryByKey.TryGetValue(entryKey, out var entry) ? entry : null;
        }
    }

    /// <summary>
    /// Enumera todas as entradas indexadas no catálogo.
    /// </summary>
    public IReadOnlyList<GlobalSearchEntry> EnumerateEntries()
    {
        lock (_lock)
        {
            EnsureFlatListLocked();
            return _flatEntries.ToList();
        }
    }

    /// <summary>
    /// Limpa integralmente todas as entradas do catálogo.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entriesBySource.Clear();
            _entryByKey.Clear();
            _flatEntries.Clear();
            _isDirty = false;
        }
    }

    /// <summary>
    /// Executa a busca global com ranqueamento e retorno dos Top-K melhores resultados.
    /// Caso a consulta seja vazia, retorna os favoritos ordenados pelo índice de preferência.
    /// </summary>
    public IReadOnlyList<GlobalSearchResult> Search(string? query, int limit = 20)
    {
        if (limit <= 0) limit = 20;

        lock (_lock)
        {
            EnsureFlatListLocked();

            // Consulta vazia: retorna favoritos diretamente como sugestões rápidas
            if (string.IsNullOrWhiteSpace(query))
            {
                return _flatEntries
                    .Where(e => e.IsFavorite)
                    .OrderBy(e => e.FavoriteOrderIndex ?? int.MaxValue)
                    .Take(limit)
                    .Select(e => new GlobalSearchResult
                    {
                        Entry = e,
                        Score = 10000 - (e.FavoriteOrderIndex ?? 0)
                    })
                    .ToList();
            }

            string rawQuery = query.Trim();
            string[] terms = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (terms.Length == 0)
            {
                return Array.Empty<GlobalSearchResult>();
            }

            for (int i = 0; i < terms.Length; i++)
            {
                terms[i] = terms[i].ToLowerInvariant();
            }

            string qLower = rawQuery.ToLowerInvariant();

            // Min-Heap de tamanho K para garantir complexidade O(N log K)
            var minHeap = new PriorityQueue<(GlobalSearchEntry Entry, int Score), int>();

            for (int i = 0; i < _flatEntries.Count; i++)
            {
                GlobalSearchEntry candidate = _flatEntries[i];
                string target = candidate.NormalizedSearchTarget;

                // AND lógico: todos os termos digitados devem existir no alvo de busca
                bool allTermsMatch = true;
                for (int t = 0; t < terms.Length; t++)
                {
                    if (!target.Contains(terms[t], StringComparison.Ordinal))
                    {
                        allTermsMatch = false;
                        break;
                    }
                }

                if (!allTermsMatch) continue;

                int score = CalculateScore(candidate, rawQuery, qLower, terms);

                if (minHeap.Count < limit)
                {
                    minHeap.Enqueue((candidate, score), score);
                }
                else if (score > minHeap.Peek().Score)
                {
                    minHeap.Dequeue();
                    minHeap.Enqueue((candidate, score), score);
                }
            }

            if (minHeap.Count == 0)
            {
                return Array.Empty<GlobalSearchResult>();
            }

            List<GlobalSearchResult> results = new(minHeap.Count);
            while (minHeap.Count > 0)
            {
                var item = minHeap.Dequeue();
                results.Add(new GlobalSearchResult { Entry = item.Entry, Score = item.Score });
            }

            // Ordena o Top-K decrescente por score, desempatando por DisplayName e Origem
            results.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.Entry.DisplayName, b.Entry.DisplayName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return string.Compare(a.Entry.SourceDisplayName, b.Entry.SourceDisplayName, StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }
    }

    /// <summary>
    /// Calcula a pontuação (score) de relevância para um candidato.
    /// Prioridade: Favorito > Match Exato > Prefixo > Substring > HostName > NodePath > SourceName.
    /// </summary>
    internal static int CalculateScore(GlobalSearchEntry entry, string rawQuery, string qLower, string[] terms)
    {
        int score = 0;

        // 1. Tier 1: Bônus de Favorito (+10.000)
        if (entry.IsFavorite)
        {
            score += 10000;
        }

        string name = entry.DisplayNameLower;
        string? origName = entry.OriginalDisplayNameLower;
        string? alias = entry.FavoriteAliasLower;
        string? host = entry.HostNameLower;
        string path = entry.NodePathLower;
        string source = entry.SourceDisplayNameLower;

        // 2. Correspondência no Nome / Apelido
        if (name == qLower || (origName is not null && origName == qLower) || (alias is not null && alias == qLower))
        {
            score += 1000; // Match Exato
        }
        else if (name.StartsWith(qLower, StringComparison.Ordinal)
            || (origName is not null && origName.StartsWith(qLower, StringComparison.Ordinal))
            || (alias is not null && alias.StartsWith(qLower, StringComparison.Ordinal)))
        {
            score += 600; // Prefixo
        }
        else if (name.Contains(qLower, StringComparison.Ordinal)
            || (origName is not null && origName.Contains(qLower, StringComparison.Ordinal))
            || (alias is not null && alias.Contains(qLower, StringComparison.Ordinal)))
        {
            score += 400; // Substring
        }

        // 3. Correspondência no HostName (IP / DNS)
        if (!string.IsNullOrEmpty(host))
        {
            if (host == qLower || host.StartsWith(qLower, StringComparison.Ordinal))
            {
                score += 300;
            }
            else if (host.Contains(qLower, StringComparison.Ordinal))
            {
                score += 200;
            }
        }

        // 4. Correspondência no NodePath (Caminho de grupos)
        if (path.Contains(qLower, StringComparison.Ordinal))
        {
            score += 100;
        }

        // 5. Correspondência no SourceDisplayName (Nome do arquivo ou portal)
        if (source.Contains(qLower, StringComparison.Ordinal))
        {
            score += 50;
        }

        // 6. Avaliação Multi-termo
        if (terms.Length > 1)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                string t = terms[i];
                if (name.Contains(t, StringComparison.Ordinal)) score += 200;
                else if (origName is not null && origName.Contains(t, StringComparison.Ordinal)) score += 200;
                else if (host is not null && host.Contains(t, StringComparison.Ordinal)) score += 100;
                else if (path.Contains(t, StringComparison.Ordinal)) score += 50;
                else if (source.Contains(t, StringComparison.Ordinal)) score += 30;
            }
        }

        // 7. Penalidade de Tamanho (Favorece correspondências mais justas e curtas)
        int lengthDiff = Math.Max(0, entry.DisplayName.Length - rawQuery.Length);
        score -= Math.Min(50, lengthDiff);

        return score;
    }

    private void IndexSourceInternal(IConnectionSource source)
    {
        List<GlobalSearchEntry> sourceEntries = [];

        if (source is RdgFileSource rdgSource)
        {
            foreach (RdgNode root in rdgSource.Roots)
            {
                foreach (XElement serverElement in root.Element.DescendantsAndSelf().Where(e => e.Name.LocalName == "server"))
                {
                    string nodePath = ServerLocator.BuildNodePath(serverElement);
                    string hostName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "name")?.Value?.Trim() ?? string.Empty;
                    string displayName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "displayName")?.Value?.Trim() ?? hostName;
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = "Servidor";

                    string entryKey = $"{rdgSource.Id}:{nodePath}";

                    GlobalSearchEntry entry = new()
                    {
                        EntryKey = entryKey,
                        SourceId = rdgSource.Id,
                        SourceDisplayName = rdgSource.DisplayName,
                        SourceKind = ConnectionSourceKind.RdgFile,
                        DisplayName = displayName,
                        OriginalDisplayName = displayName,
                        HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName,
                        NodePath = nodePath,
                        ServerElement = serverElement
                    };
                    entry.UpdateSearchTarget();

                    sourceEntries.Add(entry);
                    _entryByKey[entryKey] = entry;
                }
            }
        }
        else if (source is GuacamoleInventorySource guacSource)
        {
            // Extrai servidores referenciados no inventário Guacamole
            HashSet<XElement> indexedElements = [];

            foreach (var kvp in guacSource.GuacamoleReferences)
            {
                XElement serverElement = kvp.Key;
                GuacamoleConnectionReference reference = kvp.Value;
                string nodePath = "/" + reference.DisplayName;
                string entryKey = $"{guacSource.Id}:{reference.ConnectionIdentifier}";
                string? hostName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "name")?.Value?.Trim();

                GlobalSearchEntry entry = new()
                {
                    EntryKey = entryKey,
                    SourceId = guacSource.Id,
                    SourceDisplayName = guacSource.DisplayName,
                    SourceKind = ConnectionSourceKind.GuacamoleInventory,
                    DisplayName = reference.DisplayName,
                    OriginalDisplayName = reference.DisplayName,
                    HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName,
                    NodePath = nodePath,
                    ServerElement = serverElement,
                    GuacamoleReference = reference
                };
                entry.UpdateSearchTarget();

                sourceEntries.Add(entry);
                _entryByKey[entryKey] = entry;
                indexedElements.Add(serverElement);
            }

            // Fallback para nós <server> do XML que porventura não tenham referência
            foreach (RdgNode root in guacSource.Roots)
            {
                foreach (XElement serverElement in root.Element.DescendantsAndSelf().Where(e => e.Name.LocalName == "server"))
                {
                    if (indexedElements.Contains(serverElement)) continue;

                    string nodePath = ServerLocator.BuildNodePath(serverElement);
                    string hostName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "name")?.Value?.Trim() ?? string.Empty;
                    string displayName = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "displayName")?.Value?.Trim() ?? hostName;
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = "Servidor";

                    string entryKey = $"{guacSource.Id}:{nodePath}";

                    GlobalSearchEntry entry = new()
                    {
                        EntryKey = entryKey,
                        SourceId = guacSource.Id,
                        SourceDisplayName = guacSource.DisplayName,
                        SourceKind = ConnectionSourceKind.GuacamoleInventory,
                        DisplayName = displayName,
                        OriginalDisplayName = displayName,
                        HostName = string.IsNullOrWhiteSpace(hostName) ? null : hostName,
                        NodePath = nodePath,
                        ServerElement = serverElement
                    };
                    entry.UpdateSearchTarget();

                    sourceEntries.Add(entry);
                    _entryByKey[entryKey] = entry;
                }
            }
        }

        _entriesBySource[source.Id] = sourceEntries;
    }

    private void RemoveSourceInternal(Guid sourceId)
    {
        if (_entriesBySource.Remove(sourceId, out var removedList))
        {
            foreach (GlobalSearchEntry entry in removedList)
            {
                _entryByKey.Remove(entry.EntryKey);
            }
        }
    }

    private void UpdateFavoriteInternal(FavoriteEntry favorite, bool isRemoved)
    {
        string directKey = favorite.SourceType == "RdgFile"
            ? $"{favorite.SourceId}:{favorite.Locator.NodePath}"
            : $"{favorite.SourceId}:{favorite.Locator.ConnectionIdentifier}";

        if (!_entryByKey.TryGetValue(directKey, out var entry))
        {
            // Fallback para localização resiliente caso caminho tenha mudado
            if (_entriesBySource.TryGetValue(favorite.SourceId, out var sourceEntries))
            {
                entry = sourceEntries.FirstOrDefault(e =>
                    (!string.IsNullOrEmpty(favorite.Locator.HostName) && string.Equals(e.HostName, favorite.Locator.HostName, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(e.OriginalDisplayName, favorite.OriginalDisplayName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.NodePath, favorite.Locator.NodePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (entry is not null)
        {
            if (isRemoved)
            {
                entry.IsFavorite = false;
                entry.FavoriteAlias = null;
                entry.FavoriteId = null;
                entry.FavoriteOrderIndex = null;
                entry.DisplayName = entry.OriginalDisplayName ?? entry.DisplayName;
            }
            else
            {
                entry.IsFavorite = true;
                entry.FavoriteAlias = favorite.CustomAlias;
                entry.FavoriteId = favorite.FavoriteId;
                entry.FavoriteOrderIndex = favorite.OrderIndex;
                entry.DisplayName = favorite.EffectiveDisplayName;
            }

            entry.UpdateSearchTarget();
        }
    }

    private void EnsureFlatListLocked()
    {
        if (!_isDirty) return;

        int total = 0;
        foreach (var list in _entriesBySource.Values)
        {
            total += list.Count;
        }

        _flatEntries = new List<GlobalSearchEntry>(total);
        foreach (var list in _entriesBySource.Values)
        {
            _flatEntries.AddRange(list);
        }

        _isDirty = false;
    }
}
