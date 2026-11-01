using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PRDC;

internal sealed class GuacamoleSessionHost : UserControl
{
    private const string RuntimeMissingMessage =
        "O Microsoft Edge WebView2 Runtime é necessário para abrir sessões do Apache Guacamole dentro do PRDC.";

    private const string RemoteResizeScript =
        """
        (() => {
            try {
                window.dispatchEvent(new Event('resize'));
            } catch (e) {}
            try {
                document.querySelectorAll('object.resize-sensor').forEach(sensor => {
                    try {
                        if (sensor.contentDocument && sensor.contentDocument.defaultView) {
                            sensor.contentDocument.defaultView.dispatchEvent(new Event('resize'));
                        }
                    } catch (e) {}
                });
            } catch (e) {}
        })()
        """;

    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(16, 20, 26)
    };

    private readonly System.Windows.Forms.Timer _resizeDebounceTimer = new()
    {
        Interval = 250
    };

    private Task? _initializationTask;
    private bool _coreWebView2Initialized;
    private bool _handlersAttached;
    private bool _connectionRequested;
    private bool _disposed;
    private string _lastStateMessage = string.Empty;

    public GuacamoleSessionHost(GuacamoleConnectionReference connectionReference)
    {
        ArgumentNullException.ThrowIfNull(connectionReference);

        ConnectionReference = connectionReference;
        TargetClientUri = connectionReference.BuildClientUri();

        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(16, 20, 26);
        Controls.Add(_webView);

        _resizeDebounceTimer.Tick += OnResizeDebounceTimerTick;
    }

    public event EventHandler<RdpConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public RdpConnectionState ConnectionState { get; private set; } = RdpConnectionState.Disconnected;

    public GuacamoleConnectionReference ConnectionReference { get; }

    public Uri TargetClientUri { get; }

    public async Task ConnectAsync()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        _connectionRequested = true;
        SetState(RdpConnectionState.Connecting, $"Conectando a {ConnectionReference.DisplayName} via Guacamole...");

        try
        {
            await EnsureCoreWebView2InitializedAsync();
            if (!CanNavigate())
            {
                return;
            }

            _webView.CoreWebView2!.Navigate(TargetClientUri.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _connectionRequested = false;
            SetState(RdpConnectionState.Disconnected, RuntimeMissingMessage);
            throw new InvalidOperationException(RuntimeMissingMessage, exception);
        }
        catch (Exception) when (_disposed || IsDisposed || Disposing)
        {
            // Aba/host descartado durante a inicialização assíncrona (ex.: ObjectDisposedException, InvalidOperationException, COMException E_ABORT).
        }
        catch (Exception exception)
        {
            _connectionRequested = false;
            SetState(
                RdpConnectionState.Disconnected,
                $"Falha ao iniciar o WebView2 para {ConnectionReference.DisplayName}: {exception.Message}");
            throw;
        }
    }

    public async Task ReconnectAsync()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        _connectionRequested = true;
        SetState(RdpConnectionState.Connecting, $"Reconectando a {ConnectionReference.DisplayName} via Guacamole...");

        try
        {
            await EnsureCoreWebView2InitializedAsync();
            if (!CanNavigate())
            {
                return;
            }

            string currentSource = _webView.CoreWebView2!.Source;
            if (string.Equals(currentSource, TargetClientUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            {
                _webView.CoreWebView2.Reload();
            }
            else
            {
                _webView.CoreWebView2.Navigate(TargetClientUri.AbsoluteUri);
            }
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _connectionRequested = false;
            SetState(RdpConnectionState.Disconnected, RuntimeMissingMessage);
            throw new InvalidOperationException(RuntimeMissingMessage, exception);
        }
        catch (Exception) when (_disposed || IsDisposed || Disposing)
        {
            // Aba/host descartado durante a reconexão assíncrona (ex.: ObjectDisposedException, InvalidOperationException, COMException E_ABORT).
        }
        catch (Exception exception)
        {
            _connectionRequested = false;
            SetState(
                RdpConnectionState.Disconnected,
                $"Falha ao reconectar a {ConnectionReference.DisplayName}: {exception.Message}");
            throw;
        }
    }

    public void DisconnectSession()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        _connectionRequested = false;
        _resizeDebounceTimer.Stop();

        try
        {
            if (_coreWebView2Initialized && !_webView.IsDisposed && _webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.Navigate("about:blank");
            }
        }
        catch
        {
            // Ignora falhas de navegação ao desconectar.
        }

        SetState(RdpConnectionState.Disconnected, $"Sessão desconectada de {ConnectionReference.DisplayName}.");
    }

    internal static bool IsAllowedUri(Uri baseUri, string? candidateUriString)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        if (!baseUri.IsAbsoluteUri
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidateUriString))
        {
            return false;
        }

        if (string.Equals(candidateUriString, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(candidateUriString, UriKind.Absolute, out Uri? candidateUri))
        {
            return false;
        }

        if (candidateUri.Scheme != Uri.UriSchemeHttp && candidateUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        string requiredPathPrefix = baseUri.AbsolutePath.TrimEnd('/') + "/";

        return string.Equals(candidateUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidateUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidateUri.DnsSafeHost, baseUri.DnsSafeHost, StringComparison.OrdinalIgnoreCase)
            && candidateUri.Port == baseUri.Port
            && candidateUri.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTunnelProtocolUri(Uri baseUri, string? requestUriString)
    {
        if (!IsAllowedUri(baseUri, requestUriString)
            || !Uri.TryCreate(requestUriString, UriKind.Absolute, out Uri? requestUri))
        {
            return false;
        }

        string basePrefix = baseUri.AbsolutePath.TrimEnd('/') + "/api/session/tunnels/";
        const string protocolSuffix = "/protocol";

        string path = requestUri.AbsolutePath;
        if (!path.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(protocolSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int uuidLength = path.Length - basePrefix.Length - protocolSuffix.Length;
        if (uuidLength <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> uuidSegment = path.AsSpan(basePrefix.Length, uuidLength);
        return !uuidSegment.IsWhiteSpace() && !uuidSegment.Contains('/');
    }

    internal static bool IsTokensUri(Uri baseUri, string? requestUriString)
    {
        if (!IsAllowedUri(baseUri, requestUriString)
            || !Uri.TryCreate(requestUriString, UriKind.Absolute, out Uri? requestUri))
        {
            return false;
        }

        string expectedPath = baseUri.AbsolutePath.TrimEnd('/') + "/api/tokens";
        return string.Equals(requestUri.AbsolutePath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;
        _connectionRequested = false;

        if (disposing)
        {
            try
            {
                _resizeDebounceTimer.Stop();
                _resizeDebounceTimer.Tick -= OnResizeDebounceTimerTick;
                _resizeDebounceTimer.Dispose();
            }
            catch
            {
                // Ignora falhas de descarte do timer de debounce.
            }

            try
            {
                if (!_webView.IsDisposed && _webView.CoreWebView2 is not null)
                {
                    DetachCoreWebView2Handlers(_webView.CoreWebView2);
                    _webView.CoreWebView2.Navigate("about:blank");
                }
            }
            catch
            {
                // Ignora falhas de limpeza durante o dispose.
            }

            try
            {
                if (!_webView.IsDisposed)
                {
                    _webView.Dispose();
                }
            }
            catch
            {
                // Ignora falhas de dispose do controle nativo.
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        ScheduleResizeNotification();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            ScheduleResizeNotification();
        }
    }

    private void ScheduleResizeNotification()
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        if (!_coreWebView2Initialized || _webView.IsDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        if (ConnectionState is not (RdpConnectionState.Connected or RdpConnectionState.LoggedIn))
        {
            return;
        }

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    private async void OnResizeDebounceTimerTick(object? sender, EventArgs e)
    {
        _resizeDebounceTimer.Stop();

        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        if (!_coreWebView2Initialized || _webView.IsDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        if (ConnectionState is not (RdpConnectionState.Connected or RdpConnectionState.LoggedIn))
        {
            return;
        }

        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        try
        {
            string currentSource = _webView.CoreWebView2.Source;
            if (!IsAllowedUri(ConnectionReference.BaseUri, currentSource)
                || string.Equals(currentSource, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _webView.CoreWebView2.ExecuteScriptAsync(RemoteResizeScript);
        }
        catch
        {
            // Ignora falhas transitórias de execução de script durante redimensionamento ou descarte.
        }
    }

    private bool CanNavigate()
    {
        return !_disposed
            && !IsDisposed
            && !Disposing
            && _connectionRequested
            && !_webView.IsDisposed
            && _webView.CoreWebView2 is not null;
    }

    private Task EnsureCoreWebView2InitializedAsync()
    {
        if (_coreWebView2Initialized && !_webView.IsDisposed && _webView.CoreWebView2 is not null)
        {
            return Task.CompletedTask;
        }

        if (_initializationTask is null || _initializationTask.IsFaulted || _initializationTask.IsCanceled)
        {
            _initializationTask = InitializeCoreWebView2InternalAsync();
        }

        return _initializationTask;
    }

    private async Task InitializeCoreWebView2InternalAsync()
    {
        CoreWebView2Environment environment = await WebView2EnvironmentProvider.GetOrCreateAsync();
        if (_disposed || IsDisposed || Disposing || _webView.IsDisposed)
        {
            return;
        }

        await _webView.EnsureCoreWebView2Async(environment);
        if (_disposed || IsDisposed || Disposing || _webView.IsDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        if (!_coreWebView2Initialized)
        {
            ConfigureCoreWebView2(_webView.CoreWebView2);
            _coreWebView2Initialized = true;
        }
    }

    private void ConfigureCoreWebView2(CoreWebView2 coreWebView2)
    {
        CoreWebView2Settings settings = coreWebView2.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
#if !DEBUG
        settings.AreDevToolsEnabled = false;
#endif

        if (!_handlersAttached)
        {
            coreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            coreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            coreWebView2.SourceChanged += CoreWebView2_SourceChanged;
            coreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            coreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
            coreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
            coreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
            _handlersAttached = true;
        }
    }

    private void DetachCoreWebView2Handlers(CoreWebView2 coreWebView2)
    {
        if (!_handlersAttached)
        {
            return;
        }

        _handlersAttached = false;
        coreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
        coreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        coreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
        coreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        coreWebView2.PermissionRequested -= CoreWebView2_PermissionRequested;
        coreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
        coreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (_disposed || IsDisposed || Disposing)
        {
            eventArgs.Cancel = true;
            return;
        }

        if (!IsAllowedUri(ConnectionReference.BaseUri, eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            return;
        }

        if (_connectionRequested
            && !string.Equals(eventArgs.Uri, "about:blank", StringComparison.OrdinalIgnoreCase)
            && ConnectionState == RdpConnectionState.Disconnected)
        {
            SetState(RdpConnectionState.Connecting, $"Carregando sessão Guacamole para {ConnectionReference.DisplayName}...");
        }
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!CanNavigate())
        {
            return;
        }

        string currentSource;
        try
        {
            currentSource = _webView.CoreWebView2!.Source;
        }
        catch
        {
            return;
        }

        if (string.Equals(currentSource, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!eventArgs.IsSuccess)
        {
            if (eventArgs.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                return;
            }

            _connectionRequested = false;
            SetState(
                RdpConnectionState.Disconnected,
                $"Não foi possível carregar a sessão do Apache Guacamole ({ConnectionReference.DisplayName}: {eventArgs.WebErrorStatus}).");
            return;
        }

        if (ConnectionState == RdpConnectionState.Connecting)
        {
            SetState(
                RdpConnectionState.Connected,
                $"Conectado ao Guacamole ({ConnectionReference.DisplayName}); aguardando abertura do túnel...");
            ScheduleResizeNotification();
        }
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs eventArgs)
    {
        if (_disposed || IsDisposed || Disposing || _webView.IsDisposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            string currentSource = _webView.CoreWebView2.Source;
            if (!IsAllowedUri(ConnectionReference.BaseUri, currentSource))
            {
                _connectionRequested = false;
                _webView.CoreWebView2.Navigate("about:blank");
                SetState(RdpConnectionState.Disconnected, $"Navegação bloqueada fora do escopo do Guacamole ({ConnectionReference.DisplayName}).");
            }
        }
        catch
        {
            // Ignora falhas de acesso ao Source durante o descarte.
        }
    }

    private void CoreWebView2_WebResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs eventArgs)
    {
        if (!CanNavigate())
        {
            return;
        }

        try
        {
            int statusCode = eventArgs.Response.StatusCode;
            if (statusCode != 200)
            {
                return;
            }

            string requestUri = eventArgs.Request.Uri;
            if (IsTunnelProtocolUri(ConnectionReference.BaseUri, requestUri))
            {
                SetState(
                    RdpConnectionState.LoggedIn,
                    $"Sessão Guacamole ativa em {ConnectionReference.DisplayName}.");
                ScheduleResizeNotification();
                return;
            }

            if (string.Equals(eventArgs.Request.Method, "POST", StringComparison.OrdinalIgnoreCase)
                && IsTokensUri(ConnectionReference.BaseUri, requestUri))
            {
                string currentSource = _webView.CoreWebView2!.Source;
                if (!currentSource.EndsWith(TargetClientUri.Fragment, StringComparison.Ordinal))
                {
                    _webView.CoreWebView2.Navigate(TargetClientUri.AbsoluteUri);
                }
            }
        }
        catch
        {
            // Protege contra ObjectDisposedException/COMException em Dispose concorrente.
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;

        try
        {
            if (CanNavigate() && IsAllowedUri(ConnectionReference.BaseUri, eventArgs.Uri))
            {
                _webView.CoreWebView2!.Navigate(eventArgs.Uri);
            }
        }
        catch
        {
            // Ignora falhas de navegação em janelas interceptadas durante o descarte.
        }
    }

    private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs eventArgs)
    {
        eventArgs.State = eventArgs.PermissionKind switch
        {
            CoreWebView2PermissionKind.ClipboardRead => CoreWebView2PermissionState.Default,
            CoreWebView2PermissionKind.Camera => CoreWebView2PermissionState.Deny,
            CoreWebView2PermissionKind.Microphone => CoreWebView2PermissionState.Deny,
            CoreWebView2PermissionKind.Geolocation => CoreWebView2PermissionState.Deny,
            CoreWebView2PermissionKind.Notifications => CoreWebView2PermissionState.Deny,
            CoreWebView2PermissionKind.OtherSensors => CoreWebView2PermissionState.Deny,
            _ => CoreWebView2PermissionState.Deny
        };
    }

    private void CoreWebView2_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        _connectionRequested = false;
        SetState(
            RdpConnectionState.Disconnected,
            $"O processo do WebView2 falhou ({eventArgs.ProcessFailedKind}).");
    }

    private void SetState(RdpConnectionState state, string message)
    {
        if (_disposed || IsDisposed || Disposing)
        {
            return;
        }

        if (ConnectionState == state && string.Equals(_lastStateMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        ConnectionState = state;
        _lastStateMessage = message;
        ConnectionStateChanged?.Invoke(this, new RdpConnectionStateChangedEventArgs(state, message));
    }
}
