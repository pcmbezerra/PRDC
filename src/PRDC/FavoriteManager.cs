using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace PRDC;

internal sealed class FavoriteManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly List<FavoriteEntry> _favorites = [];

    public IReadOnlyList<FavoriteEntry> Favorites => _favorites;

    public FavoriteManager() { }

    public FavoriteManager(IEnumerable<FavoriteEntry>? initialFavorites)
    {
        if (initialFavorites is not null)
        {
            _favorites.AddRange(initialFavorites.OrderBy(f => f.OrderIndex));
            NormalizeOrderIndexes();
        }
    }

    public FavoriteEntry AddFavorite(IConnectionSource source, XElement serverElement, string? customAlias = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(serverElement);

        string sourceType;
        Guid sourceId = source.Id;
        ServerLocator locator;
        string originalDisplayName;

        if (source is RdgFileSource rdgSource)
        {
            sourceType = "RdgFile";
            locator = ServerLocator.FromRdgServer(rdgSource.FilePath, serverElement);
            originalDisplayName = locator.OriginalDisplayName ?? "Servidor";
        }
        else if (source is GuacamoleInventorySource guacSource)
        {
            sourceType = "GuacamolePortal";
            Guid portalId = guacSource.PortalId ?? guacSource.Id;
            if (guacSource.GuacamoleReferences.TryGetValue(serverElement, out var reference))
            {
                locator = ServerLocator.FromGuacamoleReference(portalId, reference);
                originalDisplayName = reference.DisplayName;
            }
            else
            {
                string host = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "name")?.Value?.Trim() ?? string.Empty;
                string disp = RdgNode.FindChild(RdgNode.FindChild(serverElement, "properties"), "displayName")?.Value?.Trim() ?? host;
                locator = new ServerLocator
                {
                    PortalId = portalId,
                    OriginalDisplayName = disp
                };
                originalDisplayName = disp;
            }
        }
        else
        {
            throw new NotSupportedException($"Tipo de fonte '{source.GetType().Name}' não suportado para favoritos.");
        }

        // Verifica duplicidade para o mesmo servidor
        FavoriteEntry? existing = FindExistingFavorite(sourceType, sourceId, locator);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(customAlias))
            {
                existing.CustomAlias = customAlias.Trim();
            }
            return existing;
        }

        FavoriteEntry newEntry = new()
        {
            FavoriteId = Guid.NewGuid(),
            SourceId = sourceId,
            SourceType = sourceType,
            OriginalDisplayName = originalDisplayName,
            CustomAlias = string.IsNullOrWhiteSpace(customAlias) ? null : customAlias.Trim(),
            OrderIndex = _favorites.Count,
            CreatedUtc = DateTime.UtcNow,
            Locator = locator
        };

        _favorites.Add(newEntry);
        return newEntry;
    }

    public void AddFavorite(FavoriteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_favorites.Any(f => f.FavoriteId == entry.FavoriteId))
        {
            return;
        }

        entry.OrderIndex = _favorites.Count;
        _favorites.Add(entry);
    }

    public bool RemoveFavorite(Guid favoriteId)
    {
        int index = _favorites.FindIndex(f => f.FavoriteId == favoriteId);
        if (index < 0)
        {
            return false;
        }

        _favorites.RemoveAt(index);
        NormalizeOrderIndexes();
        return true;
    }

    public bool RenameFavorite(Guid favoriteId, string? newAlias)
    {
        FavoriteEntry? entry = _favorites.FirstOrDefault(f => f.FavoriteId == favoriteId);
        if (entry is null)
        {
            return false;
        }

        entry.CustomAlias = string.IsNullOrWhiteSpace(newAlias) ? null : newAlias.Trim();
        return true;
    }

    public bool ReorderFavorite(Guid favoriteId, int newIndex)
    {
        int currentIndex = _favorites.FindIndex(f => f.FavoriteId == favoriteId);
        if (currentIndex < 0)
        {
            return false;
        }

        int targetIndex = Math.Clamp(newIndex, 0, _favorites.Count - 1);
        if (currentIndex == targetIndex)
        {
            return true;
        }

        FavoriteEntry item = _favorites[currentIndex];
        _favorites.RemoveAt(currentIndex);
        _favorites.Insert(targetIndex, item);
        NormalizeOrderIndexes();
        return true;
    }

    public FavoriteResolutionResult ResolveFavorite(FavoriteEntry favorite, IReadOnlyList<IConnectionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        ArgumentNullException.ThrowIfNull(sources);

        // 1. Resolução da Origem (Source)
        IConnectionSource? matchedSource = null;

        if (string.Equals(favorite.SourceType, "RdgFile", StringComparison.OrdinalIgnoreCase))
        {
            matchedSource = sources.OfType<RdgFileSource>()
                .FirstOrDefault(s => s.Id == favorite.SourceId);

            if (matchedSource is null && !string.IsNullOrWhiteSpace(favorite.Locator.SourcePath))
            {
                matchedSource = sources.OfType<RdgFileSource>()
                    .FirstOrDefault(s => string.Equals(s.FilePath, favorite.Locator.SourcePath, StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (string.Equals(favorite.SourceType, "GuacamolePortal", StringComparison.OrdinalIgnoreCase))
        {
            matchedSource = sources.OfType<GuacamoleInventorySource>()
                .FirstOrDefault(s => s.Id == favorite.SourceId);

            if (matchedSource is null && favorite.Locator.PortalId.HasValue)
            {
                matchedSource = sources.OfType<GuacamoleInventorySource>()
                    .FirstOrDefault(s => s.PortalId == favorite.Locator.PortalId);
            }
        }

        if (matchedSource is null)
        {
            return FavoriteResolutionResult.MissingSourceResult("Origem não encontrada no workspace atual.");
        }

        if (matchedSource is RdgFileSource { IsMissing: true } || matchedSource is GuacamoleInventorySource { IsMissing: true })
        {
            return FavoriteResolutionResult.MissingSourceResult("Origem marcada como inacessível no disco.", matchedSource);
        }

        // 2. Resolução do Servidor na Origem Encontrada
        if (matchedSource is RdgFileSource rdgSource)
        {
            return ResolveRdgServer(favorite, rdgSource);
        }

        if (matchedSource is GuacamoleInventorySource guacSource)
        {
            return ResolveGuacamoleServer(favorite, guacSource);
        }

        return FavoriteResolutionResult.MissingNodeResult(matchedSource, "Tipo de origem incompatível para resolução de servidor.");
    }

    public IReadOnlyList<(FavoriteEntry Favorite, FavoriteResolutionResult Result)> DetectOrphanFavorites(IReadOnlyList<IConnectionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        List<(FavoriteEntry, FavoriteResolutionResult)> orphans = [];
        foreach (FavoriteEntry fav in _favorites)
        {
            var res = ResolveFavorite(fav, sources);
            if (res.Status != FavoriteOrphanStatus.Valid)
            {
                orphans.Add((fav, res));
            }
        }

        return orphans;
    }

    public void Clear()
    {
        _favorites.Clear();
    }

    private FavoriteResolutionResult ResolveRdgServer(FavoriteEntry favorite, RdgFileSource rdgSource)
    {
        List<XElement> allServers = GetAllServerElements(rdgSource.Document.Roots).ToList();

        // 1. Resolução Primária por NodePath
        if (!string.IsNullOrWhiteSpace(favorite.Locator.NodePath))
        {
            XElement? matchByPath = allServers.FirstOrDefault(s =>
                string.Equals(ServerLocator.BuildNodePath(s), favorite.Locator.NodePath, StringComparison.OrdinalIgnoreCase));

            if (matchByPath is not null)
            {
                return FavoriteResolutionResult.ValidResult(rdgSource, matchByPath, isFallback: false);
            }
        }

        // 2. Resolução Secundária (Fallback Resiliente por HostName + DisplayName)
        if (!string.IsNullOrWhiteSpace(favorite.Locator.HostName))
        {
            string expectedDisp = favorite.Locator.OriginalDisplayName ?? favorite.OriginalDisplayName;

            XElement? matchByHostAndName = allServers.FirstOrDefault(s =>
            {
                string host = RdgNode.FindChild(RdgNode.FindChild(s, "properties"), "name")?.Value?.Trim() ?? string.Empty;
                string disp = RdgNode.FindChild(RdgNode.FindChild(s, "properties"), "displayName")?.Value?.Trim() ?? host;
                return string.Equals(host, favorite.Locator.HostName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(disp, expectedDisp, StringComparison.OrdinalIgnoreCase);
            });

            if (matchByHostAndName is not null)
            {
                // Atualiza o NodePath para refletir a nova localização após movimentação de grupo
                favorite.Locator.NodePath = ServerLocator.BuildNodePath(matchByHostAndName);
                return FavoriteResolutionResult.ValidResult(
                    rdgSource,
                    matchByHostAndName,
                    isFallback: true,
                    message: "Resolvido por correspondência de HostName e Nome.");
            }

            // 3. Fallback somente por HostName se único
            var matchesByHost = allServers.Where(s =>
            {
                string host = RdgNode.FindChild(RdgNode.FindChild(s, "properties"), "name")?.Value?.Trim() ?? string.Empty;
                return string.Equals(host, favorite.Locator.HostName, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (matchesByHost.Count == 1)
            {
                favorite.Locator.NodePath = ServerLocator.BuildNodePath(matchesByHost[0]);
                return FavoriteResolutionResult.ValidResult(
                    rdgSource,
                    matchesByHost[0],
                    isFallback: true,
                    message: "Resolvido por HostName exclusivo.");
            }
        }

        return FavoriteResolutionResult.MissingNodeResult(rdgSource, "Servidor não encontrado no arquivo RDG.");
    }

    private FavoriteResolutionResult ResolveGuacamoleServer(FavoriteEntry favorite, GuacamoleInventorySource guacSource)
    {
        // 1. Resolução Primária por ConnectionIdentifier e DataSource
        if (!string.IsNullOrWhiteSpace(favorite.Locator.ConnectionIdentifier))
        {
            var match = guacSource.GuacamoleReferences.FirstOrDefault(kvp =>
                string.Equals(kvp.Value.ConnectionIdentifier, favorite.Locator.ConnectionIdentifier, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(favorite.Locator.DataSource) || string.Equals(kvp.Value.DataSource, favorite.Locator.DataSource, StringComparison.OrdinalIgnoreCase)));

            if (match.Key is not null)
            {
                return FavoriteResolutionResult.ValidResult(guacSource, match.Key, isFallback: false);
            }
        }

        // 2. Resolução Secundária (Fallback por DisplayName dentro do portal)
        string expectedDisp = favorite.Locator.OriginalDisplayName ?? favorite.OriginalDisplayName;
        if (!string.IsNullOrWhiteSpace(expectedDisp))
        {
            var matchByName = guacSource.GuacamoleReferences.FirstOrDefault(kvp =>
                string.Equals(kvp.Value.DisplayName, expectedDisp, StringComparison.OrdinalIgnoreCase));

            if (matchByName.Key is not null)
            {
                return FavoriteResolutionResult.ValidResult(
                    guacSource,
                    matchByName.Key,
                    isFallback: true,
                    message: "Resolvido por correspondência de DisplayName do portal Guacamole.");
            }
        }

        return FavoriteResolutionResult.MissingNodeResult(guacSource, "Conexão não encontrada no portal Guacamole.");
    }

    private FavoriteEntry? FindExistingFavorite(string sourceType, Guid sourceId, ServerLocator locator)
    {
        if (string.Equals(sourceType, "RdgFile", StringComparison.OrdinalIgnoreCase))
        {
            return _favorites.FirstOrDefault(f =>
                string.Equals(f.SourceType, "RdgFile", StringComparison.OrdinalIgnoreCase) &&
                (f.SourceId == sourceId || string.Equals(f.Locator.SourcePath, locator.SourcePath, StringComparison.OrdinalIgnoreCase)) &&
                ((!string.IsNullOrEmpty(locator.NodePath) && string.Equals(f.Locator.NodePath, locator.NodePath, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(locator.HostName) && string.Equals(f.Locator.HostName, locator.HostName, StringComparison.OrdinalIgnoreCase))));
        }

        if (string.Equals(sourceType, "GuacamolePortal", StringComparison.OrdinalIgnoreCase))
        {
            return _favorites.FirstOrDefault(f =>
                string.Equals(f.SourceType, "GuacamolePortal", StringComparison.OrdinalIgnoreCase) &&
                (f.Locator.PortalId == locator.PortalId || f.SourceId == sourceId) &&
                string.Equals(f.Locator.ConnectionIdentifier, locator.ConnectionIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void NormalizeOrderIndexes()
    {
        for (int i = 0; i < _favorites.Count; i++)
        {
            _favorites[i].OrderIndex = i;
        }
    }

    private static IEnumerable<XElement> GetAllServerElements(IEnumerable<RdgNode> roots)
    {
        foreach (RdgNode root in roots)
        {
            foreach (XElement s in GetAllServerElements(root.Element))
            {
                yield return s;
            }
        }
    }

    private static IEnumerable<XElement> GetAllServerElements(XElement element)
    {
        if (element.Name.LocalName == "server")
        {
            yield return element;
        }

        foreach (XElement child in element.Elements())
        {
            foreach (XElement s in GetAllServerElements(child))
            {
                yield return s;
            }
        }
    }

    public static List<FavoriteEntry> ExtractFromWorkspace(WorkspaceDocument workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.Extensions is null || !workspace.Extensions.TryGetValue("favorites", out object? raw) || raw is null)
        {
            return [];
        }

        if (raw is List<FavoriteEntry> typedList)
        {
            return typedList;
        }

        try
        {
            if (raw is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    string json = element.GetRawText();
                    return JsonSerializer.Deserialize<List<FavoriteEntry>>(json, JsonOptions) ?? [];
                }
            }
            else
            {
                string json = JsonSerializer.Serialize(raw, JsonOptions);
                return JsonSerializer.Deserialize<List<FavoriteEntry>>(json, JsonOptions) ?? [];
            }
        }
        catch
        {
            return [];
        }

        return [];
    }

    public static void ApplyToWorkspace(WorkspaceDocument workspace, IEnumerable<FavoriteEntry> favorites)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(favorites);

        workspace.Extensions ??= [];
        workspace.Extensions["favorites"] = favorites.OrderBy(f => f.OrderIndex).ToList();
    }
}

