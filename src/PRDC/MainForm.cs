using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace PRDC;

internal sealed class MainForm : Form
{
    private const int ConnectionPaneWidth = 320;
    private const int AutoHideConnectionPaneWidth = 34;
    private const int SessionTabCloseButtonSize = 16;
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        HideSelection = false,
        LabelEdit = false
    };
    private readonly Label _nodeTitle = new()
    {
        AutoSize = true,
        Font = new Font(
            SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
            18,
            FontStyle.Bold)
    };
    private readonly Label _nodeType = new() { AutoSize = true };
    private readonly Label _nodeHost = new() { AutoSize = true };
    private readonly Label _connectionState = new() { AutoSize = true };
    private readonly Label _thumbnailTitle = new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 18, 0, 6),
        Text = "Miniatura da sessão"
    };
    private readonly PictureBox _thumbnail = new()
    {
        BackColor = Color.Black,
        BorderStyle = BorderStyle.FixedSingle,
        Size = new Size(640, 360),
        SizeMode = PictureBoxSizeMode.CenterImage
    };
    private readonly Label _thumbnailStatus = new() { AutoSize = true, Text = "Sem sessão ativa." };
    private readonly System.Windows.Forms.Timer _thumbnailTimer = new() { Interval = 1000 };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripMenuItem _saveMenuItem = new("&Salvar");
    private readonly ToolStripMenuItem _saveAsMenuItem = new("Salvar &como...");
    private readonly ToolStripMenuItem _addGroupMenuItem = new("Adicionar &grupo...");
    private readonly ToolStripMenuItem _addServerMenuItem = new("Adicionar &servidor...");
    private readonly ToolStripMenuItem _editMenuItem = new("&Editar...");
    private readonly ToolStripMenuItem _removeMenuItem = new("&Remover");
    private readonly ToolStripMenuItem _connectMenuItem = new("&Conectar");
    private readonly ToolStripMenuItem _reconnectMenuItem = new("&Reconectar");
    private readonly ToolStripMenuItem _disconnectMenuItem = new("&Desconectar");
    private readonly ToolStripMenuItem _logOffMenuItem = new("Fazer &logoff");
    private readonly ToolStripMenuItem _closeSessionTabMenuItem = new("&Fechar aba");
    private readonly ToolStripMenuItem _showConnectionPaneMenuItem = new("&Mostrar");
    private readonly ToolStripMenuItem _hideConnectionPaneMenuItem = new("&Ocultar");
    private readonly ToolStripMenuItem _autoHideConnectionPaneMenuItem = new("Ocultar &automaticamente");
    private readonly ToolStripButton _connectButton = new("Conectar");
    private readonly ToolStripButton _disconnectButton = new("Desconectar");
    private readonly TabControl _workspaceTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _sessionPage = new("Sessões") { Padding = new Padding(3) };
    private readonly Panel _sessionPanel = new() { Dock = DockStyle.Fill };
    private readonly TabControl _sessionTabs = new()
    {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        Padding = new Point(16, 4)
    };
    private readonly Label _sessionPlaceholder = new()
    {
        Dock = DockStyle.Fill,
        Text = "Selecione um servidor e use Sessão > Conectar.",
        TextAlign = ContentAlignment.MiddleCenter
    };
    private readonly Button _autoHideConnectionPaneButton = new()
    {
        AccessibleName = "Mostrar lista de conexões",
        Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat,
        Text = "›",
        Visible = false
    };
    private readonly System.Windows.Forms.Timer _autoHideTimer = new() { Interval = 500 };
    private readonly Dictionary<XElement, RdpSession> _sessions = [];
    private RdgDocument _document = RdgDocument.CreateNew();
    private SplitContainer? _workspaceSplit;
    private ConnectionPaneMode _connectionPaneMode = ConnectionPaneMode.Visible;
    private int _expandedConnectionPaneWidth = ConnectionPaneWidth;
    private bool _populatingTree;

    public MainForm()
    {
        Text = "PRDC";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1200, 760);

        MainMenuStrip = BuildMenu();
        Controls.Add(BuildWorkspace());
        _workspaceSplit!.Panel2MinSize = 300;
        Controls.Add(BuildToolStrip());
        Controls.Add(MainMenuStrip);
        Controls.Add(new StatusStrip { Items = { _statusLabel } });

        _tree.AfterSelect += (_, _) => UpdateSelection();
        _tree.AfterExpand += (_, eventArgs) => UpdateExpanded(eventArgs.Node, true);
        _tree.AfterCollapse += (_, eventArgs) => UpdateExpanded(eventArgs.Node, false);
        _tree.NodeMouseDoubleClick += (_, _) => ActivateSelectedNode();
        _tree.ContextMenuStrip = BuildContextMenu();
        _thumbnailTimer.Tick += (_, _) => UpdateThumbnails();
        _sessionTabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateSelection();
            UpdateCommands();
        };
        _sessionTabs.DrawItem += DrawSessionTab;
        _sessionTabs.MouseDown += OnSessionTabMouseDown;
        _autoHideConnectionPaneButton.MouseEnter += (_, _) => ExpandAutoHideConnectionPane();
        _autoHideConnectionPaneButton.Click += (_, _) => ExpandAutoHideConnectionPane();
        _autoHideTimer.Tick += (_, _) => UpdateAutoHideConnectionPane();

        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
        PopulateTree();
        UpdateTitle();
        SetStatus("Pronto.");
    }

    private MenuStrip BuildMenu()
    {
        MenuStrip menu = new();

        ToolStripMenuItem file = new("&Arquivo");
        ToolStripMenuItem newItem = new("&Novo", null, (_, _) => NewDocument())
        {
            ShortcutKeys = Keys.Control | Keys.N
        };
        ToolStripMenuItem openItem = new("&Abrir...", null, (_, _) => OpenDocument())
        {
            ShortcutKeys = Keys.Control | Keys.O
        };
        _saveMenuItem.Click += (_, _) => SaveDocument();
        _saveMenuItem.ShortcutKeys = Keys.Control | Keys.S;
        _saveAsMenuItem.Click += (_, _) => SaveDocumentAs();
        ToolStripMenuItem exitItem = new("Sai&r", null, (_, _) => Close())
        {
            ShortcutKeys = Keys.Alt | Keys.F4
        };
        file.DropDownItems.AddRange([
            newItem, openItem, new ToolStripSeparator(), _saveMenuItem, _saveAsMenuItem,
            new ToolStripSeparator(), exitItem
        ]);

        ToolStripMenuItem edit = new("&Editar");
        _addGroupMenuItem.Click += (_, _) => AddGroup();
        _addGroupMenuItem.ShortcutKeys = Keys.Control | Keys.G;
        _addServerMenuItem.Click += (_, _) => AddServer();
        _addServerMenuItem.ShortcutKeys = Keys.Control | Keys.A;
        _editMenuItem.Click += (_, _) => EditSelectedNode();
        _editMenuItem.ShortcutKeys = Keys.Alt | Keys.Enter;
        _removeMenuItem.Click += (_, _) => RemoveSelectedNode();
        _removeMenuItem.ShortcutKeys = Keys.Delete;
        edit.DropDownItems.AddRange([
            _addGroupMenuItem, _addServerMenuItem, new ToolStripSeparator(),
            _editMenuItem, _removeMenuItem
        ]);
        edit.DropDownOpening += (_, _) => UpdateCommands();

        ToolStripMenuItem view = new("E&xibir");
        ToolStripMenuItem connectionPane = new("Lista de &conexões");
        _showConnectionPaneMenuItem.Click += (_, _) => SetConnectionPaneMode(ConnectionPaneMode.Visible);
        _hideConnectionPaneMenuItem.Click += (_, _) => SetConnectionPaneMode(ConnectionPaneMode.Hidden);
        _autoHideConnectionPaneMenuItem.Click += (_, _) => SetConnectionPaneMode(ConnectionPaneMode.AutoHide);
        connectionPane.DropDownItems.AddRange([
            _showConnectionPaneMenuItem,
            _hideConnectionPaneMenuItem,
            _autoHideConnectionPaneMenuItem
        ]);
        view.DropDownItems.Add(connectionPane);
        UpdateConnectionPaneMenu();

        ToolStripMenuItem session = new("&Sessão");
        _connectMenuItem.Click += (_, _) => ConnectSelectedServer();
        _connectMenuItem.ShortcutKeys = Keys.Control | Keys.Enter;
        _reconnectMenuItem.Click += (_, _) => ReconnectSession();
        _reconnectMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.R;
        _disconnectMenuItem.Click += (_, _) => DisconnectSession();
        _disconnectMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.D;
        _logOffMenuItem.Click += (_, _) => LogOffSession();
        _closeSessionTabMenuItem.Click += (_, _) => CloseCurrentSessionTab();
        _closeSessionTabMenuItem.ShortcutKeys = Keys.Control | Keys.W;
        session.DropDownItems.AddRange([
            _connectMenuItem,
            _reconnectMenuItem,
            _disconnectMenuItem,
            _logOffMenuItem,
            new ToolStripSeparator(),
            _closeSessionTabMenuItem
        ]);
        session.DropDownOpening += (_, _) => UpdateCommands();

        ToolStripMenuItem help = new("A&juda");
        help.DropDownItems.Add(new ToolStripMenuItem("&Sobre", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([file, edit, view, session, help]);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        ToolStrip toolStrip = new() { GripStyle = ToolStripGripStyle.Hidden };
        toolStrip.Items.Add(new ToolStripButton("Novo", null, (_, _) => NewDocument()));
        toolStrip.Items.Add(new ToolStripButton("Abrir", null, (_, _) => OpenDocument()));
        toolStrip.Items.Add(new ToolStripButton("Salvar", null, (_, _) => SaveDocument()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("Grupo", null, (_, _) => AddGroup()));
        toolStrip.Items.Add(new ToolStripButton("Servidor", null, (_, _) => AddServer()));
        toolStrip.Items.Add(new ToolStripButton("Editar", null, (_, _) => EditSelectedNode()));
        toolStrip.Items.Add(new ToolStripButton("Remover", null, (_, _) => RemoveSelectedNode()));
        toolStrip.Items.Add(new ToolStripSeparator());
        _connectButton.Click += (_, _) => ConnectSelectedServer();
        _disconnectButton.Click += (_, _) => DisconnectSession();
        toolStrip.Items.Add(_connectButton);
        toolStrip.Items.Add(_disconnectButton);
        return toolStrip;
    }

    private Control BuildWorkspace()
    {
        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = ConnectionPaneWidth,
            Panel1MinSize = AutoHideConnectionPaneWidth,
            FixedPanel = FixedPanel.Panel1
        };
        _workspaceSplit.Panel1.Padding = new Padding(6);
        _workspaceSplit.Panel1.Controls.Add(_tree);
        _workspaceSplit.Panel1.Controls.Add(_autoHideConnectionPaneButton);
        _workspaceSplit.SplitterMoved += (_, _) =>
        {
            if (_connectionPaneMode != ConnectionPaneMode.Hidden
                && _tree.Visible
                && _workspaceSplit.SplitterDistance > AutoHideConnectionPaneWidth)
            {
                _expandedConnectionPaneWidth = _workspaceSplit.SplitterDistance;
            }
        };

        Panel details = new() { Dock = DockStyle.Fill, Padding = new Padding(28) };
        FlowLayoutPanel detailLayout = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        detailLayout.Controls.Add(_nodeTitle);
        detailLayout.Controls.Add(_nodeType);
        detailLayout.Controls.Add(_nodeHost);
        detailLayout.Controls.Add(_connectionState);
        detailLayout.Controls.Add(_thumbnailTitle);
        detailLayout.Controls.Add(_thumbnail);
        detailLayout.Controls.Add(_thumbnailStatus);
        details.Controls.Add(detailLayout);

        TabPage detailsPage = new("Detalhes") { Padding = new Padding(3) };
        detailsPage.Controls.Add(details);
        _sessionPanel.Controls.Add(_sessionPlaceholder);
        _sessionPage.Controls.Add(_sessionPanel);
        _workspaceTabs.TabPages.Add(detailsPage);
        _workspaceTabs.TabPages.Add(_sessionPage);
        _workspaceSplit.Panel2.Controls.Add(_workspaceTabs);
        return _workspaceSplit;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new();
        ToolStripItem connect = menu.Items.Add("Conectar", null, (_, _) => ConnectSelectedServer());
        ToolStripItem disconnect = menu.Items.Add("Desconectar", null, (_, _) => DisconnectSession());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripItem addGroup = menu.Items.Add("Adicionar grupo...", null, (_, _) => AddGroup());
        ToolStripItem addServer = menu.Items.Add("Adicionar servidor...", null, (_, _) => AddServer());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripItem edit = menu.Items.Add("Editar...", null, (_, _) => EditSelectedNode());
        ToolStripItem remove = menu.Items.Add("Remover", null, (_, _) => RemoveSelectedNode());
        menu.Opening += (_, _) =>
        {
            UpdateCommands();
            connect.Enabled = _connectMenuItem.Enabled;
            disconnect.Enabled = _disconnectMenuItem.Enabled;
            addGroup.Enabled = _addGroupMenuItem.Enabled;
            addServer.Enabled = _addServerMenuItem.Enabled;
            edit.Enabled = _editMenuItem.Enabled;
            remove.Enabled = _removeMenuItem.Enabled;
        };
        return menu;
    }

    private void NewDocument()
    {
        if (!ConfirmCloseSessions() || !ConfirmDiscardChanges())
        {
            return;
        }

        _document = RdgDocument.CreateNew();
        PopulateTree();
        UpdateTitle();
        SetStatus("Novo arquivo RDG criado.");
    }

    private void OpenDocument()
    {
        if (!ConfirmCloseSessions() || !ConfirmDiscardChanges())
        {
            return;
        }

        using OpenFileDialog dialog = new()
        {
            Filter = "RDCMan Groups (*.rdg)|*.rdg|Arquivos XML (*.xml)|*.xml|Todos os arquivos (*.*)|*.*",
            Title = "Abrir arquivo RDG"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _document = RdgDocument.Load(dialog.FileName);
            PopulateTree();
            UpdateTitle();
            SetStatus($"Aberto: {dialog.FileName}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao abrir RDG", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SaveDocument()
    {
        return _document.FilePath is null ? SaveDocumentAs() : SaveTo(_document.FilePath);
    }

    private bool SaveDocumentAs()
    {
        using SaveFileDialog dialog = new()
        {
            Filter = "RDCMan Groups (*.rdg)|*.rdg|Arquivos XML (*.xml)|*.xml",
            DefaultExt = "rdg",
            AddExtension = true,
            Title = "Salvar arquivo RDG",
            FileName = _document.FilePath is null ? "PRDC.rdg" : Path.GetFileName(_document.FilePath)
        };

        return dialog.ShowDialog(this) == DialogResult.OK && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string filePath)
    {
        try
        {
            _document.Save(filePath);
            UpdateTitle();
            SetStatus($"Salvo: {filePath}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao salvar RDG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void AddGroup()
    {
        RdgNode? parent = GetContainerNode();
        if (parent is null)
        {
            return;
        }

        using NodeDialog dialog = new(RdgNodeKind.Group, "Novo grupo");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RdgNode created = _document.AddGroup(parent, dialog.NodeName);
        RefreshAndSelect(created.Element);
        MarkChanged("Grupo adicionado.");
    }

    private void AddServer()
    {
        RdgNode? parent = GetContainerNode();
        if (parent is null)
        {
            return;
        }

        using NodeDialog dialog = new(RdgNodeKind.Server, "Novo servidor");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RdgNode created = _document.AddServer(parent, dialog.NodeName, dialog.HostName);
        RefreshAndSelect(created.Element);
        MarkChanged("Servidor adicionado.");
    }

    private void EditSelectedNode()
    {
        RdgNode? node = SelectedNode;
        if (node is null)
        {
            return;
        }

        using NodeDialog dialog = new(node.Kind, node.Name, node.HostName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (node.Kind == RdgNodeKind.Server)
        {
            _document.UpdateServer(node, dialog.NodeName, dialog.HostName);
            if (_sessions.TryGetValue(node.Element, out RdpSession? session))
            {
                session.DisplayName = dialog.NodeName;
                UpdateSessionTitle(session);
            }
        }
        else
        {
            _document.UpdateGroup(node, dialog.NodeName);
        }

        RefreshAndSelect(node.Element);
        MarkChanged("Item atualizado.");
    }

    private void ActivateSelectedNode()
    {
        if (SelectedNode?.Kind == RdgNodeKind.Server)
        {
            ConnectSelectedServer();
        }
        else
        {
            EditSelectedNode();
        }
    }

    private void ConnectSelectedServer()
    {
        RdgNode? server = SelectedNode;
        if (server?.Kind != RdgNodeKind.Server || string.IsNullOrWhiteSpace(server.HostName))
        {
            return;
        }

        try
        {
            _workspaceTabs.SelectedTab = _sessionPage;
            RdpSession session = GetOrCreateSession(server);
            _sessionTabs.SelectedTab = session.Page;
            UpdateConnectionViews();

            if (IsSessionActive(session))
            {
                SetStatus($"A sessão de {server.Name} já está ativa.");
                return;
            }

            session.Host.ConnectTo(server.HostName);
            UpdateCommands();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            MessageBox.Show(
                this,
                exception.InnerException?.Message ?? exception.Message,
                "Erro ao conectar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("Falha ao iniciar a conexão RDP.");
            UpdateCommands();
        }
    }

    private void DisconnectSession()
    {
        RdpSession? session = GetCommandSession();
        if (session is null || !IsSessionActive(session))
        {
            return;
        }

        try
        {
            session.Host.DisconnectSession();
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            MessageBox.Show(
                this,
                exception.InnerException?.Message ?? exception.Message,
                "Erro ao desconectar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UpdateCommands();
        }
    }

    private void ReconnectSession()
    {
        RdpSession? session = GetCommandSession();
        if (session is null || !IsSessionActive(session) || string.IsNullOrWhiteSpace(session.Host.Server))
        {
            return;
        }

        string server = session.Host.Server;
        RdpClientHost previousHost = session.Host;
        previousHost.ConnectionStateChanged -= OnRdpConnectionStateChanged;

        try
        {
            session.ReplaceHost();
            session.Host.ConnectionStateChanged += OnRdpConnectionStateChanged;
            session.Host.ConnectTo(server);
            SetStatus($"Reconectando a {session.DisplayName}...");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            session.Host.ConnectionStateChanged -= OnRdpConnectionStateChanged;
            MessageBox.Show(
                this,
                exception.InnerException?.Message ?? exception.Message,
                "Erro ao reconectar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("Falha ao reconectar a sessão RDP.");
        }
        finally
        {
            UpdateSessionTitles();
            UpdateConnectionViews();
            UpdateCommands();
        }
    }

    private async void LogOffSession()
    {
        RdpSession? session = GetCommandSession();
        if (session?.Host.ConnectionState != RdpConnectionState.LoggedIn)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            $"Fazer logoff da sessão Windows em {session.DisplayName}? Aplicativos abertos no servidor serão encerrados.",
            "Confirmar logoff",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetStatus($"Solicitando logoff em {session.DisplayName}...");
            Task operation = session.Host.LogOffSessionAsync();
            UpdateCommands();
            await operation;
            SetStatus($"Logoff solicitado em {session.DisplayName}.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or InvalidCastException)
        {
            string message = exception is System.Runtime.InteropServices.COMException
                ? $"{exception.Message}{Environment.NewLine}{Environment.NewLine}HRESULT: 0x{exception.HResult:X8}"
                : exception.Message;
            MessageBox.Show(
                this,
                message,
                "Erro ao fazer logoff",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("Falha ao fazer logoff da sessão remota.");
        }
        finally
        {
            UpdateCommands();
        }
    }

    private void CloseCurrentSessionTab()
    {
        RdpSession? session = _sessionTabs.SelectedTab?.Tag as RdpSession;
        if (session is null)
        {
            return;
        }

        if (IsSessionActive(session))
        {
            DialogResult result = MessageBox.Show(
                this,
                $"A sessão de {session.DisplayName} será desconectada e a aba será fechada. Continuar?",
                "Fechar aba",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }
        }

        CloseSession(session);
        UpdateConnectionViews();
        UpdateCommands();
    }

    private void DrawSessionTab(object? sender, DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0 || eventArgs.Index >= _sessionTabs.TabPages.Count)
        {
            return;
        }

        TabPage page = _sessionTabs.TabPages[eventArgs.Index];
        bool selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color background = selected ? SystemColors.Window : SystemColors.Control;
        using (SolidBrush backgroundBrush = new(background))
        {
            eventArgs.Graphics.FillRectangle(backgroundBrush, eventArgs.Bounds);
        }

        Rectangle closeButton = GetSessionTabCloseButtonBounds(eventArgs.Bounds);
        Rectangle textBounds = Rectangle.FromLTRB(
            eventArgs.Bounds.Left + 6,
            eventArgs.Bounds.Top,
            closeButton.Left - 3,
            eventArgs.Bounds.Bottom);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            page.Text,
            Font,
            textBounds,
            SystemColors.ControlText,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis);

        const int glyphInset = 4;
        using Pen closePen = new(SystemColors.ControlText, 1.5F);
        eventArgs.Graphics.DrawLine(
            closePen,
            closeButton.Left + glyphInset,
            closeButton.Top + glyphInset,
            closeButton.Right - glyphInset - 1,
            closeButton.Bottom - glyphInset - 1);
        eventArgs.Graphics.DrawLine(
            closePen,
            closeButton.Right - glyphInset - 1,
            closeButton.Top + glyphInset,
            closeButton.Left + glyphInset,
            closeButton.Bottom - glyphInset - 1);

        if (selected)
        {
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, eventArgs.Bounds);
        }
    }

    private void OnSessionTabMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        for (int index = 0; index < _sessionTabs.TabPages.Count; index++)
        {
            Rectangle tabBounds = _sessionTabs.GetTabRect(index);
            if (!GetSessionTabCloseButtonBounds(tabBounds).Contains(eventArgs.Location))
            {
                continue;
            }

            _sessionTabs.SelectedIndex = index;
            CloseCurrentSessionTab();
            return;
        }
    }

    private static Rectangle GetSessionTabCloseButtonBounds(Rectangle tabBounds)
    {
        return new Rectangle(
            tabBounds.Right - SessionTabCloseButtonSize - 4,
            tabBounds.Top + ((tabBounds.Height - SessionTabCloseButtonSize) / 2),
            SessionTabCloseButtonSize,
            SessionTabCloseButtonSize);
    }

    private void OnRdpConnectionStateChanged(object? sender, RdpConnectionStateChangedEventArgs eventArgs)
    {
        RdpSession? session = _sessions.Values.FirstOrDefault(
            candidate => ReferenceEquals(candidate.Host, sender));
        if (session is null)
        {
            return;
        }

        SetStatus(eventArgs.Message);
        if (eventArgs.State == RdpConnectionState.Disconnected)
        {
            session.ClearThumbnail();
        }
        else if (eventArgs.State is RdpConnectionState.Connected or RdpConnectionState.LoggedIn)
        {
            UpdateThumbnail(session);
            _thumbnailTimer.Start();
        }

        if (!_sessions.Values.Any(IsSessionConnected))
        {
            _thumbnailTimer.Stop();
        }

        UpdateSessionTitles();
        UpdateConnectionViews();
        UpdateCommands();
    }

    private void RemoveSelectedNode()
    {
        RdgNode? node = SelectedNode;
        if (node is null || node.Kind == RdgNodeKind.File)
        {
            return;
        }

        string type = node.Kind == RdgNodeKind.Server ? "servidor" : "grupo";
        DialogResult result = MessageBox.Show(
            this,
            $"Remover o {type} '{node.Name}'?",
            "PRDC",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        CloseSessionsFor(node.Element);
        _document.Remove(node);
        PopulateTree();
        MarkChanged("Item removido.");
    }

    private void PopulateTree()
    {
        _populatingTree = true;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (RdgNode root in _document.Roots)
            {
                _tree.Nodes.Add(CreateTreeNode(root));
            }
        }
        finally
        {
            _tree.EndUpdate();
            _populatingTree = false;
        }

        if (_tree.Nodes.Count > 0)
        {
            _tree.SelectedNode = _tree.Nodes[0];
        }
        else
        {
            UpdateSelection();
        }
    }

    private TreeNode CreateTreeNode(RdgNode node)
    {
        TreeNode treeNode = new(GetTreeNodeText(node)) { Tag = node };
        foreach (RdgNode child in node.Children)
        {
            treeNode.Nodes.Add(CreateTreeNode(child));
        }

        if (node.IsExpanded)
        {
            treeNode.Expand();
        }

        return treeNode;
    }

    private void RefreshAndSelect(XElement element)
    {
        PopulateTree();
        TreeNode? treeNode = FindTreeNode(_tree.Nodes, element);
        if (treeNode is not null)
        {
            _tree.SelectedNode = treeNode;
            treeNode.EnsureVisible();
        }
    }

    private static TreeNode? FindTreeNode(TreeNodeCollection nodes, XElement element)
    {
        foreach (TreeNode treeNode in nodes)
        {
            if (treeNode.Tag is RdgNode node && ReferenceEquals(node.Element, element))
            {
                return treeNode;
            }

            TreeNode? child = FindTreeNode(treeNode.Nodes, element);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private RdgNode? GetContainerNode()
    {
        TreeNode? selectedTreeNode = _tree.SelectedNode;
        RdgNode? selected = selectedTreeNode?.Tag as RdgNode;
        if (selected?.Kind == RdgNodeKind.Server)
        {
            selected = selectedTreeNode?.Parent?.Tag as RdgNode;
        }

        return selected is { Kind: RdgNodeKind.File or RdgNodeKind.Group } ? selected : null;
    }

    private RdgNode? SelectedNode => _tree.SelectedNode?.Tag as RdgNode;

    private void UpdateSelection()
    {
        RdgNode? node = SelectedNode;
        _nodeTitle.Text = node?.Name ?? "Nenhum item selecionado";
        _nodeType.Text = node?.Kind switch
        {
            RdgNodeKind.File => "Arquivo RDG",
            RdgNodeKind.Group => "Grupo",
            RdgNodeKind.Server => "Servidor",
            _ => string.Empty
        };
        _nodeHost.Text = node?.Kind == RdgNodeKind.Server ? $"Host: {node.HostName}" : string.Empty;
        bool isServer = node?.Kind == RdgNodeKind.Server;
        RdpConnectionState state = isServer ? GetConnectionState(node!) : RdpConnectionState.Disconnected;
        RdpSession? session = isServer ? GetSession(node!) : null;
        _connectionState.Visible = isServer;
        _connectionState.Text = isServer ? $"Estado: {GetConnectionStateText(state)}" : string.Empty;
        _thumbnailTitle.Visible = isServer;
        _thumbnail.Visible = isServer;
        _thumbnailStatus.Visible = isServer;
        _thumbnailTitle.Text = session is not null
            ? $"Miniatura da sessão - {session.Host.Server}"
            : "Miniatura da sessão";
        _thumbnail.Image = session?.Thumbnail;
        _thumbnailStatus.Text = isServer
            ? state switch
            {
                RdpConnectionState.Connecting => "Aguardando a conexão para gerar a miniatura.",
                RdpConnectionState.Connected or RdpConnectionState.LoggedIn when session?.Thumbnail is null =>
                    "Capturando a sessão remota...",
                RdpConnectionState.Connected or RdpConnectionState.LoggedIn => "Atualizada automaticamente a cada segundo.",
                _ => "Sem sessão ativa."
            }
            : string.Empty;
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        RdgNode? selected = SelectedNode;
        bool container = GetContainerNode() is not null;
        _addGroupMenuItem.Enabled = container;
        _addServerMenuItem.Enabled = container;
        _editMenuItem.Enabled = selected is not null;
        _removeMenuItem.Enabled = selected is { Kind: not RdgNodeKind.File };
        RdpSession? selectedSession = selected?.Kind == RdgNodeKind.Server ? GetSession(selected) : null;
        _connectMenuItem.Enabled = selected?.Kind == RdgNodeKind.Server
            && (selectedSession is null || !IsSessionActive(selectedSession));
        RdpSession? commandSession = GetCommandSession();
        _reconnectMenuItem.Enabled = commandSession is not null && IsSessionActive(commandSession);
        _disconnectMenuItem.Enabled = commandSession is not null && IsSessionActive(commandSession);
        _logOffMenuItem.Enabled = commandSession?.Host.ConnectionState == RdpConnectionState.LoggedIn
            && !commandSession.Host.IsLogOffPending;
        _closeSessionTabMenuItem.Enabled = _sessionTabs.SelectedTab?.Tag is RdpSession;
        _connectButton.Enabled = _connectMenuItem.Enabled;
        _disconnectButton.Enabled = _disconnectMenuItem.Enabled;
        _saveMenuItem.Enabled = true;
        _saveAsMenuItem.Enabled = true;
    }

    private void UpdateExpanded(TreeNode? treeNode, bool expanded)
    {
        if (!_populatingTree && treeNode?.Tag is RdgNode node)
        {
            _document.SetExpanded(node, expanded);
            UpdateTitle();
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_document.IsDirty)
        {
            return true;
        }

        DialogResult result = MessageBox.Show(
            this,
            "Salvar as alterações antes de continuar?",
            "PRDC",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => SaveDocument(),
            DialogResult.No => true,
            _ => false
        };
    }

    private bool ConfirmCloseSessions()
    {
        if (!_sessions.Values.Any(IsSessionActive))
        {
            CloseAllSessions();
            return true;
        }

        DialogResult result = MessageBox.Show(
            this,
            "Todas as sessões RDP serão desconectadas. Continuar?",
            "PRDC",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return false;
        }

        CloseAllSessions();
        return true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!ConfirmDiscardChanges())
        {
            eventArgs.Cancel = true;
            return;
        }

        DisconnectAllSessions();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _thumbnailTimer.Dispose();
        _autoHideTimer.Dispose();
        CloseAllSessions();
    }

    private void UpdateConnectionViews()
    {
        UpdateTreeConnectionStates(_tree.Nodes);
        UpdateSelection();
    }

    private void UpdateTreeConnectionStates(TreeNodeCollection nodes)
    {
        foreach (TreeNode treeNode in nodes)
        {
            if (treeNode.Tag is RdgNode node)
            {
                treeNode.Text = GetTreeNodeText(node);
            }

            UpdateTreeConnectionStates(treeNode.Nodes);
        }
    }

    private string GetTreeNodeText(RdgNode node)
    {
        return node.Kind == RdgNodeKind.Server
            ? $"{node.Name} [{GetConnectionStateText(GetConnectionState(node))}]"
            : node.Name;
    }

    private RdpConnectionState GetConnectionState(RdgNode server)
    {
        return GetSession(server)?.Host.ConnectionState ?? RdpConnectionState.Disconnected;
    }

    private RdpSession? GetSession(RdgNode server)
    {
        return _sessions.GetValueOrDefault(server.Element);
    }

    private static string GetConnectionStateText(RdpConnectionState state)
    {
        return state switch
        {
            RdpConnectionState.Connecting => "Conectando",
            RdpConnectionState.Connected => "Conectado",
            RdpConnectionState.LoggedIn => "Login concluído",
            _ => "Desconectado"
        };
    }

    private RdpSession GetOrCreateSession(RdgNode server)
    {
        if (_sessions.TryGetValue(server.Element, out RdpSession? existing))
        {
            existing.DisplayName = server.Name;
            UpdateSessionTitle(existing);
            return existing;
        }

        RdpSession session = new(server.Element, server.Name);
        session.Host.ConnectionStateChanged += OnRdpConnectionStateChanged;
        session.Page.Tag = session;
        _sessions.Add(server.Element, session);
        _sessionTabs.TabPages.Add(session.Page);

        if (!_sessionPanel.Controls.Contains(_sessionTabs))
        {
            _sessionPanel.Controls.Clear();
            _sessionPanel.Controls.Add(_sessionTabs);
        }

        UpdateSessionTitles();
        return session;
    }

    private RdpSession? GetCommandSession()
    {
        if (_sessionTabs.SelectedTab?.Tag is RdpSession currentSession)
        {
            return currentSession;
        }

        if (SelectedNode is { Kind: RdgNodeKind.Server } selected
            && GetSession(selected) is { } selectedSession
            && IsSessionActive(selectedSession))
        {
            return selectedSession;
        }

        return _sessionTabs.SelectedTab?.Tag as RdpSession;
    }

    private static bool IsSessionActive(RdpSession session)
    {
        return session.Host.ConnectionState is
            RdpConnectionState.Connecting or RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
    }

    private static bool IsSessionConnected(RdpSession session)
    {
        return session.Host.ConnectionState is RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
    }

    private void UpdateThumbnails()
    {
        foreach (RdpSession session in _sessions.Values.Where(IsSessionConnected).ToArray())
        {
            UpdateThumbnail(session);
        }

        UpdateSelection();
    }

    private void UpdateThumbnail(RdpSession session)
    {
        Bitmap? thumbnail = session.Host.CaptureThumbnail(_thumbnail.ClientSize);
        if (thumbnail is null)
        {
            return;
        }

        Bitmap? previous = session.Thumbnail;
        session.Thumbnail = thumbnail;
        if (SelectedNode is { Kind: RdgNodeKind.Server } selected
            && ReferenceEquals(selected.Element, session.ServerElement))
        {
            _thumbnail.Image = thumbnail;
            _thumbnailStatus.Text = "Atualizada automaticamente a cada segundo.";
        }

        previous?.Dispose();
    }

    private void UpdateSessionTitles()
    {
        foreach (RdpSession session in _sessions.Values)
        {
            UpdateSessionTitle(session);
        }

        int activeCount = _sessions.Values.Count(IsSessionActive);
        _sessionPage.Text = activeCount == 0 ? "Sessões" : $"Sessões ({activeCount})";
    }

    private void UpdateSessionTitle(RdpSession session)
    {
        session.Page.Text = $"{session.DisplayName} [{GetConnectionStateText(session.Host.ConnectionState)}]";
        _sessionTabs.Invalidate();
    }

    private void DisconnectAllSessions()
    {
        foreach (RdpSession session in _sessions.Values.Where(IsSessionActive).ToArray())
        {
            try
            {
                session.Host.DisconnectSession();
            }
            catch (Exception exception) when (
                exception is System.Runtime.InteropServices.COMException
                    or System.Runtime.InteropServices.InvalidComObjectException
                    or TargetInvocationException)
            {
                SetStatus($"Falha ao desconectar {session.DisplayName}: {exception.InnerException?.Message ?? exception.Message}");
            }
        }
    }

    private void CloseSessionsFor(XElement element)
    {
        RdpSession[] sessions = _sessions.Values
            .Where(session => ReferenceEquals(session.ServerElement, element)
                || session.ServerElement.Ancestors().Any(ancestor => ReferenceEquals(ancestor, element)))
            .ToArray();

        foreach (RdpSession session in sessions)
        {
            CloseSession(session);
        }
    }

    private void CloseAllSessions()
    {
        foreach (RdpSession session in _sessions.Values.ToArray())
        {
            CloseSession(session);
        }

        _thumbnailTimer.Stop();
        _thumbnail.Image = null;
        _sessionPanel.Controls.Clear();
        _sessionPanel.Controls.Add(_sessionPlaceholder);
        UpdateSessionTitles();
    }

    private void CloseSession(RdpSession session)
    {
        try
        {
            if (IsSessionActive(session))
            {
                session.Host.DisconnectSession();
            }
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            SetStatus($"Falha ao encerrar {session.DisplayName}: {exception.InnerException?.Message ?? exception.Message}");
        }

        session.Host.ConnectionStateChanged -= OnRdpConnectionStateChanged;
        _sessions.Remove(session.ServerElement);
        _sessionTabs.TabPages.Remove(session.Page);
        if (ReferenceEquals(_thumbnail.Image, session.Thumbnail))
        {
            _thumbnail.Image = null;
        }

        session.Dispose();
        if (_sessions.Count == 0)
        {
            _thumbnailTimer.Stop();
            _sessionPanel.Controls.Clear();
            _sessionPanel.Controls.Add(_sessionPlaceholder);
        }

        UpdateSessionTitles();
    }

    private void SetConnectionPaneMode(ConnectionPaneMode mode)
    {
        _connectionPaneMode = mode;
        _autoHideTimer.Stop();

        if (_workspaceSplit is null)
        {
            return;
        }

        _workspaceSplit.Panel1Collapsed = mode == ConnectionPaneMode.Hidden;
        if (mode == ConnectionPaneMode.Hidden)
        {
            UpdateConnectionPaneMenu();
            return;
        }

        if (mode == ConnectionPaneMode.AutoHide)
        {
            CollapseAutoHideConnectionPane();
        }
        else
        {
            _tree.Visible = true;
            _autoHideConnectionPaneButton.Visible = false;
            _workspaceSplit.SplitterDistance = GetExpandedConnectionPaneWidth();
        }

        UpdateConnectionPaneMenu();
    }

    private void ExpandAutoHideConnectionPane()
    {
        if (_connectionPaneMode != ConnectionPaneMode.AutoHide || _workspaceSplit is null)
        {
            return;
        }

        _autoHideConnectionPaneButton.Visible = false;
        _tree.Visible = true;
        _workspaceSplit.SplitterDistance = GetExpandedConnectionPaneWidth();
        _autoHideTimer.Start();
    }

    private void CollapseAutoHideConnectionPane()
    {
        if (_workspaceSplit is null)
        {
            return;
        }

        if (_tree.Visible && _workspaceSplit.SplitterDistance > AutoHideConnectionPaneWidth)
        {
            _expandedConnectionPaneWidth = _workspaceSplit.SplitterDistance;
        }

        _tree.Visible = false;
        _autoHideConnectionPaneButton.Visible = true;
        _workspaceSplit.SplitterDistance = AutoHideConnectionPaneWidth;
        _autoHideTimer.Stop();
    }

    private void UpdateAutoHideConnectionPane()
    {
        if (_connectionPaneMode != ConnectionPaneMode.AutoHide
            || _workspaceSplit is null
            || !_tree.Visible
            || _tree.ContextMenuStrip?.Visible == true)
        {
            return;
        }

        Rectangle paneBounds = _workspaceSplit.Panel1.RectangleToScreen(_workspaceSplit.Panel1.ClientRectangle);
        if (!paneBounds.Contains(Cursor.Position))
        {
            CollapseAutoHideConnectionPane();
        }
    }

    private int GetExpandedConnectionPaneWidth()
    {
        if (_workspaceSplit is null)
        {
            return _expandedConnectionPaneWidth;
        }

        int maximum = Math.Max(
            AutoHideConnectionPaneWidth,
            _workspaceSplit.ClientSize.Width - _workspaceSplit.Panel2MinSize - _workspaceSplit.SplitterWidth);
        return Math.Clamp(_expandedConnectionPaneWidth, AutoHideConnectionPaneWidth, maximum);
    }

    private void UpdateConnectionPaneMenu()
    {
        _showConnectionPaneMenuItem.Checked = _connectionPaneMode == ConnectionPaneMode.Visible;
        _hideConnectionPaneMenuItem.Checked = _connectionPaneMode == ConnectionPaneMode.Hidden;
        _autoHideConnectionPaneMenuItem.Checked = _connectionPaneMode == ConnectionPaneMode.AutoHide;
    }

    private void MarkChanged(string status)
    {
        UpdateTitle();
        SetStatus(status);
    }

    private void UpdateTitle()
    {
        string fileName = _document.FilePath is null ? "Novo arquivo" : Path.GetFileName(_document.FilePath);
        Text = $"PRDC - {fileName}{(_document.IsDirty ? " *" : string.Empty)}";
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }

    private void ShowAbout()
    {
        using AboutDialog dialog = new(Icon);
        dialog.ShowDialog(this);
    }
}

internal enum ConnectionPaneMode
{
    Visible,
    Hidden,
    AutoHide
}

internal sealed class RdpSession : IDisposable
{
    public RdpSession(XElement serverElement, string displayName)
    {
        ServerElement = serverElement;
        DisplayName = displayName;
        Host = new RdpClientHost();
        Page = new TabPage(displayName) { Padding = new Padding(3) };
        Page.Controls.Add(Host);
    }

    public XElement ServerElement { get; }

    public string DisplayName { get; set; }

    public RdpClientHost Host { get; private set; }

    public TabPage Page { get; }

    public Bitmap? Thumbnail { get; set; }

    public void ClearThumbnail()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }

    public void ReplaceHost()
    {
        RdpClientHost previous = Host;
        Page.Controls.Remove(previous);
        Host = new RdpClientHost();
        Page.Controls.Add(Host);
        previous.Dispose();
        ClearThumbnail();
    }

    public void Dispose()
    {
        ClearThumbnail();
        Page.Dispose();
    }
}
