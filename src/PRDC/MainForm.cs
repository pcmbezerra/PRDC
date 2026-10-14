using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace PRDC;

internal sealed class MainForm : Form
{
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
    private readonly ToolStripMenuItem _disconnectMenuItem = new("&Desconectar");
    private readonly ToolStripButton _connectButton = new("Conectar");
    private readonly ToolStripButton _disconnectButton = new("Desconectar");
    private readonly TabControl _workspaceTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _sessionPage = new("Sessão") { Padding = new Padding(3) };
    private readonly Panel _sessionPanel = new() { Dock = DockStyle.Fill };
    private readonly Label _sessionPlaceholder = new()
    {
        Dock = DockStyle.Fill,
        Text = "Selecione um servidor e use Sessão > Conectar.",
        TextAlign = ContentAlignment.MiddleCenter
    };
    private RdgDocument _document = RdgDocument.CreateNew();
    private RdpClientHost? _rdpHost;
    private XElement? _activeServerElement;
    private Bitmap? _latestThumbnail;
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
        Controls.Add(BuildToolStrip());
        Controls.Add(MainMenuStrip);
        Controls.Add(new StatusStrip { Items = { _statusLabel } });

        _tree.AfterSelect += (_, _) => UpdateSelection();
        _tree.AfterExpand += (_, eventArgs) => UpdateExpanded(eventArgs.Node, true);
        _tree.AfterCollapse += (_, eventArgs) => UpdateExpanded(eventArgs.Node, false);
        _tree.NodeMouseDoubleClick += (_, _) => ActivateSelectedNode();
        _tree.ContextMenuStrip = BuildContextMenu();
        _thumbnailTimer.Tick += (_, _) => UpdateThumbnail();

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

        ToolStripMenuItem session = new("&Sessão");
        _connectMenuItem.Click += (_, _) => ConnectSelectedServer();
        _connectMenuItem.ShortcutKeys = Keys.Control | Keys.Enter;
        _disconnectMenuItem.Click += (_, _) => DisconnectSession();
        _disconnectMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.D;
        session.DropDownItems.AddRange([_connectMenuItem, _disconnectMenuItem]);
        session.DropDownOpening += (_, _) => UpdateCommands();

        ToolStripMenuItem help = new("A&juda");
        help.DropDownItems.Add(new ToolStripMenuItem("&Sobre", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([file, edit, session, help]);
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
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 320,
            FixedPanel = FixedPanel.Panel1
        };
        split.Panel1.Padding = new Padding(6);
        split.Panel1.Controls.Add(_tree);

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
        split.Panel2.Controls.Add(_workspaceTabs);
        return split;
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
        if (!ConfirmDisconnectSession() || !ConfirmDiscardChanges())
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
        if (!ConfirmDisconnectSession() || !ConfirmDiscardChanges())
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

            if (_rdpHost is null)
            {
                _rdpHost = new RdpClientHost();
                _rdpHost.ConnectionStateChanged += OnRdpConnectionStateChanged;
                _sessionPanel.Controls.Clear();
                _sessionPanel.Controls.Add(_rdpHost);
            }

            _sessionPage.Text = $"Sessão - {server.Name}";
            _activeServerElement = server.Element;
            UpdateConnectionViews();
            _rdpHost.ConnectTo(server.HostName);
            UpdateCommands();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException or TargetInvocationException)
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
        if (_rdpHost is null)
        {
            return;
        }

        try
        {
            _rdpHost.DisconnectSession();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or TargetInvocationException)
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

    private void OnRdpConnectionStateChanged(object? sender, RdpConnectionStateChangedEventArgs eventArgs)
    {
        SetStatus(eventArgs.Message);
        if (eventArgs.State == RdpConnectionState.Disconnected)
        {
            _thumbnailTimer.Stop();
            ClearThumbnail();
            _activeServerElement = null;
            _sessionPage.Text = "Sessão";
        }
        else if (eventArgs.State is RdpConnectionState.Connected or RdpConnectionState.LoggedIn)
        {
            SelectActiveServer();
            UpdateThumbnail();
            _thumbnailTimer.Start();
        }

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
        _connectionState.Visible = isServer;
        _connectionState.Text = isServer ? $"Estado: {GetConnectionStateText(state)}" : string.Empty;
        bool hasActiveSession = _activeServerElement is not null
            && _rdpHost?.ConnectionState is
                RdpConnectionState.Connecting or RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
        bool showThumbnail = isServer || hasActiveSession;
        _thumbnailTitle.Visible = showThumbnail;
        _thumbnail.Visible = showThumbnail;
        _thumbnailStatus.Visible = showThumbnail;
        _thumbnailTitle.Text = hasActiveSession
            ? $"Miniatura da sessão ativa - {_rdpHost!.Server}"
            : "Miniatura da sessão";
        _thumbnail.Image = hasActiveSession ? _latestThumbnail : null;
        _thumbnailStatus.Text = hasActiveSession
            ? _rdpHost!.ConnectionState switch
            {
                RdpConnectionState.Connecting => "Aguardando a conexão para gerar a miniatura.",
                RdpConnectionState.Connected or RdpConnectionState.LoggedIn when _latestThumbnail is null =>
                    "Capturando a sessão remota...",
                RdpConnectionState.Connected or RdpConnectionState.LoggedIn => "Atualizada automaticamente a cada segundo.",
                _ => "Sem sessão ativa."
            }
            : isServer ? "Sem sessão ativa para este servidor." : string.Empty;
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
        bool sessionActive = _rdpHost?.ConnectionState is
            RdpConnectionState.Connecting or RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
        _connectMenuItem.Enabled = selected?.Kind == RdgNodeKind.Server && !sessionActive;
        _disconnectMenuItem.Enabled = sessionActive;
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

    private bool ConfirmDisconnectSession()
    {
        if (_rdpHost?.ConnectionState is not (
            RdpConnectionState.Connecting or RdpConnectionState.Connected or RdpConnectionState.LoggedIn))
        {
            return true;
        }

        DialogResult result = MessageBox.Show(
            this,
            "A sessão RDP atual será desconectada. Continuar?",
            "PRDC",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return false;
        }

        DisconnectSession();
        return true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!ConfirmDiscardChanges())
        {
            eventArgs.Cancel = true;
            return;
        }

        DisconnectSession();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _thumbnailTimer.Dispose();
        ClearThumbnail();
    }

    private void UpdateConnectionViews()
    {
        UpdateTreeConnectionStates(_tree.Nodes);
        UpdateSelection();
    }

    private void SelectActiveServer()
    {
        if (_activeServerElement is null)
        {
            return;
        }

        TreeNode? activeServer = FindTreeNode(_tree.Nodes, _activeServerElement);
        if (activeServer is null || ReferenceEquals(_tree.SelectedNode, activeServer))
        {
            return;
        }

        _tree.SelectedNode = activeServer;
        activeServer.EnsureVisible();
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
        return IsActiveServer(server)
            ? _rdpHost?.ConnectionState ?? RdpConnectionState.Disconnected
            : RdpConnectionState.Disconnected;
    }

    private bool IsActiveServer(RdgNode server)
    {
        return _activeServerElement is not null && ReferenceEquals(server.Element, _activeServerElement);
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

    private void UpdateThumbnail()
    {
        if (_rdpHost?.ConnectionState is not (RdpConnectionState.Connected or RdpConnectionState.LoggedIn))
        {
            return;
        }

        Bitmap? thumbnail = _rdpHost.CaptureThumbnail(_thumbnail.ClientSize);
        if (thumbnail is null)
        {
            return;
        }

        Bitmap? previous = _latestThumbnail;
        _latestThumbnail = thumbnail;
        _thumbnail.Image = thumbnail;
        _thumbnailStatus.Text = "Atualizada automaticamente a cada segundo.";

        previous?.Dispose();
    }

    private void ClearThumbnail()
    {
        _thumbnail.Image = null;
        _latestThumbnail?.Dispose();
        _latestThumbnail = null;
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
