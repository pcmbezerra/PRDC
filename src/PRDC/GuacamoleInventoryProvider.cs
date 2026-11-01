using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace PRDC;

internal sealed record GuacamoleImportResult(
    RdgDocument Document,
    int GroupCount,
    int ServerCount,
    IReadOnlyDictionary<XElement, GuacamoleConnectionReference> ConnectionReferences);

internal static class GuacamoleInventoryProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<GuacamoleImportResult> ImportAsync(
        string baseUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        Uri normalizedBaseUri = NormalizeBaseUri(baseUrl);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Informe o usuário do Guacamole.", nameof(username));
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Informe a senha do Guacamole.", nameof(password));
        }

        using SocketsHttpHandler httpHandler = new()
        {
            UseProxy = false
        };
        using HttpClient httpClient = new(httpHandler)
        {
            BaseAddress = normalizedBaseUri,
            Timeout = RequestTimeout
        };

        GuacamoleTokenResponse tokenResponse = await AuthenticateAsync(
            httpClient,
            username.Trim(),
            password,
            cancellationToken);

        string authToken = tokenResponse.AuthToken!;
        IReadOnlyList<string> dataSources = DiscoverDataSources(tokenResponse);

        try
        {
            List<(string DataSource, GuacamoleGroupDto RootGroup)> rdpTrees = [];

            foreach (string dataSource in dataSources)
            {
                GuacamoleGroupDto? rootGroup = await FetchConnectionTreeAsync(
                    httpClient,
                    dataSource,
                    authToken,
                    cancellationToken);

                if (rootGroup is not null && HasRdpConnections(rootGroup))
                {
                    rdpTrees.Add((dataSource, rootGroup));
                }
            }

            return BuildDocument(normalizedBaseUri, rdpTrees);
        }
        finally
        {
            await RevokeTokenSafeAsync(httpClient, authToken);
        }
    }

    internal static IReadOnlyList<string> DiscoverDataSources(
        string? primaryDataSource,
        IReadOnlyList<string>? availableDataSources)
    {
        List<string> discovered = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            string trimmed = candidate.Trim();
            if (seen.Add(trimmed))
            {
                discovered.Add(trimmed);
            }
        }

        TryAdd(primaryDataSource);

        if (availableDataSources is { Count: > 0 })
        {
            foreach (string? source in availableDataSources)
            {
                TryAdd(source);
            }
        }

        return discovered;
    }

    internal static Uri NormalizeBaseUri(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            throw new InvalidOperationException("Informe a URL do Guacamole.");
        }

        string candidate = rawUrl.Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("A URL do Guacamole deve iniciar com http:// ou https://.");
        }

        return uri;
    }

    internal static string ExtractHostName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        string trimmed = displayName.Trim();
        int delimiterIndex = -1;

        for (int index = 0; index < trimmed.Length; index++)
        {
            char current = trimmed[index];
            if (current == '_' || current == '(' || char.IsWhiteSpace(current))
            {
                delimiterIndex = index;
                break;
            }
        }

        if (delimiterIndex <= 0)
        {
            return trimmed;
        }

        string candidate = trimmed[..delimiterIndex].Trim();
        return string.IsNullOrWhiteSpace(candidate) ? trimmed : candidate;
    }

    private static async Task<GuacamoleTokenResponse> AuthenticateAsync(
        HttpClient httpClient,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent formContent = new([
            new KeyValuePair<string, string>("username", username),
            new KeyValuePair<string, string>("password", password)
        ]);

        using HttpResponseMessage response = await httpClient.PostAsync(
            "api/tokens",
            formContent,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Falha de autenticação no Guacamole: usuário ou senha inválidos.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"O servidor Guacamole retornou o código HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) durante a autenticação.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GuacamoleTokenResponse? tokenResponse = await JsonSerializer.DeserializeAsync<GuacamoleTokenResponse>(
            stream,
            JsonOptions,
            cancellationToken);

        if (tokenResponse is null
            || string.IsNullOrWhiteSpace(tokenResponse.AuthToken)
            || string.IsNullOrWhiteSpace(tokenResponse.DataSource))
        {
            throw new InvalidOperationException("A resposta de autenticação do Guacamole não contém o token ou dataSource esperados.");
        }

        return tokenResponse;
    }

    private static IReadOnlyList<string> DiscoverDataSources(GuacamoleTokenResponse tokenResponse)
    {
        return DiscoverDataSources(tokenResponse.DataSource, tokenResponse.AvailableDataSources);
    }

    private static async Task<GuacamoleGroupDto?> FetchConnectionTreeAsync(
        HttpClient httpClient,
        string dataSource,
        string authToken,
        CancellationToken cancellationToken)
    {
        string relativePath = $"api/session/data/{Uri.EscapeDataString(dataSource)}/connectionGroups/ROOT/tree";
        using HttpRequestMessage request = new(HttpMethod.Get, relativePath);
        request.Headers.TryAddWithoutValidation("Guacamole-Token", authToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Falha de autorização ao consultar o inventário no Guacamole.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Falha ao obter a árvore de conexões do Guacamole para o datasource '{dataSource}' (HTTP {(int)response.StatusCode}).");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        GuacamoleGroupDto? rootGroup = await JsonSerializer.DeserializeAsync<GuacamoleGroupDto>(
            stream,
            JsonOptions,
            cancellationToken);

        return rootGroup ?? throw new InvalidOperationException("A árvore de conexões retornada pelo Guacamole está vazia ou inválida.");
    }

    private static async Task RevokeTokenSafeAsync(HttpClient httpClient, string authToken)
    {
        try
        {
            string relativePath = $"api/tokens/{Uri.EscapeDataString(authToken)}";
            using HttpResponseMessage _ = await httpClient.DeleteAsync(relativePath);
        }
        catch
        {
            // Limpeza best-effort: erros ao revogar o token não devem mascarar o resultado da importação.
        }
    }

    internal static GuacamoleImportResult BuildDocument(
        Uri baseUri,
        IReadOnlyList<(string DataSource, GuacamoleGroupDto RootGroup)> rdpTrees)
    {
        if (rdpTrees.Count == 0)
        {
            throw new InvalidOperationException("Nenhuma conexão RDP foi encontrada para este usuário no Guacamole.");
        }

        RdgDocument document = RdgDocument.CreateNew();
        document.UpdateGroup(document.Roots[0], "Guacamole");

        int groupCount = 0;
        int serverCount = 0;
        Dictionary<XElement, GuacamoleConnectionReference> connectionReferences = new(ReferenceEqualityComparer.Instance);

        if (rdpTrees.Count == 1)
        {
            PopulateContainer(
                document,
                document.Roots[0],
                baseUri,
                rdpTrees[0].DataSource,
                rdpTrees[0].RootGroup,
                connectionReferences,
                ref groupCount,
                ref serverCount);
        }
        else
        {
            IEnumerable<(string DataSource, GuacamoleGroupDto RootGroup)> orderedTrees = rdpTrees
                .OrderBy(entry => entry.DataSource, StringComparer.OrdinalIgnoreCase);

            foreach ((string dataSource, GuacamoleGroupDto rootGroup) in orderedTrees)
            {
                var dataSourceGroup = document.AddGroup(document.Roots[0], dataSource);
                groupCount++;
                PopulateContainer(
                    document,
                    dataSourceGroup,
                    baseUri,
                    dataSource,
                    rootGroup,
                    connectionReferences,
                    ref groupCount,
                    ref serverCount);
            }
        }

        if (serverCount == 0)
        {
            throw new InvalidOperationException("Nenhuma conexão RDP foi encontrada para este usuário no Guacamole.");
        }

        return new GuacamoleImportResult(document, groupCount, serverCount, connectionReferences);
    }

    private static void PopulateContainer(
        RdgDocument document,
        RdgNode parentContainer,
        Uri baseUri,
        string dataSource,
        GuacamoleGroupDto sourceGroup,
        Dictionary<XElement, GuacamoleConnectionReference> connectionReferences,
        ref int groupCount,
        ref int serverCount)
    {
        if (sourceGroup.ChildConnectionGroups is { Count: > 0 })
        {
            IEnumerable<GuacamoleGroupDto> orderedGroups = sourceGroup.ChildConnectionGroups
                .Where(HasRdpConnections)
                .OrderBy(group => group.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (GuacamoleGroupDto childGroup in orderedGroups)
            {
                string groupName = string.IsNullOrWhiteSpace(childGroup.Name)
                    ? "Grupo"
                    : childGroup.Name.Trim();

                var createdGroup = document.AddGroup(parentContainer, groupName);
                groupCount++;
                PopulateContainer(
                    document,
                    createdGroup,
                    baseUri,
                    dataSource,
                    childGroup,
                    connectionReferences,
                    ref groupCount,
                    ref serverCount);
            }
        }

        if (sourceGroup.ChildConnections is { Count: > 0 })
        {
            IEnumerable<GuacamoleConnectionDto> orderedRdpConnections = sourceGroup.ChildConnections
                .Where(IsRdpConnection)
                .OrderBy(connection => connection.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (GuacamoleConnectionDto connection in orderedRdpConnections)
            {
                string displayName = connection.Name!.Trim();
                string hostName = ExtractHostName(displayName);
                string identifier = connection.Identifier!.Trim();

                RdgNode serverNode = document.AddServer(parentContainer, displayName, hostName);
                connectionReferences[serverNode.Element] = new GuacamoleConnectionReference(
                    baseUri,
                    dataSource,
                    identifier,
                    displayName,
                    "rdp");
                serverCount++;
            }
        }
    }

    private static bool HasRdpConnections(GuacamoleGroupDto group)
    {
        if (group.ChildConnections is { Count: > 0 } && group.ChildConnections.Any(IsRdpConnection))
        {
            return true;
        }

        return group.ChildConnectionGroups is { Count: > 0 }
            && group.ChildConnectionGroups.Any(HasRdpConnections);
    }

    private static bool IsRdpConnection(GuacamoleConnectionDto connection)
    {
        return !string.IsNullOrWhiteSpace(connection.Name)
            && !string.IsNullOrWhiteSpace(connection.Identifier)
            && string.Equals(connection.Protocol?.Trim(), "rdp", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GuacamoleTokenResponse(
        string? AuthToken,
        string? DataSource,
        IReadOnlyList<string>? AvailableDataSources);

    internal sealed record GuacamoleGroupDto(
        string? Name,
        IReadOnlyList<GuacamoleGroupDto>? ChildConnectionGroups,
        IReadOnlyList<GuacamoleConnectionDto>? ChildConnections);

    internal sealed record GuacamoleConnectionDto(
        string? Identifier,
        string? Name,
        string? Protocol);
}
