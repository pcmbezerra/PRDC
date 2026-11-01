using Microsoft.Web.WebView2.Core;

namespace PRDC;

internal static class WebView2EnvironmentProvider
{
    private const string AdditionalBrowserArguments = "--no-proxy-server";

    private static readonly Lock SyncRoot = new();
    private static Task<CoreWebView2Environment>? _environmentTask;

    public static string UserDataFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PRDC",
        "WebView2");

    public static Task<CoreWebView2Environment> GetOrCreateAsync()
    {
        lock (SyncRoot)
        {
            if (_environmentTask is null || _environmentTask.IsFaulted || _environmentTask.IsCanceled)
            {
                _environmentTask = CreateEnvironmentCoreAsync();
            }

            return _environmentTask;
        }
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentCoreAsync()
    {
        string userDataFolder = UserDataFolderPath;
        Directory.CreateDirectory(userDataFolder);

        CoreWebView2EnvironmentOptions options = new(AdditionalBrowserArguments);
        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: options);
    }
}

