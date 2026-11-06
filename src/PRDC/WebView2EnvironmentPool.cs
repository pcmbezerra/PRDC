using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace PRDC;

internal static class WebView2EnvironmentPool
{
    private const string AdditionalBrowserArguments = "--no-proxy-server";
    private static readonly ConcurrentDictionary<Guid, Task<CoreWebView2Environment>> Environments = new();

    private static string? _customPortalsUserDataRootDirectory;

    public static string DefaultPortalsUserDataRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRDC",
        "WebView2",
        "portals");

    public static string PortalsUserDataRootDirectory => _customPortalsUserDataRootDirectory ?? DefaultPortalsUserDataRootDirectory;

    internal static void SetCustomPortalsUserDataRootDirectory(string? path) => _customPortalsUserDataRootDirectory = path;
    internal static void ResetCustomPortalsUserDataRootDirectory() => _customPortalsUserDataRootDirectory = null;

    public static string GetPortalUserDataFolderPath(Guid portalId) =>
        Path.Combine(PortalsUserDataRootDirectory, portalId.ToString("N"));

    public static Task<CoreWebView2Environment> GetOrCreateForPortalAsync(Guid portalId)
    {
        if (portalId == Guid.Empty)
        {
            return WebView2EnvironmentProvider.GetOrCreateAsync();
        }

        return Environments.GetOrAdd(portalId, CreateEnvironmentForPortalInternalAsync);
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentForPortalInternalAsync(Guid portalId)
    {
        string userDataFolder = GetPortalUserDataFolderPath(portalId);
        Directory.CreateDirectory(userDataFolder);

        CoreWebView2EnvironmentOptions options = new(AdditionalBrowserArguments);
        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: options);
    }

    public static void DeletePortalUserDataFolder(Guid portalId)
    {
        Environments.TryRemove(portalId, out _);

        string dir = GetPortalUserDataFolderPath(portalId);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Limpeza best-effort caso arquivos estejam temporariamente abertos
            }
        }
    }

    public static void ClearPool()
    {
        Environments.Clear();
    }
}

