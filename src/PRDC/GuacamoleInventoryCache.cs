using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace PRDC;

internal enum GuacamoleCacheFailureKind
{
    None = 0,
    NotFound,
    Corrupt,
    UnsupportedSchema,
    ValidationFailed,
    IoFailure,
    AccessDenied
}

internal sealed record GuacamoleCacheLoadResult
{
    public bool IsSuccess { get; }
    public GuacamoleCacheRoot? CacheRoot { get; }
    public GuacamoleCacheFailureKind FailureKind { get; }
    public string? ErrorMessage { get; }
    public string? RotatedFilePath { get; }

    private GuacamoleCacheLoadResult(
        bool isSuccess,
        GuacamoleCacheRoot? cacheRoot,
        GuacamoleCacheFailureKind failureKind,
        string? errorMessage,
        string? rotatedFilePath)
    {
        IsSuccess = isSuccess;
        CacheRoot = cacheRoot;
        FailureKind = failureKind;
        ErrorMessage = errorMessage;
        RotatedFilePath = rotatedFilePath;
    }

    public static GuacamoleCacheLoadResult Success(GuacamoleCacheRoot cacheRoot) =>
        new(true, cacheRoot, GuacamoleCacheFailureKind.None, null, null);

    public static GuacamoleCacheLoadResult Failed(
        GuacamoleCacheFailureKind failureKind,
        string errorMessage,
        string? rotatedFilePath = null) =>
        new(false, null, failureKind, errorMessage, rotatedFilePath);
}

internal sealed record GuacamoleCacheSaveResult
{
    public bool IsSuccess { get; }
    public GuacamoleCacheFailureKind FailureKind { get; }
    public string? ErrorMessage { get; }

    private GuacamoleCacheSaveResult(
        bool isSuccess,
        GuacamoleCacheFailureKind failureKind,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        FailureKind = failureKind;
        ErrorMessage = errorMessage;
    }

    public static GuacamoleCacheSaveResult Success() =>
        new(true, GuacamoleCacheFailureKind.None, null);

    public static GuacamoleCacheSaveResult Failed(
        GuacamoleCacheFailureKind failureKind,
        string errorMessage) =>
        new(false, failureKind, errorMessage);
}

internal sealed record GuacamoleCacheRoot(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    string BaseUrl,
    IReadOnlyList<GuacamoleCacheDataSource> DataSources);

internal sealed record GuacamoleCacheDataSource(
    string DataSource,
    GuacamoleCacheGroup RootGroup);

internal sealed record GuacamoleCacheGroup(
    string Name,
    IReadOnlyList<GuacamoleCacheGroup>? ChildGroups,
    IReadOnlyList<GuacamoleCacheConnection>? ChildConnections);

internal sealed record GuacamoleCacheConnection(
    string Identifier,
    string Name,
    string HostName,
    string Protocol);

internal static class GuacamoleInventoryCache
{
    public const int CurrentSchemaVersion = 1;
    public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    public const int MaxTreeDepth = 64;
    public const int MaxConnections = 100_000;
    public const int MaxGroups = 100_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string CacheDirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRDC", "Guacamole");

    public static string CacheFilePath =>
        Path.Combine(CacheDirectoryPath, "inventory.json");

    public static bool Exists(string? explicitFilePath = null) =>
        File.Exists(explicitFilePath ?? CacheFilePath);

    public static async Task<GuacamoleCacheLoadResult> LoadAsync(
        string? explicitFilePath = null,
        CancellationToken cancellationToken = default)
    {
        string targetPath = explicitFilePath ?? CacheFilePath;

        if (!File.Exists(targetPath))
        {
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.NotFound,
                $"O arquivo de cache '{targetPath}' não foi encontrado.");
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(targetPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.AccessDenied,
                $"Acesso negado ao inspecionar o arquivo de cache: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Erro de E/S ao inspecionar o arquivo de cache: {ex.Message}");
        }

        // 1. Arquivo vazio (0 bytes) -> Corrupção
        if (fileInfo.Length == 0)
        {
            string? rotated = RotateFile(targetPath, "corrupt", null);
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.Corrupt,
                "O inventário local do Guacamole não pôde ser carregado e foi preservado para diagnóstico. Efetue login novamente para recriá-lo.",
                rotated);
        }

        // 2. Arquivo excede 50 MB -> Corrupção / Limite violado
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            string? rotated = RotateFile(targetPath, "corrupt", null);
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.Corrupt,
                $"O arquivo de cache excede o tamanho máximo permitido de {MaxFileSizeBytes / (1024 * 1024)} MB.",
                rotated);
        }

        // 3. Leitura e parsing inicial para inspecionar schemaVersion e integridade JSON
        JsonDocument jsonDocument;
        try
        {
            await using FileStream readStream = new(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);

            jsonDocument = await JsonDocument.ParseAsync(readStream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            string? rotated = RotateFile(targetPath, "corrupt", null);
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.Corrupt,
                "O inventário local do Guacamole não pôde ser carregado e foi preservado para diagnóstico. Efetue login novamente para recriá-lo.",
                rotated);
        }
        catch (UnauthorizedAccessException ex)
        {
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.AccessDenied,
                $"Acesso negado ao abrir o arquivo de cache: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GuacamoleCacheLoadResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Erro de E/S ao abrir o arquivo de cache: {ex.Message}");
        }

        using (jsonDocument)
        {
            // 4. Verificação da estrutura básica e de schemaVersion
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                string? rotated = RotateFile(targetPath, "corrupt", null);
                return GuacamoleCacheLoadResult.Failed(
                    GuacamoleCacheFailureKind.Corrupt,
                    "O elemento raiz do JSON de cache não é um objeto.",
                    rotated);
            }

            if (jsonDocument.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaElement)
                && schemaElement.TryGetInt32(out int version))
            {
                if (version != CurrentSchemaVersion)
                {
                    string? rotated = RotateFile(targetPath, $"unsupported-v{version}", version);
                    return GuacamoleCacheLoadResult.Failed(
                        GuacamoleCacheFailureKind.UnsupportedSchema,
                        "O inventário local do Guacamole foi criado por uma versão incompatível do PRDC e não pôde ser carregado.",
                        rotated);
                }
            }
            else
            {
                string? rotated = RotateFile(targetPath, "corrupt", null);
                return GuacamoleCacheLoadResult.Failed(
                    GuacamoleCacheFailureKind.Corrupt,
                    "A propriedade 'schemaVersion' não foi encontrada ou é inválida no cache.",
                    rotated);
            }

            // 5. Deserialização completa
            GuacamoleCacheRoot? cacheRoot;
            try
            {
                cacheRoot = jsonDocument.RootElement.Deserialize<GuacamoleCacheRoot>(SerializerOptions);
            }
            catch (Exception)
            {
                string? rotated = RotateFile(targetPath, "corrupt", null);
                return GuacamoleCacheLoadResult.Failed(
                    GuacamoleCacheFailureKind.Corrupt,
                    "O inventário local do Guacamole não pôde ser carregado e foi preservado para diagnóstico. Efetue login novamente para recriá-lo.",
                    rotated);
            }

            if (cacheRoot is null)
            {
                string? rotated = RotateFile(targetPath, "corrupt", null);
                return GuacamoleCacheLoadResult.Failed(
                    GuacamoleCacheFailureKind.Corrupt,
                    "A deserialização do cache retornou nulo.",
                    rotated);
            }

            // 6. Validação profunda dos limites de payload
            try
            {
                ValidatePayload(cacheRoot);
            }
            catch (Exception ex)
            {
                string? rotated = RotateFile(targetPath, "corrupt", null);
                return GuacamoleCacheLoadResult.Failed(
                    GuacamoleCacheFailureKind.Corrupt,
                    $"O cache falhou na validação estrutural: {ex.Message}",
                    rotated);
            }

            return GuacamoleCacheLoadResult.Success(cacheRoot);
        }
    }

    public static async Task<GuacamoleCacheSaveResult> SaveAsync(
        GuacamoleCacheRoot cacheRoot,
        string? explicitFilePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cacheRoot);

        // 1. Validar completamente em memória antes de tocar no disco
        try
        {
            ValidatePayload(cacheRoot);
        }
        catch (Exception ex)
        {
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.ValidationFailed,
                $"Validação prévia do payload falhou: {ex.Message}");
        }

        string targetPath = explicitFilePath ?? CacheFilePath;
        string targetDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        string tmpPath = targetPath + ".tmp";
        string bakPath = targetPath + ".bak";

        // 2. Criar diretório do cache
        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.AccessDenied,
                $"Acesso negado ao criar o diretório de cache '{targetDir}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Falha ao criar o diretório de cache '{targetDir}': {ex.Message}");
        }

        // 3. Remover .tmp residual próprio se existir
        TryDeleteFile(tmpPath);

        // 4. Gravar inventory.json.tmp com WriteThrough e UTF-8 sem BOM
        try
        {
            await using (FileStream stream = new(
                tmpPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, cacheRoot, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteFile(tmpPath);
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.AccessDenied,
                $"Acesso negado ao gravar arquivo temporário: {ex.Message}");
        }
        catch (Exception ex)
        {
            TryDeleteFile(tmpPath);
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Falha de E/S ao gravar arquivo temporário: {ex.Message}");
        }

        // 5. Reabrir e validar integridade do temporário gravado
        try
        {
            await using FileStream verifyStream = new(
                tmpPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);

            GuacamoleCacheRoot? deserialized = await JsonSerializer.DeserializeAsync<GuacamoleCacheRoot>(
                verifyStream,
                SerializerOptions,
                cancellationToken);

            if (deserialized is null)
            {
                throw new InvalidDataException("A validação do arquivo temporário falhou (objeto nulo).");
            }

            ValidatePayload(deserialized);
        }
        catch (Exception ex)
        {
            TryDeleteFile(tmpPath);
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.ValidationFailed,
                $"Validação pós-gravação do arquivo temporário falhou: {ex.Message}");
        }

        // 6. Substituição atômica: File.Replace se existir targetPath, File.Move se não existir
        try
        {
            if (File.Exists(targetPath))
            {
                TryDeleteFile(bakPath);

                try
                {
                    File.Replace(tmpPath, targetPath, bakPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(targetPath, bakPath);
                    try
                    {
                        File.Move(tmpPath, targetPath);
                    }
                    catch
                    {
                        if (File.Exists(bakPath) && !File.Exists(targetPath))
                        {
                            File.Move(bakPath, targetPath);
                        }
                        throw;
                    }
                }
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDeleteFile(tmpPath);
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.AccessDenied,
                $"Acesso negado durante a substituição atômica: {ex.Message}");
        }
        catch (Exception ex)
        {
            TryDeleteFile(tmpPath);
            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Falha de E/S durante a substituição atômica: {ex.Message}");
        }

        // 7. Validar existência e tamanho do targetPath final
        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
        {
            if (File.Exists(bakPath) && !File.Exists(targetPath))
            {
                try { File.Move(bakPath, targetPath); } catch { }
            }

            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                "O arquivo de cache final não existe ou está vazio após a substituição.");
        }

        // 8. Excluir .bak somente após confirmação do arquivo final
        TryDeleteFile(bakPath);

        return GuacamoleCacheSaveResult.Success();
    }

    public static void Delete(string? explicitFilePath = null)
    {
        string targetPath = explicitFilePath ?? CacheFilePath;
        string tmpPath = targetPath + ".tmp";
        string bakPath = targetPath + ".bak";

        TryDeleteFile(targetPath);
        TryDeleteFile(tmpPath);
        TryDeleteFile(bakPath);
    }

    public static GuacamoleImportResult Materialize(GuacamoleCacheRoot cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(cacheRoot);
        ValidatePayload(cacheRoot);

        RdgDocument document = RdgDocument.CreateNew();
        document.UpdateGroup(document.Roots[0], "Guacamole");

        int groupCount = 0;
        int serverCount = 0;
        Dictionary<XElement, GuacamoleConnectionReference> connectionReferences = new(ReferenceEqualityComparer.Instance);
        Uri baseUri = new(cacheRoot.BaseUrl);

        if (cacheRoot.DataSources.Count == 1)
        {
            GuacamoleCacheDataSource singleDs = cacheRoot.DataSources[0];
            PopulateContainerFromCache(
                document,
                document.Roots[0],
                baseUri,
                singleDs.DataSource,
                singleDs.RootGroup,
                connectionReferences,
                ref groupCount,
                ref serverCount);
        }
        else
        {
            IEnumerable<GuacamoleCacheDataSource> orderedDataSources = cacheRoot.DataSources
                .OrderBy(entry => entry.DataSource, StringComparer.OrdinalIgnoreCase);

            foreach (GuacamoleCacheDataSource dataSourceEntry in orderedDataSources)
            {
                RdgNode dataSourceGroup = document.AddGroup(document.Roots[0], dataSourceEntry.DataSource);
                groupCount++;

                PopulateContainerFromCache(
                    document,
                    dataSourceGroup,
                    baseUri,
                    dataSourceEntry.DataSource,
                    dataSourceEntry.RootGroup,
                    connectionReferences,
                    ref groupCount,
                    ref serverCount);
            }
        }

        if (serverCount == 0)
        {
            throw new InvalidOperationException("Nenhuma conexão RDP foi encontrada no cache do Guacamole.");
        }

        return new GuacamoleImportResult(document, groupCount, serverCount, connectionReferences);
    }

    public static GuacamoleCacheRoot FromImportResult(GuacamoleImportResult importResult)
    {
        ArgumentNullException.ThrowIfNull(importResult);

        if (importResult.ConnectionReferences.Count == 0)
        {
            throw new InvalidOperationException("Não há conexões RDP no resultado da importação para persistir.");
        }

        string baseUrl = importResult.ConnectionReferences.Values.First().BaseUri.AbsoluteUri;
        RdgNode rootNode = importResult.Document.Roots[0];

        List<string> dataSources = importResult.ConnectionReferences.Values
            .Select(r => r.DataSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<GuacamoleCacheDataSource> cacheDataSources = [];

        if (dataSources.Count == 1)
        {
            string dataSource = dataSources[0];
            var (childGroups, childConnections) = ExtractChildren(rootNode, importResult.ConnectionReferences);
            GuacamoleCacheGroup rootGroup = new("ROOT", childGroups, childConnections);
            cacheDataSources.Add(new GuacamoleCacheDataSource(dataSource, rootGroup));
        }
        else
        {
            foreach (string dataSource in dataSources)
            {
                RdgNode? dsGroupNode = rootNode.Children.FirstOrDefault(
                    c => c.Kind == RdgNodeKind.Group && string.Equals(c.Name, dataSource, StringComparison.OrdinalIgnoreCase));

                if (dsGroupNode is not null)
                {
                    var (childGroups, childConnections) = ExtractChildren(dsGroupNode, importResult.ConnectionReferences);
                    GuacamoleCacheGroup rootGroup = new("ROOT", childGroups, childConnections);
                    cacheDataSources.Add(new GuacamoleCacheDataSource(dataSource, rootGroup));
                }
                else
                {
                    // Fallback para conexões órfãs filtradas por dataSource
                    List<GuacamoleCacheConnection> directConnections = [];
                    foreach (RdgNode child in rootNode.Children.Where(c => c.Kind == RdgNodeKind.Server))
                    {
                        if (importResult.ConnectionReferences.TryGetValue(child.Element, out GuacamoleConnectionReference? refObj)
                            && string.Equals(refObj.DataSource, dataSource, StringComparison.OrdinalIgnoreCase))
                        {
                            string hostName = !string.IsNullOrWhiteSpace(child.HostName)
                                ? child.HostName
                                : GuacamoleInventoryProvider.ExtractHostName(child.Name);

                            directConnections.Add(new GuacamoleCacheConnection(
                                refObj.ConnectionIdentifier,
                                refObj.DisplayName,
                                hostName,
                                refObj.Protocol));
                        }
                    }

                    GuacamoleCacheGroup rootGroup = new("ROOT", [], directConnections);
                    cacheDataSources.Add(new GuacamoleCacheDataSource(dataSource, rootGroup));
                }
            }
        }

        GuacamoleCacheRoot root = new(
            CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            baseUrl,
            cacheDataSources);

        ValidatePayload(root);
        return root;
    }

    public static void ValidatePayload(GuacamoleCacheRoot cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(cacheRoot);

        if (cacheRoot.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"SchemaVersion incompatível ({cacheRoot.SchemaVersion}). Esperado: {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(cacheRoot.BaseUrl))
        {
            throw new InvalidDataException("A URL base do Guacamole não pode ser vazia.");
        }

        if (!Uri.TryCreate(cacheRoot.BaseUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("A URL base do Guacamole deve ser uma URI absoluta iniciada por http:// ou https://.");
        }

        if (cacheRoot.DataSources is null || cacheRoot.DataSources.Count == 0)
        {
            throw new InvalidDataException("O cache não contém nenhum dataSource configurado.");
        }

        int totalGroups = 0;
        int totalConnections = 0;
        Stack<(GuacamoleCacheGroup Group, int Depth)> stack = new();

        foreach (GuacamoleCacheDataSource ds in cacheRoot.DataSources)
        {
            if (string.IsNullOrWhiteSpace(ds.DataSource))
            {
                throw new InvalidDataException("DataSource com identificador nulo ou vazio encontrado.");
            }

            if (ds.RootGroup is null)
            {
                throw new InvalidDataException($"DataSource '{ds.DataSource}' possui nó raiz nulo.");
            }

            stack.Push((ds.RootGroup, 1));
        }

        while (stack.Count > 0)
        {
            var (currentGroup, depth) = stack.Pop();

            if (depth > MaxTreeDepth)
            {
                throw new InvalidDataException(
                    $"A profundidade da árvore ({depth}) excede o limite máximo permitido de {MaxTreeDepth} níveis.");
            }

            totalGroups++;
            if (totalGroups > MaxGroups)
            {
                throw new InvalidDataException(
                    $"A quantidade total de grupos ({totalGroups}) excede o limite máximo permitido de {MaxGroups}.");
            }

            if (currentGroup.ChildConnections is { Count: > 0 })
            {
                foreach (GuacamoleCacheConnection connection in currentGroup.ChildConnections)
                {
                    totalConnections++;
                    if (totalConnections > MaxConnections)
                    {
                        throw new InvalidDataException(
                            $"A quantidade total de conexões ({totalConnections}) excede o limite máximo permitido de {MaxConnections}.");
                    }

                    if (string.IsNullOrWhiteSpace(connection.Identifier))
                    {
                        throw new InvalidDataException("Conexão com identificador nulo ou vazio encontrada.");
                    }

                    if (string.IsNullOrWhiteSpace(connection.Name))
                    {
                        throw new InvalidDataException("Conexão com nome nulo ou vazio encontrada.");
                    }

                    if (string.IsNullOrWhiteSpace(connection.Protocol)
                        || !string.Equals(connection.Protocol.Trim(), "rdp", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Protocolo inválido '{connection.Protocol}' (apenas 'rdp' é suportado).");
                    }
                }
            }

            if (currentGroup.ChildGroups is { Count: > 0 })
            {
                foreach (GuacamoleCacheGroup childGroup in currentGroup.ChildGroups)
                {
                    stack.Push((childGroup, depth + 1));
                }
            }
        }

        if (totalConnections == 0)
        {
            throw new InvalidDataException("O cache não contém nenhuma conexão RDP válida.");
        }
    }

    private static (IReadOnlyList<GuacamoleCacheGroup> Groups, IReadOnlyList<GuacamoleCacheConnection> Connections)
        ExtractChildren(RdgNode containerNode, IReadOnlyDictionary<XElement, GuacamoleConnectionReference> references)
    {
        List<GuacamoleCacheGroup> groups = [];
        List<GuacamoleCacheConnection> connections = [];

        IEnumerable<RdgNode> orderedChildren = containerNode.Children
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (RdgNode child in orderedChildren)
        {
            if (child.Kind == RdgNodeKind.Group)
            {
                var (subGroups, subConnections) = ExtractChildren(child, references);
                groups.Add(new GuacamoleCacheGroup(child.Name, subGroups, subConnections));
            }
            else if (child.Kind == RdgNodeKind.Server)
            {
                if (references.TryGetValue(child.Element, out GuacamoleConnectionReference? refObj))
                {
                    string hostName = !string.IsNullOrWhiteSpace(child.HostName)
                        ? child.HostName
                        : GuacamoleInventoryProvider.ExtractHostName(child.Name);

                    connections.Add(new GuacamoleCacheConnection(
                        refObj.ConnectionIdentifier,
                        refObj.DisplayName,
                        hostName,
                        refObj.Protocol));
                }
            }
        }

        return (groups, connections);
    }

    private static void PopulateContainerFromCache(
        RdgDocument document,
        RdgNode parentContainer,
        Uri baseUri,
        string dataSource,
        GuacamoleCacheGroup sourceGroup,
        Dictionary<XElement, GuacamoleConnectionReference> connectionReferences,
        ref int groupCount,
        ref int serverCount)
    {
        if (sourceGroup.ChildGroups is { Count: > 0 })
        {
            IEnumerable<GuacamoleCacheGroup> orderedGroups = sourceGroup.ChildGroups
                .OrderBy(g => g.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (GuacamoleCacheGroup childGroup in orderedGroups)
            {
                string groupName = string.IsNullOrWhiteSpace(childGroup.Name) ? "Grupo" : childGroup.Name.Trim();
                RdgNode createdGroup = document.AddGroup(parentContainer, groupName);
                groupCount++;

                PopulateContainerFromCache(
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
            IEnumerable<GuacamoleCacheConnection> orderedConnections = sourceGroup.ChildConnections
                .OrderBy(c => c.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (GuacamoleCacheConnection connection in orderedConnections)
            {
                string displayName = connection.Name.Trim();
                string hostName = !string.IsNullOrWhiteSpace(connection.HostName)
                    ? connection.HostName.Trim()
                    : GuacamoleInventoryProvider.ExtractHostName(displayName);
                string identifier = connection.Identifier.Trim();
                string protocol = string.IsNullOrWhiteSpace(connection.Protocol) ? "rdp" : connection.Protocol.Trim();

                RdgNode serverNode = document.AddServer(parentContainer, displayName, hostName);
                connectionReferences[serverNode.Element] = new GuacamoleConnectionReference(
                    baseUri,
                    dataSource,
                    identifier,
                    displayName,
                    protocol);
                serverCount++;
            }
        }
    }

    private static string? RotateFile(string filePath, string prefix, int? version)
    {
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string newName = version.HasValue
                ? $"inventory.unsupported-v{version.Value}-{timestamp}.json"
                : $"inventory.{prefix}-{timestamp}.json";
            string targetPath = Path.Combine(dir, newName);

            if (File.Exists(targetPath))
            {
                targetPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(targetPath)}_{Guid.NewGuid():N}.json");
            }

            File.Move(filePath, targetPath);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Limpeza best-effort
        }
    }
}

