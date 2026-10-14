using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PRDC;

internal enum RdpConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

internal sealed class RdpConnectionStateChangedEventArgs(
    RdpConnectionState state,
    string message) : EventArgs
{
    public RdpConnectionState State { get; } = state;

    public string Message { get; } = message;
}

internal sealed class RdpClientHost : AxHost
{
    private const string MsRdpClient10NotSafeClassId = "A0C63C30-F08D-4AB4-907C-34905D770C7D";
    private readonly System.Windows.Forms.Timer _stateTimer = new() { Interval = 250 };
    private object? _client;
    private bool _connectionRequested;

    public RdpClientHost() : base(MsRdpClient10NotSafeClassId)
    {
        Dock = DockStyle.Fill;
        _stateTimer.Tick += (_, _) => PollState();
    }

    public event EventHandler<RdpConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public RdpConnectionState ConnectionState { get; private set; }

    public string Server { get; private set; } = string.Empty;

    public void ConnectTo(string server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        if (ConnectionState != RdpConnectionState.Disconnected)
        {
            throw new InvalidOperationException("Já existe uma conexão RDP ativa.");
        }

        object client = EnsureClient();
        Server = server.Trim();

        SetProperty(client, "Server", Server);
        SetProperty(client, "DesktopWidth", Math.Max(800, ClientSize.Width));
        SetProperty(client, "DesktopHeight", Math.Max(600, ClientSize.Height));
        SetProperty(client, "ColorDepth", 32);
        SetOptionalProperty(client, "ConnectingText", $"Conectando a {Server}...");
        SetOptionalProperty(client, "ConnectedStatusText", $"Conectado a {Server}");
        SetOptionalProperty(client, "DisconnectedText", $"Desconectado de {Server}");
        object? advancedSettings = GetOptionalProperty(client, "AdvancedSettings9");
        if (advancedSettings is not null)
        {
            SetOptionalProperty(advancedSettings, "SmartSizing", true);
            SetOptionalProperty(advancedSettings, "AuthenticationLevel", 2);
            SetOptionalProperty(advancedSettings, "EnableCredSspSupport", true);
            SetOptionalProperty(advancedSettings, "GrabFocusOnConnect", true);
            SetOptionalProperty(advancedSettings, "RedirectClipboard", true);
        }

        _connectionRequested = true;
        SetState(RdpConnectionState.Connecting, $"Conectando a {Server}...");

        try
        {
            InvokeMethod(client, "Connect");
            _stateTimer.Start();
        }
        catch
        {
            _connectionRequested = false;
            SetState(RdpConnectionState.Disconnected, $"Falha ao iniciar a conexão com {Server}.");
            throw;
        }
    }

    public void DisconnectSession()
    {
        _stateTimer.Stop();

        if (_client is not null)
        {
            int nativeState = ReadNativeState();
            if (nativeState != 0)
            {
                InvokeMethod(_client, "Disconnect");
            }
        }

        _connectionRequested = false;
        SetState(RdpConnectionState.Disconnected, "Sessão RDP desconectada.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateTimer.Stop();

            try
            {
                DisconnectSession();
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
                // O controle pode já ter encerrado sua conexão durante o descarte.
            }

            _stateTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private object EnsureClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        CreateControl();
        _client = GetOcx() ?? throw new InvalidOperationException(
            "Não foi possível inicializar o controle RDP do Windows.");
        return _client;
    }

    private void PollState()
    {
        try
        {
            int nativeState = ReadNativeState();
            RdpConnectionState state = nativeState switch
            {
                1 => RdpConnectionState.Connected,
                2 => RdpConnectionState.Connecting,
                _ => RdpConnectionState.Disconnected
            };

            string message = state switch
            {
                RdpConnectionState.Connected => $"Conectado a {Server}.",
                RdpConnectionState.Connecting => $"Conectando a {Server}...",
                _ when _connectionRequested => $"A conexão com {Server} foi encerrada.",
                _ => "Sessão RDP desconectada."
            };

            SetState(state, message);
            if (state == RdpConnectionState.Disconnected)
            {
                _connectionRequested = false;
                _stateTimer.Stop();
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            _connectionRequested = false;
            _stateTimer.Stop();
            SetState(RdpConnectionState.Disconnected, $"Erro no controle RDP: {exception.Message}");
        }
    }

    private int ReadNativeState()
    {
        if (_client is null)
        {
            return 0;
        }

        object? value = GetProperty(_client, "Connected");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private void SetState(RdpConnectionState state, string message)
    {
        if (ConnectionState == state && state != RdpConnectionState.Disconnected)
        {
            return;
        }

        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, new RdpConnectionStateChangedEventArgs(state, message));
    }

    private static object? GetOptionalProperty(object target, string propertyName)
    {
        try
        {
            return GetProperty(target, propertyName);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return null;
        }
    }

    private static void SetOptionalProperty(object target, string propertyName, object value)
    {
        try
        {
            SetProperty(target, propertyName, value);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Algumas propriedades variam entre versões do ActiveX.
        }
    }

    private static object? GetProperty(object target, string propertyName)
    {
        return target.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target,
            [value],
            CultureInfo.InvariantCulture);
    }

    private static void InvokeMethod(object target, string methodName)
    {
        target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);
    }

    private static bool IsInteropFailure(Exception exception)
    {
        return exception is COMException or MissingMethodException or TargetInvocationException;
    }
}
