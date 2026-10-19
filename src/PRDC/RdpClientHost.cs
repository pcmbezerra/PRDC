using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PRDC;

internal enum RdpConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    LoggedIn
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
    private const uint PrintWindowRenderFullContent = 0x00000002;
    private const int MinimumSessionDimension = 200;
    private const int LeftWindowsScanCode = 0x5B;
    private const int EnterScanCode = 0x1C;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private readonly System.Windows.Forms.Timer _stateTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 250 };
    private ConnectionPointCookie? _eventCookie;
    private RdpClientEventSink? _eventSink;
    private object? _client;
    private bool _connectionRequested;
    private bool _disposed;
    private bool _logOffPending;
    private string _lastStateMessage = string.Empty;
    private Size _lastSessionSize;

    public RdpClientHost() : base(MsRdpClient10NotSafeClassId)
    {
        Dock = DockStyle.Fill;
        _stateTimer.Tick += (_, _) => PollState();
        _resizeTimer.Tick += (_, _) => ApplySessionSize();
    }

    public event EventHandler<RdpConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public RdpConnectionState ConnectionState { get; private set; }

    public bool IsLogOffPending => _logOffPending;

    public string Server { get; private set; } = string.Empty;

    public Bitmap? CaptureThumbnail(Size maximumSize)
    {
        if (ConnectionState is RdpConnectionState.Disconnected or RdpConnectionState.Connecting
            || !IsHandleCreated
            || ClientSize.Width <= 0
            || ClientSize.Height <= 0
            || maximumSize.Width <= 0
            || maximumSize.Height <= 0)
        {
            return null;
        }

        using Bitmap source = new(ClientSize.Width, ClientSize.Height);
        using (Graphics graphics = Graphics.FromImage(source))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(Handle, hdc, PrintWindowRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        Size thumbnailSize = FitInside(source.Size, maximumSize);
        Bitmap thumbnail = new(thumbnailSize.Width, thumbnailSize.Height);
        using Graphics target = Graphics.FromImage(thumbnail);
        target.DrawImage(source, new Rectangle(Point.Empty, thumbnailSize));
        return thumbnail;
    }

    public void ConnectTo(string server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        if (ConnectionState != RdpConnectionState.Disconnected)
        {
            throw new InvalidOperationException("Já existe uma conexão RDP ativa.");
        }

        object client = EnsureClient();
        Server = server.Trim();
        Size sessionSize = GetSessionSize();

        SetProperty(client, "Server", Server);
        SetProperty(client, "DesktopWidth", sessionSize.Width);
        SetProperty(client, "DesktopHeight", sessionSize.Height);
        SetProperty(client, "ColorDepth", 32);
        SetOptionalProperty(client, "ConnectingText", $"Conectando a {Server}...");
        SetOptionalProperty(client, "ConnectedStatusText", $"Conectado a {Server}");
        SetOptionalProperty(client, "DisconnectedText", $"Desconectado de {Server}");
        object securedSettings = GetProperty(client, "SecuredSettings2")
            ?? throw new InvalidOperationException("O controle RDP não oferece as configurações de teclado necessárias.");
        SetProperty(securedSettings, "KeyboardHookMode", 1);
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
        _lastSessionSize = sessionSize;
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
        _resizeTimer.Stop();

        if (_client is not null)
        {
            int nativeState = ReadNativeState();
            if (nativeState != 0)
            {
                InvokeMethod(_client, "Disconnect");
            }
        }

        _connectionRequested = false;
        _lastSessionSize = Size.Empty;
        SetState(RdpConnectionState.Disconnected, "Sessão RDP desconectada.");
    }

    public async Task LogOffSessionAsync()
    {
        if (ConnectionState != RdpConnectionState.LoggedIn || string.IsNullOrWhiteSpace(Server))
        {
            throw new InvalidOperationException("A sessão precisa estar autenticada para fazer logoff.");
        }

        if (_logOffPending)
        {
            throw new InvalidOperationException("Já existe uma solicitação de logoff em andamento.");
        }

        _logOffPending = true;

        try
        {
            Select();
            Focus();
            await Task.Delay(100);

            SendRemoteKeys(
                [0, 0, -1, -1],
                [
                    CreateKeyData(LeftWindowsScanCode, extended: true),
                    CreateKeyData(0x13, extended: false),
                    CreateKeyData(0x13, extended: false),
                    CreateKeyData(LeftWindowsScanCode, extended: true)
                ]);

            await Task.Delay(750);

            SendRemoteKeys(
                [0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1, 0, -1],
                [
                    CreateKeyData(0x26, extended: false),
                    CreateKeyData(0x26, extended: false),
                    CreateKeyData(0x18, extended: false),
                    CreateKeyData(0x18, extended: false),
                    CreateKeyData(0x22, extended: false),
                    CreateKeyData(0x22, extended: false),
                    CreateKeyData(0x18, extended: false),
                    CreateKeyData(0x18, extended: false),
                    CreateKeyData(0x21, extended: false),
                    CreateKeyData(0x21, extended: false),
                    CreateKeyData(0x21, extended: false),
                    CreateKeyData(0x21, extended: false),
                    CreateKeyData(EnterScanCode, extended: false),
                    CreateKeyData(EnterScanCode, extended: false)
                ]);

            SetState(ConnectionState, $"Comando de logoff enviado para {Server}.");
        }
        finally
        {
            _logOffPending = false;
        }
    }

    private void SendRemoteKeys(short[] keyUp, int[] keyData)
    {
        if (keyUp.Length != keyData.Length)
        {
            throw new ArgumentException("Os vetores de estado e dados das teclas devem ter o mesmo tamanho.");
        }

        Select();
        Focus();
        if (!ContainsFocus)
        {
            throw new InvalidOperationException("Não foi possível direcionar o teclado para a sessão RDP.");
        }

        NativeInput[] inputs = new NativeInput[keyData.Length];
        for (int index = 0; index < keyData.Length; index++)
        {
            uint flags = KeyEventScanCode;
            if ((keyData[index] & (1 << 24)) != 0)
            {
                flags |= KeyEventExtendedKey;
            }

            if (keyUp[index] != 0)
            {
                flags |= KeyEventKeyUp;
            }

            inputs[index] = new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        ScanCode = (ushort)((keyData[index] >> 16) & 0xFF),
                        Flags = flags
                    }
                }
            };
        }

        int inputSize = Marshal.SizeOf<NativeInput>();
        if (Environment.Is64BitProcess && inputSize != 40)
        {
            throw new InvalidOperationException($"Estrutura INPUT inválida: {inputSize} bytes; esperado: 40 bytes.");
        }

        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"O Windows enviou {sent} de {inputs.Length} eventos de teclado para a sessão RDP. Erro Win32: {error}.");
        }
    }

    private static int CreateKeyData(int scanCode, bool extended)
    {
        uint keyData = 1u | ((uint)scanCode << 16);
        if (extended)
        {
            keyData |= 1u << 24;
        }

        return unchecked((int)keyData);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        if (ConnectionState is RdpConnectionState.Connected or RdpConnectionState.LoggedIn)
        {
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _stateTimer.Stop();
            _resizeTimer.Stop();

            if (ConnectionState != RdpConnectionState.Disconnected)
            {
                try
                {
                    DisconnectSession();
                }
                catch (Exception exception) when (IsInteropFailure(exception))
                {
                    // O controle pode já ter liberado o RCW durante o descarte.
                }
            }

            _client = null;
            _stateTimer.Dispose();
            _resizeTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void CreateSink()
    {
        base.CreateSink();
        _eventSink = new RdpClientEventSink(this);
        object client = GetOcx() ?? throw new InvalidOperationException(
            "Não foi possível associar os eventos do controle RDP do Windows.");
        _eventCookie = new ConnectionPointCookie(client, _eventSink, typeof(IRdpClientEvents));
    }

    protected override void DetachSink()
    {
        _eventCookie?.Disconnect();
        _eventCookie = null;
        _eventSink = null;
        base.DetachSink();
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
                1 when ConnectionState == RdpConnectionState.LoggedIn => RdpConnectionState.LoggedIn,
                1 => RdpConnectionState.Connected,
                2 => RdpConnectionState.Connecting,
                _ => RdpConnectionState.Disconnected
            };

            string message = state switch
            {
                RdpConnectionState.LoggedIn => $"Login concluído em {Server}.",
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
        if (ConnectionState == state && string.Equals(_lastStateMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        ConnectionState = state;
        _lastStateMessage = message;
        ConnectionStateChanged?.Invoke(this, new RdpConnectionStateChangedEventArgs(state, message));
    }

    private void OnConnecting()
    {
        SetState(RdpConnectionState.Connecting, $"Conectando a {Server}...");
    }

    private void OnConnected()
    {
        SetState(RdpConnectionState.Connected, $"Conectado a {Server}; aguardando login.");
        ScheduleSessionResize();
    }

    private void OnLoginComplete()
    {
        SetState(RdpConnectionState.LoggedIn, $"Login concluído em {Server}.");
        ScheduleSessionResize();
    }

    private void OnDisconnected(int reason)
    {
        _connectionRequested = false;
        _stateTimer.Stop();
        _resizeTimer.Stop();
        string suffix = reason == 0 ? string.Empty : $" (código {reason})";
        SetState(RdpConnectionState.Disconnected, $"Sessão RDP desconectada{suffix}.");
    }

    private void OnFatalError(int errorCode)
    {
        _connectionRequested = false;
        _stateTimer.Stop();
        _resizeTimer.Stop();
        SetState(RdpConnectionState.Disconnected, $"Erro fatal do controle RDP (código {errorCode}).");
    }

    private void OnLogonError(int errorCode)
    {
        SetState(RdpConnectionState.Connected, $"Falha de login em {Server} (código {errorCode}).");
    }

    private void ScheduleSessionResize()
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ApplySessionSize()
    {
        _resizeTimer.Stop();

        if (_client is null
            || ConnectionState is not (RdpConnectionState.Connected or RdpConnectionState.LoggedIn))
        {
            return;
        }

        Size sessionSize = GetSessionSize();
        if (sessionSize == _lastSessionSize)
        {
            return;
        }

        uint physicalWidth = GetPhysicalDimension(sessionSize.Width);
        uint physicalHeight = GetPhysicalDimension(sessionSize.Height);

        try
        {
            InvokeMethod(
                _client,
                "UpdateSessionDisplaySettings",
                (uint)sessionSize.Width,
                (uint)sessionSize.Height,
                physicalWidth,
                physicalHeight,
                0u,
                100u,
                100u);
            _lastSessionSize = sessionSize;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Servidores sem suporte mantêm o Smart Sizing configurado na conexão.
        }
    }

    private Size GetSessionSize()
    {
        return new Size(
            Math.Max(MinimumSessionDimension, ClientSize.Width),
            Math.Max(MinimumSessionDimension, ClientSize.Height));
    }

    private uint GetPhysicalDimension(int pixels)
    {
        return (uint)Math.Max(1, Math.Round(pixels * 25.4 / DeviceDpi));
    }

    private static Size FitInside(Size source, Size bounds)
    {
        double scale = Math.Min((double)bounds.Width / source.Width, (double)bounds.Height / source.Height);
        return new Size(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
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

    private static void InvokeMethod(object target, string methodName, params object[] args)
    {
        target.GetType().InvokeMember(
            methodName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args.Length == 0 ? null : args,
            CultureInfo.InvariantCulture);
    }

    private static bool IsInteropFailure(Exception exception)
    {
        return exception is COMException
            or InvalidComObjectException
            or MissingMethodException
            or TargetInvocationException;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr targetDeviceContext, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] NativeInput[] inputs,
        int sizeOfInput);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;

        [FieldOffset(0)]
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [ComImport]
    [Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IRdpClientEvents
    {
        [DispId(1)]
        void OnConnecting();

        [DispId(2)]
        void OnConnected();

        [DispId(3)]
        void OnLoginComplete();

        [DispId(4)]
        void OnDisconnected(int reason);

        [DispId(10)]
        void OnFatalError(int errorCode);

        [DispId(22)]
        void OnLogonError(int errorCode);

    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class RdpClientEventSink(RdpClientHost owner) : StandardOleMarshalObject, IRdpClientEvents
    {
        public void OnConnecting() => owner.OnConnecting();

        public void OnConnected() => owner.OnConnected();

        public void OnLoginComplete() => owner.OnLoginComplete();

        public void OnDisconnected(int reason) => owner.OnDisconnected(reason);

        public void OnFatalError(int errorCode) => owner.OnFatalError(errorCode);

        public void OnLogonError(int errorCode) => owner.OnLogonError(errorCode);
    }
}
