using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PRDC;

internal static class GuacamolePortalManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string? _customPortalsRootDirectory;

    public static string DefaultPortalsRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRDC",
        "Portals");

    public static string PortalsRootDirectory => _customPortalsRootDirectory ?? DefaultPortalsRootDirectory;

    internal static void SetCustomPortalsRootDirectory(string? path) => _customPortalsRootDirectory = path;
    internal static void ResetCustomPortalsRootDirectory() => _customPortalsRootDirectory = null;

    public static string GetPortalDirectory(Guid portalId) =>
        Path.Combine(PortalsRootDirectory, portalId.ToString("N"));

    public static string GetMetadataFilePath(Guid portalId) =>
        Path.Combine(GetPortalDirectory(portalId), "portal.json");

    public static string GetInventoryFilePath(Guid portalId) =>
        Path.Combine(GetPortalDirectory(portalId), "inventory.json");

    public static IReadOnlyList<GuacamolePortalMetadata> ListPortals()
    {
        List<GuacamolePortalMetadata> portals = [];
        if (!Directory.Exists(PortalsRootDirectory))
        {
            return portals;
        }

        foreach (string dir in Directory.GetDirectories(PortalsRootDirectory))
        {
            string metadataPath = Path.Combine(dir, "portal.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(metadataPath);
                GuacamolePortalMetadata? metadata = JsonSerializer.Deserialize<GuacamolePortalMetadata>(json, JsonOptions);
                if (metadata is not null && metadata.Id != Guid.Empty)
                {
                    portals.Add(metadata);
                }
            }
            catch
            {
                // Ignora metadados corrompidos na listagem
            }
        }

        return portals;
    }

    public static async Task<GuacamolePortalMetadata?> LoadMetadataAsync(Guid portalId, CancellationToken cancellationToken = default)
    {
        string metadataPath = GetMetadataFilePath(portalId);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<GuacamolePortalMetadata>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<GuacamoleCacheSaveResult> SavePortalAsync(
        GuacamolePortalMetadata metadata,
        GuacamoleCacheRoot cacheRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(cacheRoot);

        string portalDir = GetPortalDirectory(metadata.Id);
        Directory.CreateDirectory(portalDir);

        // 1. Salva inventory.json atomicamente utilizando o GuacamoleInventoryCache
        string inventoryPath = GetInventoryFilePath(metadata.Id);
        GuacamoleCacheSaveResult saveResult = await GuacamoleInventoryCache.SaveAsync(cacheRoot, inventoryPath, cancellationToken).ConfigureAwait(false);
        if (!saveResult.IsSuccess)
        {
            return saveResult;
        }

        // 2. Salva portal.json
        string metadataPath = GetMetadataFilePath(metadata.Id);
        string tmpMetadata = metadataPath + ".tmp";
        try
        {
            await using (FileStream stream = new(tmpMetadata, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tmpMetadata, metadataPath, overwrite: true);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmpMetadata))
            {
                try { File.Delete(tmpMetadata); } catch { }
            }

            return GuacamoleCacheSaveResult.Failed(
                GuacamoleCacheFailureKind.IoFailure,
                $"Falha ao salvar metadados do portal: {ex.Message}");
        }

        return GuacamoleCacheSaveResult.Success();
    }

    public static void DeletePortal(Guid portalId)
    {
        if (portalId == Guid.Empty)
        {
            return;
        }

        // 1. Deleta arquivos de inventário e metadados
        string portalDir = GetPortalDirectory(portalId);
        if (Directory.Exists(portalDir))
        {
            try
            {
                Directory.Delete(portalDir, recursive: true);
            }
            catch
            {
                // Best-effort
            }
        }

        // 2. Deleta armazenamento de sessão do WebView2 desse portal
        WebView2EnvironmentPool.DeletePortalUserDataFolder(portalId);
    }

    public static async Task MigrateLegacyCacheIfNecessaryAsync()
    {
        try
        {
            // Se já existem portais no novo diretório, não precisa migrar
            if (Directory.Exists(PortalsRootDirectory) && Directory.GetDirectories(PortalsRootDirectory).Length > 0)
            {
                return;
            }

            if (!GuacamoleInventoryCache.Exists())
            {
                return;
            }

            GuacamoleCacheLoadResult legacyLoad = await GuacamoleInventoryCache.LoadAsync();
            if (!legacyLoad.IsSuccess || legacyLoad.CacheRoot is null)
            {
                return;
            }

            Guid legacyPortalId = Guid.NewGuid();
            string baseUrl = legacyLoad.CacheRoot.BaseUrl;

            GuacamolePortalMetadata metadata = new(
                legacyPortalId,
                "Guacamole",
                baseUrl,
                legacyLoad.CacheRoot.SavedAtUtc.UtcDateTime);

            await SavePortalAsync(metadata, legacyLoad.CacheRoot);
        }
        catch
        {
            // Migração best-effort não interrompe inicialização
        }
    }
}
