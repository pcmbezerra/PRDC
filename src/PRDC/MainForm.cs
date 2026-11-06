using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace PRDC;

internal sealed class MainForm : Form
{
    private const int ConnectionPaneWidth = 220;
    private const int MinimumExpandedConnectionPaneWidth = 180;
    private const int AutoHideConnectionPaneWidth = 34;
    private const int SessionTabCloseButtonSize = 16;
    private const int MinimumSessionTabWidth = 76;
    private const int DefaultSessionTabWidth = 110;
    private const int SessionTabHorizontalChrome = 58;
    private const int StatusCircleDiameter = 10;
    private const int StatusCircleSpacing = 6;
    private static readonly Color DisconnectedStatusColor = Color.FromArgb(220, 53, 69);
    private static readonly Color AuthenticatingStatusColor = Color.FromArgb(245, 180, 0);
    private static readonly Color ConnectedStatusColor = Color.FromArgb(40, 167, 69);
    private static readonly Color InactiveConnectedStatusColor = Color.FromArgb(0, 120, 212);
    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        DrawMode = TreeViewDrawMode.OwnerDrawText,
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
    private readonly ToolStripMenuItem _saveAllMenuItem = new("Salvar &tudo");
    private readonly ToolStripMenuItem _closeFileMenuItem = new("&Fechar arquivo");
    private readonly ToolStripMenuItem _refreshGuacamoleMenuItem = new("&Atualizar lista do Guacamole");
    private readonly ToolStripMenuItem _removeGuacamoleMenuItem = new("&Remover inventário do Guacamole...");
    private readonly ToolStripMenuItem _addGroupMenuItem = new("Adicionar &grupo...");
    private readonly ToolStripMenuItem _addServerMenuItem = new("Adicionar &servidor...");
    private readonly ToolStripMenuItem _editMenuItem = new("&Editar...");
    private readonly ToolStripMenuItem _removeMenuItem = new("&Remover");
    private readonly ToolStripMenuItem _toggleFavoriteMenuItem = new("&Adicionar aos Favoritos");
    private readonly ToolStripMenuItem _goToSourceMenuItem = new("&Ir para a origem");
    private readonly ToolStripMenuItem _connectMenuItem = new("&Conectar");
    private readonly ToolStripMenuItem _reconnectMenuItem = new("&Reconectar");
    private readonly ToolStripMenuItem _disconnectMenuItem = new("&Desconectar");
    private readonly ToolStripMenuItem _logOffMenuItem = new("Fazer &logoff");
    private readonly ToolStripMenuItem _closeSessionTabMenuItem = new("&Fechar aba");
    private readonly ToolStripMenuItem _showConnectionPaneMenuItem = new("&Mostrar");
    private readonly ToolStripMenuItem _hideConnectionPaneMenuItem = new("&Ocultar");
    private readonly ToolStripMenuItem _autoHideConnectionPaneMenuItem = new("Ocultar &automaticamente");
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripMenuItem _fullScreenMenuItem = new("Tela &cheia");
    private readonly ToolStripMenuItem _lightThemeMenuItem = new("&Claro");
    private readonly ToolStripMenuItem _darkThemeMenuItem = new("&Escuro");
    private readonly ToolStripMenuItem _systemThemeMenuItem = new("&Usar tema do Windows");
    private bool _isFullScreen;
    private FormWindowState _previousWindowState = FormWindowState.Normal;
    private FormBorderStyle _previousBorderStyle = FormBorderStyle.Sizable;
    private Rectangle _previousBounds;
    private readonly TabControl _workspaceTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _detailsPage = new("Detalhes") { Padding = new Padding(3) };
    private readonly TabPage _sessionPage = new("Sessões") { Padding = new Padding(3) };
    private readonly Panel _sessionPanel = new() { Dock = DockStyle.Fill };
    private readonly TabControl _sessionTabs = new()
    {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(DefaultSessionTabWidth, 26)
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
    private readonly Dictionary<XElement, RdpSession> _sessions = new(ReferenceEqualityComparer.Instance);
    private readonly List<IConnectionSource> _sources = [];
    private IConnectionSource? _activeSource;
    private int _untitledCounter;
    private SplitContainer? _workspaceSplit;
    private ConnectionPaneMode _connectionPaneMode = ConnectionPaneMode.Visible;
    private int _expandedConnectionPaneWidth = ConnectionPaneWidth;
    private bool _populatingTree;
    private bool _startupInventoryLoadAttempted;
    private bool _applyingTheme;
    private readonly System.Windows.Forms.Timer _workspaceSaveDebounceTimer = new() { Interval = 500 };
    private Guid _currentWorkspaceId = Guid.NewGuid();
    private bool _isRestoringWorkspace;
    private bool _isWorkspaceRecoveryMode;
    private bool _workspaceLoadedFromDisk;
    private static readonly object FavoritesRootTag = new();
    private FavoriteManager _favoriteManager = new();
    private readonly SearchCatalogManager _searchCatalog = new();

    internal FavoriteManager FavoriteManager => _favoriteManager;

    internal SearchCatalogManager SearchCatalog => _searchCatalog;

    internal TreeView ConnectionTree => _tree;

    internal TabControl SessionTabs => _sessionTabs;

    internal IReadOnlyList<IConnectionSource> Sources => _sources;

    internal IConnectionSource? ActiveSource => _activeSource;

    internal IConnectionSource Source => _activeSource ?? (_sources.Count > 0 ? _sources[0] : null!);

    internal void SetSource(IConnectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ClearSources();
        RegisterSource(source);
        _activeSource = source;
    }

    internal void SetActiveSource(IConnectionSource? source)
    {
        if (!ReferenceEquals(_activeSource, source))
        {
            _activeSource = source;
            RequestWorkspaceSave();
        }
    }

    private void RegisterSource(IConnectionSource source)
    {
        _sources.Add(source);
        source.DirtyChanged += OnSourceDirtyChanged;
        _searchCatalog.IndexSource(source, _favoriteManager);
        RequestWorkspaceSave();
    }

    private void UnregisterSource(IConnectionSource source)
    {
        source.DirtyChanged -= OnSourceDirtyChanged;
        _sources.Remove(source);
        _searchCatalog.RemoveSource(source.Id);
        RequestWorkspaceSave();
    }

    private void ClearSources()
    {
        foreach (IConnectionSource s in _sources)
        {
            s.DirtyChanged -= OnSourceDirtyChanged;
            s.Dispose();
        }
        _sources.Clear();
        _searchCatalog.Clear();
    }

    internal void RegisterOrReplaceGuacamoleSource(GuacamoleInventorySource guacSource)
    {
        ArgumentNullException.ThrowIfNull(guacSource);

        GuacamoleInventorySource? existingGuac = _sources.OfType<GuacamoleInventorySource>()
            .FirstOrDefault(s => s.Id == guacSource.Id || string.Equals(s.DisplayName, guacSource.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (existingGuac is not null)
        {
            CloseSessionsForSource(existingGuac.Id);
            UnregisterSource(existingGuac);
            existingGuac.Dispose();
        }
        else if (_sources.Count == 1
            && _sources[0] is RdgFileSource initial
            && initial.Document.FilePath is null
            && !initial.IsDirty
            && initial.Roots.All(r => r.Children.Count == 0)
            && _sessions.Count == 0)
        {
            ClearSources();
        }

        RegisterSource(guacSource);
        _activeSource = guacSource;

        PopulateTree();
        UpdateTitle();
        UpdateCommands();
    }

    private void OnSourceDirtyChanged(object? sender, EventArgs e)
    {
        if (sender is IConnectionSource source)
        {
            UpdateSourceNodeText(source);
        }
        UpdateTitle();
        UpdateCommands();
    }

    public MainForm()
    {
        Text = "PRDC";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1200, 760);

        _untitledCounter = 1;
        RdgFileSource initialSource = RdgFileSource.CreateNew($"Sem título {_untitledCounter}");
        RegisterSource(initialSource);
        _activeSource = initialSource;

        MainMenuStrip = BuildMenu();
        Controls.Add(BuildWorkspace());
        _workspaceSplit!.Panel2MinSize = 300;
        _workspaceSplit.SplitterMoved += (_, _) => RequestWorkspaceSave();
        _workspaceSaveDebounceTimer.Tick += (_, _) => SaveCurrentWorkspace();
        ResizeEnd += (_, _) => RequestWorkspaceSave();
        Controls.Add(MainMenuStrip);
        _statusStrip.Items.Add(_statusLabel);
        Controls.Add(_statusStrip);

        _tree.DrawNode += DrawTreeNode;
        _tree.AfterSelect += (_, _) => UpdateSelection();
        _tree.AfterExpand += (_, eventArgs) => UpdateExpanded(eventArgs.Node, true);
        _tree.AfterCollapse += (_, eventArgs) => UpdateExpanded(eventArgs.Node, false);
        _tree.NodeMouseDoubleClick += (_, _) => ActivateSelectedNode();
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _tree.SelectedNode = e.Node;
            }
        };
        _tree.ContextMenuStrip = BuildContextMenu();
        _thumbnailTimer.Tick += (_, _) => UpdateThumbnails();
        _workspaceTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _workspaceTabs.DrawItem += DrawWorkspaceTab;
        _workspaceTabs.SelectedIndexChanged += (_, _) =>
        {
            _tree.Invalidate();
            InvalidateSessionTabHeaders();
            UpdateThumbnails();
            UpdateSelection();
        };
        _sessionTabs.SelectedIndexChanged += (_, _) =>
        {
            _tree.Invalidate();
            InvalidateSessionTabHeaders();
            if (_sessionTabs.SelectedTab?.Tag is RdpSession currentSession && currentSession.Context is not null)
            {
                IConnectionSource? sessionSource = _sources.FirstOrDefault(s => s.Id == currentSession.Context.SourceId);
                if (sessionSource is not null)
                {
                    _activeSource = sessionSource;
                }
            }
            UpdateSelection();
            UpdateCommands();
        };
        _sessionTabs.DrawItem += DrawSessionTab;
        _sessionTabs.MouseDown += OnSessionTabMouseDown;
        _sessionTabs.SizeChanged += (_, _) => UpdateSessionTabSizes();
        _autoHideConnectionPaneButton.MouseEnter += (_, _) => ExpandAutoHideConnectionPane();
        _autoHideConnectionPaneButton.Click += (_, _) => ExpandAutoHideConnectionPane();
        _autoHideTimer.Tick += (_, _) => UpdateAutoHideConnectionPane();

        ThemeManager.ThemeChanged += ApplyCurrentTheme;
        Shown += OnShown;
        SizeChanged += (_, _) => ClampConnectionPaneWidth();
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
        InitializeWorkspace();
        PopulateTree();
        UpdateTitle();
        SetStatus("Pronto.");
        ApplyCurrentTheme();
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
        _saveAllMenuItem.Click += (_, _) => SaveAllDocuments();
        _saveAllMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        _closeFileMenuItem.Click += (_, _) => CloseSelectedSource();

        ToolStripMenuItem loginGuacamoleItem = new("&Login no Guacamole...", null, (_, _) => LoginGuacamoleAsync())
        {
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.G
        };
        _refreshGuacamoleMenuItem.Click += (_, _) => RefreshGuacamoleAsync();
        _refreshGuacamoleMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.U;
        _removeGuacamoleMenuItem.Click += (_, _) => RemoveGuacamoleInventory();

        ToolStripMenuItem exitItem = new("Sai&r", null, (_, _) => Close())
        {
            ShortcutKeys = Keys.Alt | Keys.F4
        };
        file.DropDownItems.AddRange([
            newItem,
            openItem,
            _saveMenuItem,
            _saveAsMenuItem,
            _saveAllMenuItem,
            _closeFileMenuItem,
            new ToolStripSeparator(),
            loginGuacamoleItem,
            _refreshGuacamoleMenuItem,
            _removeGuacamoleMenuItem,
            new ToolStripSeparator(),
            exitItem
        ]);
        file.DropDownOpening += (_, _) => UpdateCommands();

        ToolStripMenuItem edit = new("&Editar");
        _addGroupMenuItem.Click += (_, _) => AddGroup();
        _addGroupMenuItem.ShortcutKeys = Keys.Control | Keys.G;
        _addServerMenuItem.Click += (_, _) => AddServer();
        _addServerMenuItem.ShortcutKeys = Keys.Control | Keys.A;
        _editMenuItem.Click += (_, _) => EditSelectedNode();
        _editMenuItem.ShortcutKeys = Keys.Alt | Keys.Enter;
        _removeMenuItem.Click += (_, _) => RemoveSelectedNode();
        _removeMenuItem.ShortcutKeys = Keys.Delete;
        _toggleFavoriteMenuItem.Click += (_, _) => ToggleFavoriteForSelectedNode();
        _toggleFavoriteMenuItem.ShortcutKeys = Keys.Control | Keys.D;
        _goToSourceMenuItem.Click += (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
            {
                GoToFavoriteSource(fav);
            }
        };

        ToolStripMenuItem quickOpenItem = new("&Busca rápida... (Quick Open)");
        quickOpenItem.ShortcutKeys = Keys.Control | Keys.P;
        quickOpenItem.Click += (_, _) => OpenQuickOpen();

        edit.DropDownItems.AddRange([
            _addGroupMenuItem, _addServerMenuItem, new ToolStripSeparator(),
            _editMenuItem, _removeMenuItem, new ToolStripSeparator(),
            _toggleFavoriteMenuItem, _goToSourceMenuItem, new ToolStripSeparator(),
            quickOpenItem
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

        _fullScreenMenuItem.Click += (_, _) => ToggleFullScreen();
        _fullScreenMenuItem.ShortcutKeys = Keys.F11;

        ToolStripMenuItem themeMenu = new("T&ema");
        _lightThemeMenuItem.Click += (_, _) => ThemeManager.SetTheme(AppTheme.Light);
        _darkThemeMenuItem.Click += (_, _) => ThemeManager.SetTheme(AppTheme.Dark);
        _systemThemeMenuItem.Click += (_, _) => ThemeManager.SetTheme(AppTheme.System);
        themeMenu.DropDownItems.AddRange([
            _lightThemeMenuItem,
            _darkThemeMenuItem,
            _systemThemeMenuItem
        ]);

        view.DropDownItems.AddRange([
            connectionPane,
            new ToolStripSeparator(),
            _fullScreenMenuItem,
            themeMenu
        ]);
        UpdateConnectionPaneMenu();
        UpdateThemeMenu();

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

        _detailsPage.Controls.Add(details);
        _sessionPanel.Controls.Add(_sessionPlaceholder);
        _sessionPage.Controls.Add(_sessionPanel);
        _workspaceTabs.TabPages.Add(_detailsPage);
        _workspaceTabs.TabPages.Add(_sessionPage);
        _workspaceSplit.Panel2.Controls.Add(_workspaceTabs);
        return _workspaceSplit;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        ContextMenuStrip menu = new();

        // 1. Itens para Nó Raiz ★ Favoritos
        ToolStripItem favExpandAll = menu.Items.Add("Expandir favoritos", null, (_, _) => FindFavoritesRootNode()?.ExpandAll());
        ToolStripItem favCollapseAll = menu.Items.Add("Recolher favoritos", null, (_, _) => FindFavoritesRootNode()?.Collapse());
        ToolStripSeparator favRootSep = new();
        menu.Items.Add(favRootSep);
        ToolStripItem favCleanOrphans = menu.Items.Add("Remover favoritos órfãos...", null, (_, _) => CleanOrphanFavorites());

        // 2. Itens para Nó de Favorito Individual
        ToolStripItem favConnect = menu.Items.Add("Conectar", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) _ = ConnectFavoriteAsync(fav);
        });
        ToolStripItem favGoToSource = menu.Items.Add("Ir para a origem (Ctrl+G)", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) GoToFavoriteSource(fav);
        });
        ToolStripSeparator favSep1 = new();
        menu.Items.Add(favSep1);
        ToolStripItem favRename = menu.Items.Add("Renomear favorito... (F2)", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) RenameFavoritePrompt(fav);
        });
        ToolStripItem favMoveUp = menu.Items.Add("Mover para cima", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) MoveFavoriteUp(fav);
        });
        ToolStripItem favMoveDown = menu.Items.Add("Mover para baixo", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) MoveFavoriteDown(fav);
        });
        ToolStripSeparator favSep2 = new();
        menu.Items.Add(favSep2);
        ToolStripItem favRemove = menu.Items.Add("Remover dos Favoritos (Del)", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav) RemoveFavoriteNode(fav);
        });

        // 3. Itens Regulares da Árvore RDG / Guacamole
        ToolStripItem connect = menu.Items.Add("Conectar", null, (_, _) => ConnectSelectedServer());
        ToolStripItem disconnect = menu.Items.Add("Desconectar", null, (_, _) => DisconnectSession());
        ToolStripSeparator regSep1 = new();
        menu.Items.Add(regSep1);
        ToolStripItem toggleFavRegular = menu.Items.Add("Adicionar aos Favoritos (Ctrl+D)", null, (_, _) => ToggleFavoriteForSelectedNode());
        ToolStripSeparator regSepFav = new();
        menu.Items.Add(regSepFav);
        ToolStripItem addGroup = menu.Items.Add("Adicionar grupo...", null, (_, _) => AddGroup());
        ToolStripItem addServer = menu.Items.Add("Adicionar servidor...", null, (_, _) => AddServer());
        ToolStripSeparator regSep2 = new();
        menu.Items.Add(regSep2);
        ToolStripItem edit = menu.Items.Add("Editar...", null, (_, _) => EditSelectedNode());
        ToolStripItem remove = menu.Items.Add("Remover", null, (_, _) => RemoveSelectedNode());
        ToolStripSeparator fileSeparator = new();
        menu.Items.Add(fileSeparator);
        ToolStripItem closeFile = menu.Items.Add("Fechar arquivo", null, (_, _) => CloseSelectedSource());
        ToolStripSeparator guacSeparator = new();
        menu.Items.Add(guacSeparator);
        ToolStripItem refreshGuac = menu.Items.Add("Atualizar portal...", null, (_, _) => RefreshGuacamoleAsync());
        ToolStripItem removeGuac = menu.Items.Add("Remover portal...", null, (_, _) => RemoveGuacamoleInventory());

        menu.Opening += (_, _) =>
        {
            UpdateCommands();

            bool isFavRoot = ReferenceEquals(_tree.SelectedNode?.Tag, FavoritesRootTag);
            FavoriteEntry? fav = _tree.SelectedNode?.Tag as FavoriteEntry;
            bool isFavItem = fav is not null;
            bool isRegularNode = !isFavRoot && !isFavItem;

            // Raiz ★ Favoritos
            favExpandAll.Visible = isFavRoot;
            favCollapseAll.Visible = isFavRoot;
            favRootSep.Visible = isFavRoot;
            favCleanOrphans.Visible = isFavRoot;
            favCleanOrphans.Enabled = isFavRoot && _favoriteManager.DetectOrphanFavorites(_sources).Count > 0;

            // Item de Favorito
            favConnect.Visible = isFavItem;
            favConnect.Enabled = isFavItem && _favoriteManager.ResolveFavorite(fav!, _sources).IsValid;
            favGoToSource.Visible = isFavItem;
            favGoToSource.Enabled = isFavItem && _favoriteManager.ResolveFavorite(fav!, _sources).IsValid;
            favSep1.Visible = isFavItem;
            favRename.Visible = isFavItem;
            favMoveUp.Visible = isFavItem;
            favMoveUp.Enabled = isFavItem && fav!.OrderIndex > 0;
            favMoveDown.Visible = isFavItem;
            favMoveDown.Enabled = isFavItem && fav!.OrderIndex < _favoriteManager.Favorites.Count - 1;
            favSep2.Visible = isFavItem;
            favRemove.Visible = isFavItem;

            // Itens Regulares
            connect.Visible = isRegularNode;
            connect.Enabled = _connectMenuItem.Enabled;
            disconnect.Visible = isRegularNode;
            disconnect.Enabled = _disconnectMenuItem.Enabled;
            regSep1.Visible = isRegularNode;

            bool isServer = isRegularNode && SelectedNode?.Kind == RdgNodeKind.Server;
            toggleFavRegular.Visible = isServer;
            regSepFav.Visible = isServer;
            if (isServer)
            {
                bool isFav = IsServerFavorited(SelectedNode);
                toggleFavRegular.Text = isFav ? "Remover dos Favoritos (Ctrl+D)" : "Adicionar aos Favoritos (Ctrl+D)";
            }

            addGroup.Visible = isRegularNode;
            addGroup.Enabled = _addGroupMenuItem.Enabled;
            addServer.Visible = isRegularNode;
            addServer.Enabled = _addServerMenuItem.Enabled;
            regSep2.Visible = isRegularNode;
            edit.Visible = isRegularNode;
            edit.Enabled = _editMenuItem.Enabled;
            remove.Visible = isRegularNode;
            remove.Enabled = _removeMenuItem.Enabled;

            bool isFile = isRegularNode && SelectedNode?.Kind == RdgNodeKind.File;
            closeFile.Visible = isFile;
            closeFile.Enabled = isFile;
            fileSeparator.Visible = isFile;

            bool isGuacSource = isRegularNode && SelectedNode is not null && GetSourceForNode(SelectedNode) is GuacamoleInventorySource;
            refreshGuac.Visible = isGuacSource;
            refreshGuac.Enabled = isGuacSource;
            removeGuac.Visible = isGuacSource;
            removeGuac.Enabled = isGuacSource;
            guacSeparator.Visible = isGuacSource;
        };
        return menu;
    }

    internal void NewDocument()
    {
        _untitledCounter++;
        RdgFileSource newSource = RdgFileSource.CreateNew($"Sem título {_untitledCounter}");
        RegisterSource(newSource);
        _activeSource = newSource;
        PopulateTree();
        SelectSourceRootNode(newSource);
        UpdateTitle();
        UpdateCommands();
        SetStatus($"Novo arquivo RDG criado: {newSource.DisplayName}");
    }

    private void OpenDocument()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "RDCMan Groups (*.rdg)|*.rdg|Arquivos XML (*.xml)|*.xml|Todos os arquivos (*.*)|*.*",
            Title = "Abrir arquivo RDG"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        OpenDocument(dialog.FileName);
    }

    internal IConnectionSource? OpenDocument(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            string fullPath = Path.GetFullPath(filePath);

            IConnectionSource? existing = _sources.FirstOrDefault(s =>
                s is RdgFileSource rdg && string.Equals(rdg.Document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _activeSource = existing;
                SelectSourceRootNode(existing);
                SetStatus($"Arquivo já aberto: {existing.DisplayName}");
                return existing;
            }

            if (_sources.Count == 1
                && _sources[0] is RdgFileSource singleRdg
                && singleRdg.Document.FilePath is null
                && !singleRdg.IsDirty
                && singleRdg.Roots.All(r => r.Children.Count == 0)
                && _sessions.Count == 0)
            {
                ClearSources();
            }
            else
            {
                RdgFileSource? emptyUntitled = _sources.OfType<RdgFileSource>().FirstOrDefault(s =>
                    s.Document.FilePath is null && !s.IsDirty && s.Roots.All(r => r.Children.Count == 0));
                if (emptyUntitled is not null
                    && _sources.Count(s => s is RdgFileSource) == 1
                    && _sessions.Values.All(sess => sess.Context?.SourceId != emptyUntitled.Id))
                {
                    UnregisterSource(emptyUntitled);
                    emptyUntitled.Dispose();
                }
            }

            RdgFileSource newSource = RdgFileSource.Load(fullPath);
            RegisterSource(newSource);
            _activeSource = newSource;

            PopulateTree();
            SelectSourceRootNode(newSource);
            UpdateTitle();
            UpdateCommands();
            SetStatus($"Aberto: {fullPath}");
            return newSource;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao abrir RDG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        AutoResizeConnectionPane();
        await LoadCachedGuacamoleInventoryAsync();
    }

    private async Task LoadCachedGuacamoleInventoryAsync()
    {
        if (_startupInventoryLoadAttempted || _workspaceLoadedFromDisk)
        {
            return;
        }

        _startupInventoryLoadAttempted = true;

        await GuacamolePortalManager.MigrateLegacyCacheIfNecessaryAsync();

        IReadOnlyList<GuacamolePortalMetadata> portals = GuacamolePortalManager.ListPortals();
        if (portals.Count == 0 && !GuacamoleInventoryCache.Exists())
        {
            SetStatus("Pronto.");
            return;
        }

        int loadedPortals = 0;
        int totalServers = 0;
        int totalGroups = 0;

        if (portals.Count > 0)
        {
            foreach (GuacamolePortalMetadata portal in portals)
            {
                try
                {
                    string inventoryPath = GuacamolePortalManager.GetInventoryFilePath(portal.Id);
                    GuacamoleCacheLoadResult loadResult = await GuacamoleInventoryCache.LoadAsync(inventoryPath);
                    if (loadResult.IsSuccess && loadResult.CacheRoot is not null)
                    {
                        GuacamoleImportResult materialized = GuacamoleInventoryCache.Materialize(loadResult.CacheRoot);
                        GuacamoleInventorySource guacSource = GuacamoleInventorySource.FromImportResult(
                            materialized,
                            portal.Id,
                            portal.DisplayName);
                        RegisterOrReplaceGuacamoleSource(guacSource);

                        loadedPortals++;
                        totalServers += materialized.ServerCount;
                        totalGroups += materialized.GroupCount;
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro ao carregar portal Guacamole '{portal.DisplayName}': {ex.Message}");
                }
            }

            if (loadedPortals > 0)
            {
                SetStatus($"Inventário Guacamole carregado: {loadedPortals} portal(is), {totalServers} servidor(es) em {totalGroups} grupo(s).");
                return;
            }
        }
        else
        {
            GuacamoleCacheLoadResult loadResult;
            try
            {
                loadResult = await GuacamoleInventoryCache.LoadAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Falha ao carregar o inventário do Guacamole: {ex.Message}");
                return;
            }

            if (loadResult.IsSuccess && loadResult.CacheRoot is not null)
            {
                try
                {
                    GuacamoleImportResult materialized = GuacamoleInventoryCache.Materialize(loadResult.CacheRoot);
                    GuacamoleInventorySource guacSource = GuacamoleInventorySource.FromImportResult(materialized);
                    RegisterOrReplaceGuacamoleSource(guacSource);

                    string lastUpdate = loadResult.CacheRoot.SavedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                    SetStatus($"Inventário Guacamole carregado: {materialized.ServerCount} servidor(es) em {materialized.GroupCount} grupo(s). Última atualização: {lastUpdate}.");
                }
                catch (Exception ex)
                {
                    SetStatus($"Erro ao materializar o inventário do Guacamole: {ex.Message}");
                }

                return;
            }
        }

        SetStatus("Pronto.");
    }

    private async void LoginGuacamoleAsync()
    {
        using GuacamoleLoginDialog dialog = new();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            SetStatus("Login no Guacamole cancelado.");
            return;
        }

        await ExecuteGuacamoleImportFlowAsync(
            dialog.ServerUrl,
            dialog.UserName,
            dialog.Password,
            isRefresh: false,
            portalDisplayName: dialog.PortalDisplayName);
    }

    private async void RefreshGuacamoleAsync()
    {
        GuacamoleInventorySource? targetSource = GetTargetGuacamoleSource();
        string? initialUrl = targetSource?.BaseUrl ?? GetActiveGuacamoleBaseUrl();
        string? initialName = targetSource?.DisplayName;

        if (initialUrl is null && GuacamoleInventoryCache.Exists())
        {
            try
            {
                var cached = await GuacamoleInventoryCache.LoadAsync();
                if (cached.IsSuccess && cached.CacheRoot is not null)
                {
                    initialUrl = cached.CacheRoot.BaseUrl;
                }
            }
            catch
            {
                // Fallback silencioso
            }
        }

        using GuacamoleLoginDialog dialog = new(initialUrl ?? string.Empty, initialName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            SetStatus("Atualização do Guacamole cancelada.");
            return;
        }

        await ExecuteGuacamoleImportFlowAsync(
            dialog.ServerUrl,
            dialog.UserName,
            dialog.Password,
            isRefresh: true,
            portalDisplayName: dialog.PortalDisplayName,
            targetPortalId: targetSource?.Id);
    }

    private async Task ExecuteGuacamoleImportFlowAsync(
        string serverUrl,
        string username,
        string password,
        bool isRefresh,
        string? portalDisplayName = null,
        Guid? targetPortalId = null)
    {
        string operationName = isRefresh ? "Atualização do Guacamole" : "Login no Guacamole";
        Guid portalId = targetPortalId ?? Guid.NewGuid();
        string displayName = string.IsNullOrWhiteSpace(portalDisplayName) ? "Guacamole" : portalDisplayName.Trim();

        GuacamoleImportResult result;
        UseWaitCursor = true;
        SetStatus(isRefresh
            ? $"Atualizando portal Guacamole '{displayName}'..."
            : $"Autenticando no portal '{displayName}' e carregando o inventário...");

        try
        {
            result = await GuacamoleInventoryProvider.ImportAsync(serverUrl, username, password);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show(
                this,
                "Tempo limite esgotado ao comunicar com o servidor Guacamole.",
                $"Erro na {operationName.ToLowerInvariant()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus($"Falha ao autenticar ou carregar o inventário do Guacamole.");
            return;
        }
        catch (HttpRequestException exception)
        {
            MessageBox.Show(
                this,
                $"Não foi possível conectar ao servidor Guacamole: {exception.Message}",
                $"Erro na {operationName.ToLowerInvariant()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus($"Falha ao autenticar ou carregar o inventário do Guacamole.");
            return;
        }
        catch (JsonException)
        {
            MessageBox.Show(
                this,
                "O servidor Guacamole retornou uma resposta em formato JSON inválido.",
                $"Erro na {operationName.ToLowerInvariant()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus($"Falha ao autenticar ou carregar o inventário do Guacamole.");
            return;
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                $"Erro na {operationName.ToLowerInvariant()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus($"Falha ao autenticar ou carregar o inventário do Guacamole.");
            return;
        }
        finally
        {
            UseWaitCursor = false;
        }

        GuacamoleCacheRoot cacheRoot;
        try
        {
            cacheRoot = GuacamoleInventoryCache.FromImportResult(result);
            GuacamoleInventoryCache.ValidatePayload(cacheRoot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Os dados recebidos do Guacamole não puderam ser validados: {ex.Message}",
                $"Erro na {operationName.ToLowerInvariant()}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("Falha na validação do inventário do Guacamole.");
            return;
        }

        GuacamoleInventorySource? existingGuac = _sources.OfType<GuacamoleInventorySource>().FirstOrDefault(s => s.Id == portalId);
        if (existingGuac is not null)
        {
            List<RdpSession> guacSessions = _sessions.Values
                .Where(s => s.Context?.SourceId == existingGuac.Id)
                .ToList();

            if (guacSessions.Any(IsSessionActive))
            {
                DialogResult sessionConfirm = MessageBox.Show(
                    this,
                    $"Existem sessões ativas do portal '{existingGuac.DisplayName}' que serão encerradas para {operationName.ToLowerInvariant()}. Deseja continuar?",
                    "Atualizar inventário do Guacamole",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (sessionConfirm != DialogResult.Yes)
                {
                    SetStatus($"{operationName} cancelado.");
                    return;
                }
            }

            if (existingGuac.IsDirty)
            {
                DialogResult discardConfirm = MessageBox.Show(
                    this,
                    $"O inventário do portal '{existingGuac.DisplayName}' possui modificações não salvas que serão descartadas. Deseja continuar?",
                    "Modificações não salvas no Guacamole",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (discardConfirm != DialogResult.Yes)
                {
                    SetStatus($"{operationName} cancelado.");
                    return;
                }
            }
        }

        GuacamolePortalMetadata metadata = new(portalId, displayName, serverUrl, DateTime.UtcNow);
        GuacamoleCacheSaveResult saveResult = await GuacamolePortalManager.SavePortalAsync(metadata, cacheRoot);
        if (!saveResult.IsSuccess)
        {
            string errorMessage = saveResult.FailureKind switch
            {
                GuacamoleCacheFailureKind.AccessDenied =>
                    "O PRDC não possui permissão para acessar o inventário local do Guacamole.",
                GuacamoleCacheFailureKind.ValidationFailed =>
                    "Os dados recebidos do Guacamole não puderam ser validados.",
                GuacamoleCacheFailureKind.IoFailure or _ =>
                    "Não foi possível salvar ou carregar o inventário local do Guacamole."
            };

            MessageBox.Show(
                this,
                errorMessage,
                "Erro ao salvar cache do Guacamole",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("Falha ao salvar o inventário local do Guacamole.");
            return;
        }

        GuacamoleInventorySource guacSource = GuacamoleInventorySource.FromImportResult(result, portalId, displayName);
        RegisterOrReplaceGuacamoleSource(guacSource);

        if (isRefresh)
        {
            SetStatus($"Portal '{displayName}' atualizado: {result.ServerCount} servidor(es) RDP em {result.GroupCount} grupo(s).");
        }
        else
        {
            SetStatus($"Login concluído no portal '{displayName}'. Importados {result.ServerCount} servidor(es) RDP em {result.GroupCount} grupo(s). Cache local salvo.");
        }
    }

    internal void RemoveGuacamoleInventory(bool promptUser = true)
    {
        GuacamoleInventorySource? guacSource = GetTargetGuacamoleSource();
        if (guacSource is null && GuacamolePortalManager.ListPortals().Count == 0 && !GuacamoleInventoryCache.Exists())
        {
            SetStatus("Nenhum inventário do Guacamole carregado.");
            return;
        }

        string portalName = guacSource?.DisplayName ?? "Guacamole";
        Guid? portalId = guacSource?.Id;

        if (promptUser)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                $"Deseja remover o portal Guacamole '{portalName}' deste computador? As conexões precisarão ser importadas novamente.",
                "Remover portal do Guacamole",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        if (guacSource is not null)
        {
            List<RdpSession> guacSessions = _sessions.Values
                .Where(s => s.Context?.SourceId == guacSource.Id)
                .ToList();

            if (promptUser && guacSessions.Any(IsSessionActive))
            {
                DialogResult sessionConfirm = MessageBox.Show(
                    this,
                    $"Existem sessões do portal '{portalName}' ativas que serão encerradas. Deseja continuar?",
                    "Fechar sessões do Guacamole",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (sessionConfirm != DialogResult.Yes)
                {
                    return;
                }
            }

            CloseSessionsForSource(guacSource.Id);
            UnregisterSource(guacSource);
            guacSource.Dispose();

            if (_sources.Count == 0)
            {
                _untitledCounter++;
                RdgFileSource initial = RdgFileSource.CreateNew($"Sem título {_untitledCounter}");
                RegisterSource(initial);
                _activeSource = initial;
            }
            else if (ReferenceEquals(_activeSource, guacSource))
            {
                _activeSource = _sources[0];
            }
        }

        try
        {
            if (portalId.HasValue)
            {
                GuacamolePortalManager.DeletePortal(portalId.Value);
            }
            else
            {
                GuacamoleInventoryCache.Delete();
            }
        }
        catch (Exception ex)
        {
            if (promptUser)
            {
                MessageBox.Show(
                    this,
                    $"Não foi possível excluir o cache do portal Guacamole: {ex.Message}",
                    "Remover portal do Guacamole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        PopulateTree();
        UpdateTitle();
        UpdateCommands();
        SetStatus($"Portal Guacamole '{portalName}' removido.");
    }

    private GuacamoleInventorySource? GetTargetGuacamoleSource()
    {
        return (_activeSource as GuacamoleInventorySource)
            ?? (GetSourceForNode(SelectedNode) as GuacamoleInventorySource)
            ?? _sources.OfType<GuacamoleInventorySource>().FirstOrDefault();
    }

    private string? GetActiveGuacamoleBaseUrl()
    {
        return GetTargetGuacamoleSource()?.BaseUrl;
    }

    internal bool SaveDocument()
    {
        IConnectionSource? targetSource = _activeSource ?? GetSourceForNode(SelectedNode) ?? _sources.FirstOrDefault();
        if (targetSource is null)
        {
            return false;
        }

        if (targetSource.IsReadOnly)
        {
            MessageBox.Show(this, "Esta origem é somente leitura e não pode ser salva.", "Salvar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return targetSource.Document.FilePath is null ? SaveSourceAs(targetSource) : SaveSourceTo(targetSource, targetSource.Document.FilePath);
    }

    internal bool SaveDocumentAs()
    {
        IConnectionSource? targetSource = _activeSource ?? GetSourceForNode(SelectedNode) ?? _sources.FirstOrDefault();
        if (targetSource is null)
        {
            return false;
        }

        if (targetSource.IsReadOnly)
        {
            MessageBox.Show(this, "Esta origem é somente leitura e não pode ser salva.", "Salvar como", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return SaveSourceAs(targetSource);
    }

    private bool SaveSourceAs(IConnectionSource targetSource)
    {
        string defaultName = targetSource.Document.FilePath is null
            ? (targetSource.Kind == ConnectionSourceKind.GuacamoleInventory ? "Guacamole.rdg" : $"{targetSource.DisplayName}.rdg")
            : Path.GetFileName(targetSource.Document.FilePath);

        using SaveFileDialog dialog = new()
        {
            Filter = "RDCMan Groups (*.rdg)|*.rdg|Arquivos XML (*.xml)|*.xml",
            DefaultExt = "rdg",
            AddExtension = true,
            Title = $"Salvar '{targetSource.DisplayName}' como...",
            FileName = defaultName
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        return SaveSourceTo(targetSource, dialog.FileName);
    }

    private bool SaveSourceTo(IConnectionSource targetSource, string filePath)
    {
        try
        {
            targetSource.SaveAs(filePath);
            UpdateSourceNodeText(targetSource);
            UpdateSessionTitles();
            UpdateTitle();
            UpdateCommands();
            SetStatus($"Salvo: {filePath}");
            RequestWorkspaceSave();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Erro ao salvar RDG", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    internal bool SaveAllDocuments()
    {
        List<string> errors = [];
        int savedCount = 0;

        foreach (IConnectionSource source in _sources.Where(s => s.IsDirty && !s.IsReadOnly).ToList())
        {
            try
            {
                if (source.Document.FilePath is null)
                {
                    if (SaveSourceAs(source))
                    {
                        savedCount++;
                    }
                }
                else
                {
                    source.Save();
                    UpdateSourceNodeText(source);
                    savedCount++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{source.DisplayName}: {ex.Message}");
            }
        }

        UpdateTitle();
        UpdateCommands();

        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Erros ao salvar alguns arquivos:\n" + string.Join("\n", errors), "Salvar tudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (savedCount > 0)
        {
            SetStatus($"Salvo(s) {savedCount} arquivo(s).");
        }

        return true;
    }

    internal bool CloseSelectedSource()
    {
        IConnectionSource? source = GetSourceForNode(SelectedNode) ?? _activeSource;
        if (source is null)
        {
            return false;
        }

        return CloseSource(source);
    }

    internal bool CloseSource(IConnectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_sources.Contains(source))
        {
            return false;
        }

        if (source.IsDirty)
        {
            DialogResult result = MessageBox.Show(
                this,
                $"Salvar as alterações em '{source.DisplayName}' antes de fechar?",
                "PRDC",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            if (result == DialogResult.Yes)
            {
                if (source.Document.FilePath is null)
                {
                    if (!SaveSourceAs(source))
                    {
                        return false;
                    }
                }
                else
                {
                    source.Save();
                }
            }
        }

        CloseSessionsForSource(source.Id);

        UnregisterSource(source);
        source.Dispose();

        if (_sources.Count == 0)
        {
            _untitledCounter++;
            RdgFileSource initial = RdgFileSource.CreateNew($"Sem título {_untitledCounter}");
            RegisterSource(initial);
            _activeSource = initial;
        }
        else if (ReferenceEquals(_activeSource, source))
        {
            _activeSource = _sources[0];
        }

        PopulateTree();
        UpdateTitle();
        UpdateCommands();
        SetStatus($"Arquivo fechado: {source.DisplayName}");
        return true;
    }

    internal IConnectionSource? GetSourceForNode(RdgNode? node)
    {
        if (node is null)
        {
            return null;
        }

        XDocument? doc = node.Element.Document;
        if (doc is not null)
        {
            foreach (IConnectionSource source in _sources)
            {
                if (source.Roots.Count > 0 && source.Roots[0].Element.Document == doc)
                {
                    return source;
                }
            }
        }

        XElement current = node.Element;
        while (current.Parent is not null && current.Parent.Name.LocalName != "RDCMan")
        {
            current = current.Parent;
        }

        return _sources.FirstOrDefault(s => s.Roots.Any(r => ReferenceEquals(r.Element, current)));
    }

    private void SelectSourceRootNode(IConnectionSource source)
    {
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Tag is RdgNode rdgNode && source.Roots.Any(r => ReferenceEquals(r.Element, rdgNode.Element)))
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                break;
            }
        }
    }

    private void UpdateSourceNodeText(IConnectionSource source)
    {
        foreach (TreeNode treeNode in _tree.Nodes)
        {
            if (treeNode.Tag is RdgNode node && node.Kind == RdgNodeKind.File && source.Roots.Any(r => ReferenceEquals(r.Element, node.Element)))
            {
                treeNode.Text = GetTreeNodeText(node);
                break;
            }
        }
    }

    private void AddGroup()
    {
        RdgNode? parent = GetContainerNode();
        if (parent is null)
        {
            return;
        }

        IConnectionSource? source = GetSourceForNode(parent);
        if (source is null)
        {
            return;
        }

        using NodeDialog dialog = new(RdgNodeKind.Group, "Novo grupo");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RdgNode created = source.Document.AddGroup(parent, dialog.NodeName);
        RefreshAndSelect(created.Element);
        MarkChanged(source, "Grupo adicionado.");
    }

    private void AddServer()
    {
        RdgNode? parent = GetContainerNode();
        if (parent is null)
        {
            return;
        }

        IConnectionSource? source = GetSourceForNode(parent);
        if (source is null)
        {
            return;
        }

        using NodeDialog dialog = new(RdgNodeKind.Server, "Novo servidor");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RdgNode created = source.Document.AddServer(parent, dialog.NodeName, dialog.HostName);
        RefreshAndSelect(created.Element);
        MarkChanged(source, "Servidor adicionado.");
    }

    private void EditSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
        {
            RenameFavoritePrompt(fav);
            return;
        }

        RdgNode? node = SelectedNode;
        if (node is null)
        {
            return;
        }

        IConnectionSource? source = GetSourceForNode(node);
        if (source is null)
        {
            return;
        }

        if (source is RdgFileSource { IsMissing: true } rdg)
        {
            MessageBox.Show(
                this,
                $"O arquivo '{rdg.FilePath}' não foi encontrado no disco.",
                "Arquivo não encontrado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (source is GuacamoleInventorySource { IsMissing: true } guac)
        {
            MessageBox.Show(
                this,
                $"O portal Guacamole '{guac.DisplayName}' não foi encontrado no disco.",
                "Portal não encontrado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using NodeDialog dialog = new(node.Kind, node.Name, node.HostName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (node.Kind == RdgNodeKind.Server)
        {
            source.Document.UpdateServer(node, dialog.NodeName, dialog.HostName);
            if (_sessions.TryGetValue(node.Element, out RdpSession? session))
            {
                session.DisplayName = dialog.NodeName;
                UpdateSessionTitle(session);
            }
        }
        else
        {
            source.Document.UpdateGroup(node, dialog.NodeName);
        }

        RefreshAndSelect(node.Element);
        MarkChanged(source, "Item atualizado.");
    }

    private void ActivateSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
        {
            _ = ConnectFavoriteAsync(fav);
            return;
        }

        if (SelectedNode?.Kind == RdgNodeKind.Server)
        {
            ConnectSelectedServer();
        }
        else
        {
            EditSelectedNode();
        }
    }

    private async void ConnectSelectedServer()
    {
        if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
        {
            await ConnectFavoriteAsync(fav);
            return;
        }

        await ConnectSelectedServerAsync();
    }

    internal async Task ConnectSelectedServerAsync()
    {
        RdgNode? server = SelectedNode;
        if (server?.Kind != RdgNodeKind.Server)
        {
            return;
        }

        IConnectionSource? source = GetSourceForNode(server);
        bool isGuacamole = source?.GuacamoleReferences.ContainsKey(server.Element) == true;
        if (!isGuacamole && string.IsNullOrWhiteSpace(server.HostName))
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

            if (session.Kind == SessionKind.GuacamoleWeb)
            {
                UpdateCommands();
                await session.GuacamoleHost!.ConnectAsync();
            }
            else
            {
                session.RdpHost!.ConnectTo(server.HostName);
            }

            UpdateCommands();
        }
        catch (ObjectDisposedException)
        {
            // Encerramento normal da aba/formulário durante inicialização assíncrona.
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            var (title, message, icon) = FormatConnectionErrorMessage(exception, isGuacamole, isReconnect: false);
            MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButtons.OK,
                icon);
            SetStatus(isGuacamole
                ? "Falha ao iniciar a sessão do Apache Guacamole."
                : "Falha ao iniciar a conexão RDP.");
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
            session.Disconnect();
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException
                or ObjectDisposedException
                or InvalidOperationException)
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
            UpdateConnectionViews();
            UpdateCommands();
        }
    }

    private async void ReconnectSession()
    {
        await ReconnectSessionAsync();
    }

    internal async Task ReconnectSessionAsync()
    {
        RdpSession? session = GetCommandSession();
        if (session is null || !CanReconnectSession(session))
        {
            return;
        }

        if (session.Kind == SessionKind.GuacamoleWeb)
        {
            try
            {
                UpdateCommands();
                await session.GuacamoleHost!.ReconnectAsync();
            }
            catch (ObjectDisposedException)
            {
                // Encerramento normal da aba/formulário durante reconexão assíncrona.
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or System.Runtime.InteropServices.COMException)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }

                MessageBox.Show(
                    this,
                    exception.Message,
                    "Erro ao reconectar",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                SetStatus("Falha ao reconectar a sessão do Apache Guacamole.");
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    UpdateSessionTitles();
                    UpdateConnectionViews();
                    UpdateCommands();
                }
            }

            return;
        }

        string server = session.RdpHost!.Server;
        session.DetachStateChanged(OnRdpConnectionStateChanged);

        try
        {
            session.ReplaceNativeHost();
            session.AttachStateChanged(OnRdpConnectionStateChanged);
            session.RdpHost.ConnectTo(server);
            SetStatus($"Reconectando a {session.DisplayName}...");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            session.DetachStateChanged(OnRdpConnectionStateChanged);
            var (title, message, icon) = FormatConnectionErrorMessage(exception, isGuacamole: false, isReconnect: true);
            MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButtons.OK,
                icon);
            SetStatus("Falha ao reconectar a sessão RDP.");
        }
        finally
        {
            UpdateSessionTitles();
            UpdateConnectionViews();
            UpdateCommands();
        }
    }

    private static (string Title, string Message, MessageBoxIcon Icon) FormatConnectionErrorMessage(
        Exception exception,
        bool isGuacamole,
        bool isReconnect)
    {
        Exception root = exception is TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException
            : exception;

        if (root is COMException comEx && ((uint)comEx.HResult is 0x80040111 or 0x80040154))
        {
            string detail = (uint)comEx.HResult switch
            {
                0x80040111 => "O controle ActiveX do Microsoft Remote Desktop (mstscax.dll) não pôde fornecer uma classe compatível neste sistema operacional.\n\n" +
                              "Verifique se o cliente de Área de Trabalho Remota do Windows está instalado e atualizado, ou utilize conexões via Apache Guacamole.",
                0x80040154 => "O controle ActiveX do Microsoft Remote Desktop (mstscax.dll) não está registrado no sistema operacional.\n\n" +
                              "Execute 'regsvr32 %windir%\\system32\\mstscax.dll' em um prompt de comando elevado para registrar o componente, ou utilize conexões via Apache Guacamole.",
                _ => root.Message
            };

            return ("Cliente RDP Indisponível", detail, MessageBoxIcon.Warning);
        }

        string defaultTitle = isReconnect ? "Erro ao reconectar" : "Erro ao conectar";
        return (defaultTitle, root.Message, MessageBoxIcon.Error);
    }

    private async void LogOffSession()
    {
        RdpSession? session = GetCommandSession();
        if (session is not { SupportsLogOff: true, ConnectionState: RdpConnectionState.LoggedIn, RdpHost: not null })
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
            Task operation = session.RdpHost.LogOffSessionAsync();
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

    private void DrawTreeNode(object? sender, DrawTreeNodeEventArgs eventArgs)
    {
        if (eventArgs.Node is null || eventArgs.Bounds.Width <= 0 || eventArgs.Bounds.Height <= 0)
        {
            return;
        }

        bool isFavRoot = ReferenceEquals(eventArgs.Node.Tag, FavoritesRootTag);
        FavoriteEntry? favEntry = eventArgs.Node.Tag as FavoriteEntry;
        RdgNode? node = eventArgs.Node.Tag as RdgNode;

        if (!isFavRoot && favEntry is null && node is null)
        {
            eventArgs.DrawDefault = true;
            return;
        }

        bool selected = (eventArgs.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
        Color backColor = selected ? SystemColors.Highlight : _tree.BackColor;
        Color foreColor = selected ? SystemColors.HighlightText : _tree.ForeColor;

        int textOffset;
        Color? customCircleColor = null;

        if (isFavRoot)
        {
            textOffset = 2;
        }
        else if (favEntry is not null)
        {
            textOffset = 3 + StatusCircleDiameter + StatusCircleSpacing;
            var res = _favoriteManager.ResolveFavorite(favEntry, _sources);
            if (res.IsValid && res.ServerElement is not null)
            {
                customCircleColor = GetServerStatusColor(res.ServerElement);
            }
            else
            {
                customCircleColor = Color.DarkGray;
                if (!selected)
                {
                    foreColor = ThemeManager.IsDark ? Color.DarkGray : SystemColors.GrayText;
                }
            }
        }
        else
        {
            textOffset = node!.Kind == RdgNodeKind.Server
                ? 3 + StatusCircleDiameter + StatusCircleSpacing
                : 2;
        }

        string displayText = isFavRoot
            ? eventArgs.Node.Text
            : favEntry is not null
                ? eventArgs.Node.Text
                : node!.Name;

        Font drawFont = isFavRoot
            ? new Font(_tree.Font, FontStyle.Bold)
            : _tree.Font;

        int measuredTextWidth = TextRenderer.MeasureText(
            eventArgs.Graphics,
            displayText,
            drawFont,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        int boundsWidth = Math.Max(eventArgs.Bounds.Width, textOffset + measuredTextWidth + 4);
        Rectangle nodeBounds = new(eventArgs.Bounds.Left, eventArgs.Bounds.Top, boundsWidth, eventArgs.Bounds.Height);

        using (SolidBrush backgroundBrush = new(backColor))
        {
            eventArgs.Graphics.FillRectangle(backgroundBrush, nodeBounds);
        }

        if (favEntry is not null && customCircleColor.HasValue)
        {
            Rectangle circleBounds = new(
                nodeBounds.Left + 3,
                nodeBounds.Top + ((nodeBounds.Height - StatusCircleDiameter) / 2),
                StatusCircleDiameter,
                StatusCircleDiameter);
            DrawStatusCircle(eventArgs.Graphics, circleBounds, customCircleColor.Value);
        }
        else if (node?.Kind == RdgNodeKind.Server)
        {
            Rectangle circleBounds = new(
                nodeBounds.Left + 3,
                nodeBounds.Top + ((nodeBounds.Height - StatusCircleDiameter) / 2),
                StatusCircleDiameter,
                StatusCircleDiameter);
            DrawStatusCircle(eventArgs.Graphics, circleBounds, GetServerStatusColor(node.Element));
        }

        Rectangle textBounds = Rectangle.FromLTRB(
            nodeBounds.Left + textOffset,
            nodeBounds.Top,
            nodeBounds.Right,
            nodeBounds.Bottom);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            displayText,
            drawFont,
            textBounds,
            foreColor,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix);

        if (isFavRoot)
        {
            drawFont.Dispose();
        }

        if ((eventArgs.State & TreeNodeStates.Focused) == TreeNodeStates.Focused)
        {
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, nodeBounds, foreColor, backColor);
        }
    }

    private void DrawSessionTab(object? sender, DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0 || eventArgs.Index >= _sessionTabs.TabPages.Count)
        {
            return;
        }

        TabPage page = _sessionTabs.TabPages[eventArgs.Index];
        bool isDark = ThemeManager.IsDark;
        bool selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color background = isDark
            ? (selected ? ThemeManager.ControlBackground : ThemeManager.SurfaceBackground)
            : (selected ? SystemColors.Window : SystemColors.Control);
        Color textColor = isDark ? ThemeManager.TextPrimary : SystemColors.ControlText;

        using (SolidBrush backgroundBrush = new(background))
        {
            eventArgs.Graphics.FillRectangle(backgroundBrush, eventArgs.Bounds);
        }

        Rectangle circleBounds = new(
            eventArgs.Bounds.Left + 7,
            eventArgs.Bounds.Top + ((eventArgs.Bounds.Height - StatusCircleDiameter) / 2),
            StatusCircleDiameter,
            StatusCircleDiameter);
        Color statusColor = page.Tag is RdpSession session
            ? GetServerStatusColor(session.ServerElement)
            : DisconnectedStatusColor;
        DrawStatusCircle(eventArgs.Graphics, circleBounds, statusColor);

        Rectangle closeButton = GetSessionTabCloseButtonBounds(eventArgs.Bounds);
        Rectangle textBounds = Rectangle.FromLTRB(
            circleBounds.Right + StatusCircleSpacing,
            eventArgs.Bounds.Top,
            closeButton.Left - 4,
            eventArgs.Bounds.Bottom);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            page.Text,
            Font,
            textBounds,
            textColor,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);

        const int glyphInset = 4;
        using Pen closePen = new(textColor, 1.5F);
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

        if (selected && !isDark)
        {
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, eventArgs.Bounds);
        }
    }

    private void DrawWorkspaceTab(object? sender, DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0 || eventArgs.Index >= _workspaceTabs.TabPages.Count)
        {
            return;
        }

        TabPage page = _workspaceTabs.TabPages[eventArgs.Index];
        bool isDark = ThemeManager.IsDark;
        bool selected = (eventArgs.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color background = isDark
            ? (selected ? ThemeManager.ControlBackground : ThemeManager.SurfaceBackground)
            : (selected ? SystemColors.Window : SystemColors.Control);
        Color textColor = isDark ? ThemeManager.TextPrimary : SystemColors.ControlText;

        using (SolidBrush backgroundBrush = new(background))
        {
            eventArgs.Graphics.FillRectangle(backgroundBrush, eventArgs.Bounds);
        }

        TextRenderer.DrawText(
            eventArgs.Graphics,
            page.Text,
            Font,
            eventArgs.Bounds,
            textColor,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix);

        if (selected && !isDark)
        {
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, eventArgs.Bounds);
        }
    }

    private static void DrawStatusCircle(Graphics graphics, Rectangle bounds, Color color)
    {
        SmoothingMode previousMode = graphics.SmoothingMode;
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush brush = new(color);
            graphics.FillEllipse(brush, bounds);
        }
        finally
        {
            graphics.SmoothingMode = previousMode;
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
            candidate => candidate.OwnsHost(sender));
        if (session is null)
        {
            return;
        }

        SetStatus(eventArgs.Message);
        if (eventArgs.State == RdpConnectionState.Disconnected)
        {
            session.ClearThumbnail();
        }
        else if (session.SupportsThumbnail && eventArgs.State is RdpConnectionState.Connected or RdpConnectionState.LoggedIn)
        {
            UpdateThumbnail(session);
            _thumbnailTimer.Start();
        }

        if (!_sessions.Values.Any(candidate => candidate.SupportsThumbnail && IsSessionConnected(candidate)))
        {
            _thumbnailTimer.Stop();
        }

        UpdateSessionTitles();
        UpdateConnectionViews();
        UpdateCommands();
    }

    private void RemoveSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
        {
            RemoveFavoriteNode(fav);
            return;
        }

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

        IConnectionSource? source = GetSourceForNode(node);
        if (source is null)
        {
            return;
        }

        CloseSessionsFor(node.Element);
        RemoveGuacamoleReferencesFor(node.Element);
        source.Document.Remove(node);
        source.MarkModified();
        PopulateTree();
        MarkChanged(source, "Item removido.");
    }

    private void PopulateTree()
    {
        _populatingTree = true;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();

            // 1. Nó raiz fixo ★ Favoritos
            _tree.Nodes.Add(CreateFavoritesRootNode());

            // 2. Fontes de Conexão
            foreach (IConnectionSource source in _sources)
            {
                foreach (RdgNode root in source.Roots)
                {
                    _tree.Nodes.Add(CreateTreeNode(root));
                }
            }
        }
        finally
        {
            _tree.EndUpdate();
            _populatingTree = false;
        }

        if (_tree.Nodes.Count > 0)
        {
            TreeNode? target = null;
            if (_activeSource is not null)
            {
                foreach (TreeNode node in _tree.Nodes)
                {
                    if (node.Tag is RdgNode rdgNode && _activeSource.Roots.Any(r => ReferenceEquals(r.Element, rdgNode.Element)))
                    {
                        target = node;
                        break;
                    }
                }
            }

            _tree.SelectedNode = target ?? (_tree.Nodes.Count > 1 ? _tree.Nodes[1] : _tree.Nodes[0]);
        }
        else
        {
            UpdateSelection();
        }

        AutoResizeConnectionPane();
    }

    private TreeNode CreateFavoritesRootNode()
    {
        TreeNode root = new($"★ Favoritos ({_favoriteManager.Favorites.Count})")
        {
            Tag = FavoritesRootTag
        };

        foreach (FavoriteEntry fav in _favoriteManager.Favorites.OrderBy(f => f.OrderIndex))
        {
            root.Nodes.Add(CreateFavoriteTreeNode(fav));
        }

        if (_favoriteManager.Favorites.Count > 0)
        {
            root.Expand();
        }

        return root;
    }

    private TreeNode CreateFavoriteTreeNode(FavoriteEntry fav)
    {
        var res = _favoriteManager.ResolveFavorite(fav, _sources);
        string label;
        if (res.Status == FavoriteOrphanStatus.MissingSource)
        {
            label = $"{fav.EffectiveDisplayName} [origem ausente]";
        }
        else if (res.Status == FavoriteOrphanStatus.MissingNode)
        {
            label = $"{fav.EffectiveDisplayName} [servidor ausente]";
        }
        else
        {
            string sourceLabel = res.Source?.DisplayName ?? fav.SourceType;
            label = $"{fav.EffectiveDisplayName} [{sourceLabel}]";
        }

        return new TreeNode(label)
        {
            Tag = fav
        };
    }

    internal void PopulateFavoritesRootNode()
    {
        if (_populatingTree) return;

        TreeNode? favRoot = FindFavoritesRootNode();
        if (favRoot is null)
        {
            PopulateTree();
            return;
        }

        _tree.BeginUpdate();
        try
        {
            favRoot.Text = $"★ Favoritos ({_favoriteManager.Favorites.Count})";
            favRoot.Nodes.Clear();
            foreach (FavoriteEntry fav in _favoriteManager.Favorites.OrderBy(f => f.OrderIndex))
            {
                favRoot.Nodes.Add(CreateFavoriteTreeNode(fav));
            }

            if (_favoriteManager.Favorites.Count > 0)
            {
                favRoot.Expand();
            }
        }
        finally
        {
            _tree.EndUpdate();
        }

        AutoResizeConnectionPane();
        _tree.Invalidate();
    }

    private TreeNode? FindFavoritesRootNode()
    {
        return _tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => ReferenceEquals(n.Tag, FavoritesRootTag));
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
            if (ReferenceEquals(treeNode.Tag, FavoritesRootTag))
            {
                continue;
            }

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
        bool isFavRoot = ReferenceEquals(_tree.SelectedNode?.Tag, FavoritesRootTag);
        FavoriteEntry? fav = _tree.SelectedNode?.Tag as FavoriteEntry;

        if (isFavRoot)
        {
            _nodeTitle.Text = "★ Favoritos";
            _nodeType.Text = $"{_favoriteManager.Favorites.Count} favoritos cadastrados";
            _nodeHost.Text = "Acesso rápido e atalhos de conexão direta";
            _connectionState.Visible = false;
            _thumbnailTitle.Visible = false;
            _thumbnail.Visible = false;
            _thumbnailStatus.Visible = false;
            UpdateCommands();
            return;
        }

        if (fav is not null)
        {
            var res = _favoriteManager.ResolveFavorite(fav, _sources);
            _nodeTitle.Text = fav.EffectiveDisplayName;

            if (res.IsValid && res.ServerElement is not null && res.Source is not null)
            {
                _activeSource = res.Source;
                string sourceLabel = res.Source.DisplayName;
                _nodeType.Text = $"Favorito ({fav.SourceType}) | Origem: {sourceLabel}";
                _nodeHost.Text = $"Host: {fav.Locator.HostName ?? fav.Locator.OriginalDisplayName}";

                RdgNode fakeNode = RdgNode.Create(res.ServerElement) ?? new RdgNode(res.ServerElement, RdgNodeKind.Server);
                RdpConnectionState state = GetConnectionState(fakeNode);
                RdpSession? session = GetSession(fakeNode);
                bool isServer = true;
                _connectionState.Visible = isServer;
                _connectionState.Text = $"Estado: {GetConnectionStateText(state)}";
                _thumbnailTitle.Visible = isServer;
                _thumbnail.Visible = isServer;
                _thumbnailStatus.Visible = isServer;

                bool isGuac = res.Source.GuacamoleReferences.ContainsKey(res.ServerElement);
                if (isGuac)
                {
                    _thumbnailTitle.Text = $"Miniatura da sessão - {fav.EffectiveDisplayName}";
                    _thumbnail.Image = null;
                    _thumbnailStatus.Text = "Sessão web via Apache Guacamole.";
                }
                else
                {
                    _thumbnailTitle.Text = session?.RdpHost is not null
                        ? $"Miniatura da sessão - {session.RdpHost.Server}"
                        : "Miniatura da sessão";
                    _thumbnail.Image = session?.Thumbnail;
                    _thumbnailStatus.Text = state switch
                    {
                        RdpConnectionState.Connecting => "Aguardando a conexão para gerar a miniatura.",
                        RdpConnectionState.Connected or RdpConnectionState.LoggedIn when session?.Thumbnail is null => "Capturando a sessão remota...",
                        RdpConnectionState.Connected or RdpConnectionState.LoggedIn => "Atualizada automaticamente a cada segundo.",
                        _ => "Sem sessão ativa."
                    };
                }
            }
            else
            {
                string reason = res.Status switch
                {
                    FavoriteOrphanStatus.MissingSource => "Origem não carregada no workspace",
                    FavoriteOrphanStatus.MissingNode => "Servidor não encontrado na origem",
                    _ => res.FailureReason ?? "Indisponível"
                };
                _nodeType.Text = $"Favorito Órfão ({res.Status})";
                _nodeHost.Text = reason;
                _connectionState.Visible = false;
                _thumbnailTitle.Visible = false;
                _thumbnail.Visible = false;
                _thumbnailStatus.Visible = false;
            }

            UpdateCommands();
            return;
        }

        RdgNode? node = SelectedNode;
        _activeSource = GetSourceForNode(node) ?? _activeSource ?? _sources.FirstOrDefault();
        _nodeTitle.Text = node?.Name ?? "Nenhum item selecionado";
        bool isServerNode = node?.Kind == RdgNodeKind.Server;
        GuacamoleConnectionReference? guacamoleReference = null;
        if (isServerNode && _activeSource is not null && _activeSource.GuacamoleReferences.TryGetValue(node!.Element, out GuacamoleConnectionReference? foundRef))
        {
            guacamoleReference = foundRef;
        }

        if (guacamoleReference is not null)
        {
            _nodeTitle.Text = guacamoleReference.DisplayName;
            _nodeType.Text = $"Origem: Apache Guacamole | Protocolo: {guacamoleReference.Protocol} | DataSource: {guacamoleReference.DataSource}";
            _nodeHost.Text = $"Endereço: {guacamoleReference.BaseUri}";
        }
        else
        {
            _nodeType.Text = node?.Kind switch
            {
                RdgNodeKind.File => "Arquivo RDG",
                RdgNodeKind.Group => "Grupo",
                RdgNodeKind.Server => "Servidor",
                _ => string.Empty
            };
            _nodeHost.Text = isServerNode ? $"Host: {node!.HostName}" : string.Empty;
        }

        RdpConnectionState nodeState = isServerNode ? GetConnectionState(node!) : RdpConnectionState.Disconnected;
        RdpSession? nodeSession = isServerNode ? GetSession(node!) : null;
        _connectionState.Visible = isServerNode;
        _connectionState.Text = isServerNode ? $"Estado: {GetConnectionStateText(nodeState)}" : string.Empty;
        _thumbnailTitle.Visible = isServerNode;
        _thumbnail.Visible = isServerNode;
        _thumbnailStatus.Visible = isServerNode;

        if (guacamoleReference is not null)
        {
            _thumbnailTitle.Text = $"Miniatura da sessão - {guacamoleReference.DisplayName}";
            _thumbnail.Image = null;
            _thumbnailStatus.Text = "Sessão web via Apache Guacamole.";
        }
        else
        {
            _thumbnailTitle.Text = nodeSession?.RdpHost is not null
                ? $"Miniatura da sessão - {nodeSession.RdpHost.Server}"
                : "Miniatura da sessão";
            _thumbnail.Image = nodeSession?.Thumbnail;
            _thumbnailStatus.Text = isServerNode
                ? nodeState switch
                {
                    RdpConnectionState.Connecting => "Aguardando a conexão para gerar a miniatura.",
                    RdpConnectionState.Connected or RdpConnectionState.LoggedIn when nodeSession?.Thumbnail is null =>
                        "Capturando a sessão remota...",
                    RdpConnectionState.Connected or RdpConnectionState.LoggedIn => "Atualizada automaticamente a cada segundo.",
                    _ => "Sem sessão ativa."
                }
                : string.Empty;
        }

        UpdateCommands();
    }

    private void UpdateCommands()
    {
        bool isFavRoot = ReferenceEquals(_tree.SelectedNode?.Tag, FavoritesRootTag);
        FavoriteEntry? fav = _tree.SelectedNode?.Tag as FavoriteEntry;
        RdgNode? selected = SelectedNode;

        if (isFavRoot)
        {
            _addGroupMenuItem.Enabled = false;
            _addServerMenuItem.Enabled = false;
            _editMenuItem.Enabled = false;
            _removeMenuItem.Enabled = false;
            _connectMenuItem.Enabled = false;
            _toggleFavoriteMenuItem.Enabled = false;
            _goToSourceMenuItem.Enabled = false;
        }
        else if (fav is not null)
        {
            var res = _favoriteManager.ResolveFavorite(fav, _sources);
            _addGroupMenuItem.Enabled = false;
            _addServerMenuItem.Enabled = false;
            _editMenuItem.Enabled = true;
            _removeMenuItem.Enabled = true;
            _toggleFavoriteMenuItem.Text = "&Remover dos Favoritos";
            _toggleFavoriteMenuItem.Enabled = true;
            _goToSourceMenuItem.Enabled = res.IsValid;
            _goToSourceMenuItem.Visible = true;

            bool canConnect = res.IsValid && res.ServerElement is not null;
            if (canConnect)
            {
                RdgNode fakeNode = RdgNode.Create(res.ServerElement!) ?? new RdgNode(res.ServerElement!, RdgNodeKind.Server);
                RdpSession? s = GetSession(fakeNode);
                _connectMenuItem.Enabled = s is null || !IsSessionActive(s);
            }
            else
            {
                _connectMenuItem.Enabled = false;
            }
        }
        else
        {
            bool container = GetContainerNode() is not null;
            _addGroupMenuItem.Enabled = container;
            _addServerMenuItem.Enabled = container;
            _editMenuItem.Enabled = selected is not null;
            _removeMenuItem.Enabled = selected is { Kind: not RdgNodeKind.File };

            RdpSession? selectedSession = selected?.Kind == RdgNodeKind.Server ? GetSession(selected) : null;
            _connectMenuItem.Enabled = selected?.Kind == RdgNodeKind.Server
                && (selectedSession is null || !IsSessionActive(selectedSession));

            bool isServer = selected?.Kind == RdgNodeKind.Server;
            _toggleFavoriteMenuItem.Enabled = isServer;
            _toggleFavoriteMenuItem.Text = isServer && IsServerFavorited(selected)
                ? "&Remover dos Favoritos"
                : "&Adicionar aos Favoritos";
            _goToSourceMenuItem.Enabled = false;
            _goToSourceMenuItem.Visible = false;
        }

        RdpSession? commandSession = GetCommandSession();
        _reconnectMenuItem.Enabled = commandSession is not null && CanReconnectSession(commandSession);
        _disconnectMenuItem.Enabled = commandSession is not null && IsSessionActive(commandSession);
        _logOffMenuItem.Enabled = commandSession is
        {
            SupportsLogOff: true,
            ConnectionState: RdpConnectionState.LoggedIn,
            IsLogOffPending: false
        };
        _closeSessionTabMenuItem.Enabled = _sessionTabs.SelectedTab?.Tag is RdpSession;
        _saveMenuItem.Enabled = _activeSource is not null && !_activeSource.IsReadOnly;
        _saveAsMenuItem.Enabled = _activeSource is not null && !_activeSource.IsReadOnly;
        _saveAllMenuItem.Enabled = _sources.Any(s => s.IsDirty && !s.IsReadOnly);
        _closeFileMenuItem.Enabled = _activeSource is not null;
        bool hasGuacamole = _sources.Any(s => s.Kind == ConnectionSourceKind.GuacamoleInventory)
            || GuacamolePortalManager.ListPortals().Count > 0
            || GuacamoleInventoryCache.Exists();
        _refreshGuacamoleMenuItem.Enabled = hasGuacamole;
        _removeGuacamoleMenuItem.Enabled = hasGuacamole;
    }

    private void UpdateExpanded(TreeNode? treeNode, bool expanded)
    {
        if (!_populatingTree && treeNode?.Tag is RdgNode node)
        {
            IConnectionSource? source = GetSourceForNode(node);
            source?.Document.SetExpanded(node, expanded);
            if (source is not null)
            {
                UpdateSourceNodeText(source);
            }
            UpdateTitle();
            AutoResizeConnectionPane();
        }
    }

    private bool HasUnsavedChanges => _sources.Any(s => s.IsDirty);

    private bool ConfirmDiscardChanges()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        int dirtyCount = _sources.Count(s => s.IsDirty);
        string message = dirtyCount == 1
            ? $"O arquivo '{_sources.First(s => s.IsDirty).DisplayName}' possui alterações não salvas.\nDeseja salvar antes de continuar?"
            : $"Existem {dirtyCount} arquivos com alterações não salvas.\nDeseja salvar todas as alterações antes de continuar?";

        DialogResult result = MessageBox.Show(
            this,
            message,
            "PRDC",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => SaveAllDocuments(),
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
        _workspaceSaveDebounceTimer.Stop();
        SaveCurrentWorkspace();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        ThemeManager.ThemeChanged -= ApplyCurrentTheme;
        _thumbnailTimer.Dispose();
        _autoHideTimer.Dispose();
        _workspaceSaveDebounceTimer.Dispose();
        CloseAllSessions();
        ClearSources();
    }

    public bool IsFullScreen => _isFullScreen;

    public void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    public void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        _previousWindowState = WindowState;
        _previousBorderStyle = FormBorderStyle;
        _previousBounds = Bounds;
        _isFullScreen = true;

        if (_fullScreenMenuItem is not null)
        {
            _fullScreenMenuItem.Checked = true;
        }

        if (MainMenuStrip is not null)
        {
            MainMenuStrip.Visible = false;
        }

        _statusStrip.Visible = false;

        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = Screen.FromControl(this).Bounds;
    }

    public void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        _isFullScreen = false;

        if (_fullScreenMenuItem is not null)
        {
            _fullScreenMenuItem.Checked = false;
        }

        FormBorderStyle = _previousBorderStyle;
        if (_previousWindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            WindowState = FormWindowState.Normal;
            if (!_previousBounds.IsEmpty)
            {
                Bounds = _previousBounds;
            }
        }

        if (MainMenuStrip is not null)
        {
            MainMenuStrip.Visible = true;
        }

        _statusStrip.Visible = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.P))
        {
            OpenQuickOpen();
            return true;
        }

        if (keyData == Keys.F11)
        {
            ToggleFullScreen();
            return true;
        }

        if (keyData == Keys.Escape && _isFullScreen)
        {
            ExitFullScreen();
            return true;
        }

        if (keyData == (Keys.Control | Keys.D))
        {
            ToggleFavoriteForSelectedNode();
            return true;
        }

        if (keyData == (Keys.Control | Keys.G))
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
            {
                GoToFavoriteSource(fav);
                return true;
            }
        }

        if (keyData == Keys.F2)
        {
            if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
            {
                RenameFavoritePrompt(fav);
                return true;
            }
            else if (SelectedNode is not null)
            {
                EditSelectedNode();
                return true;
            }
        }

        if (keyData == Keys.Delete && _tree.SelectedNode?.Tag is FavoriteEntry favDelete)
        {
            RemoveFavoriteNode(favDelete);
            return true;
        }

        if (keyData == Keys.Enter && _tree.Focused)
        {
            ActivateSelectedNode();
            return true;
        }

        if (keyData == (Keys.Control | Keys.P))
        {
            OpenQuickOpen();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsServerFavorited(RdgNode? server)
    {
        if (server?.Kind != RdgNodeKind.Server) return false;
        IConnectionSource? source = GetSourceForNode(server);
        if (source is null) return false;

        string nodePath = ServerLocator.BuildNodePath(server.Element);
        return _favoriteManager.Favorites.Any(f =>
            f.SourceId == source.Id && (f.Locator.NodePath == nodePath || f.OriginalDisplayName == server.Name));
    }

    internal void ToggleFavoriteForSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is FavoriteEntry fav)
        {
            RemoveFavoriteNode(fav);
            return;
        }

        RdgNode? server = SelectedNode;
        if (server?.Kind != RdgNodeKind.Server)
        {
            SetStatus("Selecione um servidor para adicionar aos favoritos.");
            return;
        }

        IConnectionSource? source = GetSourceForNode(server);
        if (source is null)
        {
            return;
        }

        string nodePath = ServerLocator.BuildNodePath(server.Element);
        FavoriteEntry? existing = _favoriteManager.Favorites
            .FirstOrDefault(f => f.SourceId == source.Id && (f.Locator.NodePath == nodePath || f.OriginalDisplayName == server.Name));

        if (existing is not null)
        {
            RemoveFavoriteNode(existing);
        }
        else
        {
            FavoriteEntry added = _favoriteManager.AddFavorite(source, server.Element);
            _searchCatalog.UpdateFavorite(added, isRemoved: false);
            PopulateFavoritesRootNode();
            RequestWorkspaceSave();
            SetStatus($"'{added.EffectiveDisplayName}' adicionado aos favoritos.");
        }
    }

    internal void RemoveFavoriteNode(FavoriteEntry fav)
    {
        if (_favoriteManager.RemoveFavorite(fav.FavoriteId))
        {
            _searchCatalog.UpdateFavorite(fav, isRemoved: true);
            PopulateFavoritesRootNode();
            RequestWorkspaceSave();
            SetStatus($"Favorito '{fav.EffectiveDisplayName}' removido.");
        }
    }

    internal bool GoToFavoriteSource(FavoriteEntry fav)
    {
        var res = _favoriteManager.ResolveFavorite(fav, _sources);
        if (!res.IsValid || res.ServerElement is null)
        {
            string reason = res.Status switch
            {
                FavoriteOrphanStatus.MissingSource => "origem não carregada",
                FavoriteOrphanStatus.MissingNode => "servidor não encontrado na origem",
                _ => res.FailureReason ?? "servidor indisponível"
            };
            SetStatus($"Não foi possível ir para a origem: {reason}.");
            return false;
        }

        TreeNode? target = FindTreeNode(_tree.Nodes, res.ServerElement);
        if (target is not null)
        {
            _tree.SelectedNode = target;
            target.EnsureVisible();
            SetStatus($"Localizado na origem: {res.Source?.DisplayName}");
            return true;
        }

        SetStatus("Nó de origem não encontrado na árvore.");
        return false;
    }

    internal void RenameFavoritePrompt(FavoriteEntry fav)
    {
        using FavoriteAliasDialog dialog = new(fav.OriginalDisplayName, fav.CustomAlias);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _favoriteManager.RenameFavorite(fav.FavoriteId, dialog.AliasText);
            _searchCatalog.UpdateFavorite(fav, isRemoved: false);
            PopulateFavoritesRootNode();
            UpdateSelection();
            RequestWorkspaceSave();
            SetStatus($"Favorito atualizado para '{fav.EffectiveDisplayName}'.");
        }
    }

    internal void MoveFavoriteUp(FavoriteEntry fav)
    {
        if (fav.OrderIndex > 0)
        {
            _favoriteManager.ReorderFavorite(fav.FavoriteId, fav.OrderIndex - 1);
            PopulateFavoritesRootNode();
            RequestWorkspaceSave();
            SelectFavoriteNode(fav.FavoriteId);
        }
    }

    internal void MoveFavoriteDown(FavoriteEntry fav)
    {
        if (fav.OrderIndex < _favoriteManager.Favorites.Count - 1)
        {
            _favoriteManager.ReorderFavorite(fav.FavoriteId, fav.OrderIndex + 1);
            PopulateFavoritesRootNode();
            RequestWorkspaceSave();
            SelectFavoriteNode(fav.FavoriteId);
        }
    }

    private void SelectFavoriteNode(Guid favoriteId)
    {
        TreeNode? favRoot = FindFavoritesRootNode();
        if (favRoot is not null)
        {
            foreach (TreeNode child in favRoot.Nodes)
            {
                if (child.Tag is FavoriteEntry f && f.FavoriteId == favoriteId)
                {
                    _tree.SelectedNode = child;
                    child.EnsureVisible();
                    break;
                }
            }
        }
    }

    internal void CleanOrphanFavorites()
    {
        var orphans = _favoriteManager.DetectOrphanFavorites(_sources);
        if (orphans.Count == 0)
        {
            SetStatus("Nenhum favorito órfão encontrado.");
            return;
        }

        DialogResult dr = MessageBox.Show(
            this,
            $"Foram encontrados {orphans.Count} favoritos com origem ou servidor ausente.\nDeseja remover esses favoritos da lista?",
            "Limpar Favoritos Órfãos",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (dr == DialogResult.Yes)
        {
            foreach (var o in orphans)
            {
                _favoriteManager.RemoveFavorite(o.Favorite.FavoriteId);
                _searchCatalog.UpdateFavorite(o.Favorite, isRemoved: true);
            }
            PopulateFavoritesRootNode();
            RequestWorkspaceSave();
            SetStatus($"{orphans.Count} favoritos órfãos removidos.");
        }
    }

    internal async Task<RdpSession?> ConnectFavoriteAsync(FavoriteEntry favorite, bool startHostConnection = true, bool showPromptOnError = true)
    {
        var res = _favoriteManager.ResolveFavorite(favorite, _sources);
        if (!res.IsValid || res.Source is null || res.ServerElement is null)
        {
            string reason = res.Status switch
            {
                FavoriteOrphanStatus.MissingSource => "a origem correspondente não está carregada no workspace",
                FavoriteOrphanStatus.MissingNode => "o servidor não foi encontrado na origem",
                _ => res.FailureReason ?? "o servidor não pôde ser resolvido"
            };
            if (showPromptOnError)
            {
                MessageBox.Show(
                    this,
                    $"Não foi possível conectar ao favorito '{favorite.EffectiveDisplayName}': {reason}.",
                    "Favorito indisponível",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            SetStatus($"Favorito '{favorite.EffectiveDisplayName}' indisponível: {reason}.");
            return null;
        }

        RdgNode serverNode = RdgNode.Create(res.ServerElement)
            ?? new RdgNode(res.ServerElement, RdgNodeKind.Server);

        _activeSource = res.Source;

        try
        {
            _workspaceTabs.SelectedTab = _sessionPage;
            RdpSession session = GetOrCreateSession(serverNode);
            _sessionTabs.SelectedTab = session.Page;
            UpdateConnectionViews();

            if (IsSessionActive(session))
            {
                SetStatus($"A sessão de {favorite.EffectiveDisplayName} já está ativa.");
                return session;
            }

            if (startHostConnection)
            {
                if (session.Kind == SessionKind.GuacamoleWeb)
                {
                    UpdateCommands();
                    await session.GuacamoleHost!.ConnectAsync();
                }
                else
                {
                    session.RdpHost!.ConnectTo(serverNode.HostName);
                }
            }

            UpdateCommands();
            return session;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            if (IsDisposed || Disposing) return null;
            bool isGuacamole = res.Source.GuacamoleReferences.ContainsKey(res.ServerElement);
            var (title, message, icon) = FormatConnectionErrorMessage(exception, isGuacamole, isReconnect: false);
            if (showPromptOnError)
            {
                MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
            }
            SetStatus(isGuacamole ? "Falha ao iniciar a sessão do Apache Guacamole." : "Falha ao iniciar a conexão RDP.");
            UpdateCommands();
            return null;
        }
    }

    internal QuickOpenOverlayForm? _activeQuickOpenOverlay;
    internal bool StartHostConnectionOnQuickOpen = true;

    internal QuickOpenOverlayForm OpenQuickOpen()
    {
        if (_activeQuickOpenOverlay is not null && !_activeQuickOpenOverlay.IsDisposed)
        {
            _activeQuickOpenOverlay.BringToFront();
            _activeQuickOpenOverlay.Focus();
            _activeQuickOpenOverlay.SearchBox.Focus();
            return _activeQuickOpenOverlay;
        }

        QuickOpenOverlayForm overlay = new(_searchCatalog, this);
        _activeQuickOpenOverlay = overlay;

        overlay.ConnectRequested += async entry =>
        {
            if (StartHostConnectionOnQuickOpen)
            {
                await ExecuteQuickOpenConnectAsync(entry);
            }
        };

        overlay.GoToSourceRequested += entry =>
        {
            ExecuteQuickOpenGoToSource(entry);
        };

        overlay.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_activeQuickOpenOverlay, overlay))
            {
                _activeQuickOpenOverlay = null;
            }
        };

        overlay.Show(this);
        return overlay;
    }

    internal async Task<RdpSession?> ExecuteQuickOpenConnectAsync(GlobalSearchEntry entry, bool startHostConnection = true, bool showPromptOnError = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IConnectionSource? source = _sources.FirstOrDefault(s => s.Id == entry.SourceId);
        if (source is null)
        {
            if (showPromptOnError)
            {
                MessageBox.Show(
                    this,
                    $"Não foi possível conectar ao servidor '{entry.DisplayName}': a origem correspondente não está carregada no workspace.",
                    "Servidor indisponível",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            SetStatus($"Servidor '{entry.DisplayName}' indisponível: origem não encontrada.");
            return null;
        }

        if (source is RdgFileSource { IsMissing: true } || source is GuacamoleInventorySource { IsMissing: true })
        {
            if (showPromptOnError)
            {
                MessageBox.Show(
                    this,
                    $"Não foi possível conectar ao servidor '{entry.DisplayName}': a origem está marcada como inacessível no disco.",
                    "Origem indisponível",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            SetStatus($"Servidor '{entry.DisplayName}' indisponível: origem inacessível.");
            return null;
        }

        _activeSource = source;
        RdgNode serverNode = RdgNode.Create(entry.ServerElement)
            ?? new RdgNode(entry.ServerElement, RdgNodeKind.Server);

        try
        {
            _workspaceTabs.SelectedTab = _sessionPage;
            RdpSession session = GetOrCreateSession(serverNode);
            _sessionTabs.SelectedTab = session.Page;
            UpdateConnectionViews();

            if (IsSessionActive(session))
            {
                SetStatus($"A sessão de {entry.DisplayName} já está ativa.");
                return session;
            }

            if (startHostConnection)
            {
                if (session.Kind == SessionKind.GuacamoleWeb)
                {
                    UpdateCommands();
                    await session.GuacamoleHost!.ConnectAsync();
                }
                else
                {
                    session.RdpHost!.ConnectTo(serverNode.HostName);
                }
            }

            UpdateCommands();
            return session;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException)
        {
            if (IsDisposed || Disposing) return null;
            bool isGuacamole = entry.GuacamoleReference is not null || entry.SourceKind == ConnectionSourceKind.GuacamoleInventory;
            var (title, message, icon) = FormatConnectionErrorMessage(exception, isGuacamole, isReconnect: false);
            if (showPromptOnError)
            {
                MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
            }
            SetStatus(isGuacamole ? "Falha ao iniciar a sessão do Apache Guacamole." : "Falha ao iniciar a conexão RDP.");
            UpdateCommands();
            return null;
        }
    }

    internal bool ExecuteQuickOpenGoToSource(GlobalSearchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.ServerElement is null)
        {
            SetStatus("Servidor indisponível na origem.");
            return false;
        }

        TreeNode? target = FindTreeNode(_tree.Nodes, entry.ServerElement);
        if (target is not null)
        {
            _tree.SelectedNode = target;
            target.EnsureVisible();
            SetStatus($"Localizado na origem: {entry.SourceDisplayName} ({entry.NodePath})");
            return true;
        }

        SetStatus("Nó do servidor não encontrado na árvore.");
        return false;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_THEMECHANGED = 0x031A;

        if (m.Msg is WM_SETTINGCHANGE or WM_THEMECHANGED)
        {
            if (IsHandleCreated && !_applyingTheme && ThemeManager.CurrentTheme == AppTheme.System)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && ThemeManager.CurrentTheme == AppTheme.System)
                    {
                        ThemeManager.NotifySystemThemeChanged();
                    }
                }));
            }
        }

        base.WndProc(ref m);
    }

    private void ApplyCurrentTheme()
    {
        if (_applyingTheme)
        {
            return;
        }

        try
        {
            _applyingTheme = true;
            ThemeManager.ApplyTheme(this);
            UpdateThemeMenu();
            _workspaceTabs.Invalidate();
            _sessionTabs.Invalidate();
            _tree.Invalidate();
            _thumbnail.BackColor = Color.Black;
            _sessionPlaceholder.ForeColor = ThemeManager.TextMuted;
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    private void UpdateThemeMenu()
    {
        _lightThemeMenuItem.Checked = ThemeManager.CurrentTheme == AppTheme.Light;
        _darkThemeMenuItem.Checked = ThemeManager.CurrentTheme == AppTheme.Dark;
        _systemThemeMenuItem.Checked = ThemeManager.CurrentTheme == AppTheme.System;
    }

    private void UpdateConnectionViews()
    {
        _tree.Invalidate();
        InvalidateSessionTabHeaders();
        UpdateSelection();
    }

    private string GetTreeNodeText(RdgNode node)
    {
        if (node.Kind == RdgNodeKind.Server)
        {
            return $"    {node.Name}";
        }

        if (node.Kind == RdgNodeKind.File)
        {
            IConnectionSource? source = GetSourceForNode(node);
            if (source is not null)
            {
                string marker = source.IsDirty ? " *" : string.Empty;
                return $"{source.DisplayName}{marker}";
            }
        }

        return node.Name;
    }

    private Color GetServerStatusColor(XElement serverElement)
    {
        if (!_sessions.TryGetValue(serverElement, out RdpSession? session))
        {
            return DisconnectedStatusColor;
        }

        return session.ConnectionState switch
        {
            RdpConnectionState.Connecting or RdpConnectionState.Connected => AuthenticatingStatusColor,
            RdpConnectionState.LoggedIn => IsSessionWindowOpen(session)
                ? ConnectedStatusColor
                : InactiveConnectedStatusColor,
            _ => DisconnectedStatusColor
        };
    }

    private bool IsSessionWindowOpen(RdpSession session)
    {
        return ReferenceEquals(_workspaceTabs.SelectedTab, _sessionPage)
            && ReferenceEquals(_sessionTabs.SelectedTab, session.Page);
    }

    private RdpConnectionState GetConnectionState(RdgNode server)
    {
        return GetSession(server)?.ConnectionState ?? RdpConnectionState.Disconnected;
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

    internal RdpSession GetOrCreateSession(RdgNode server)
    {
        if (_sessions.TryGetValue(server.Element, out RdpSession? existing))
        {
            existing.DisplayName = server.Name;
            UpdateSessionTitle(existing);
            return existing;
        }

        IConnectionSource source = GetSourceForNode(server) ?? _activeSource ?? _sources.First();

        RdpSession session = source.GuacamoleReferences.TryGetValue(server.Element, out GuacamoleConnectionReference? reference)
            ? new RdpSession(server.Element, server.Name, reference, source.Id)
            : new RdpSession(server.Element, server.Name);

        SessionContext context = new(
            source.Id,
            source.DisplayName,
            server.Element,
            session);
        session.Context = context;

        session.AttachStateChanged(OnRdpConnectionStateChanged);
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

    private static bool CanReconnectSession(RdpSession session)
    {
        return session.Kind == SessionKind.GuacamoleWeb
            || (IsSessionActive(session) && !string.IsNullOrWhiteSpace(session.RdpHost?.Server));
    }

    private static bool IsSessionActive(RdpSession session)
    {
        return session.ConnectionState is
            RdpConnectionState.Connecting or RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
    }

    private static bool IsSessionConnected(RdpSession session)
    {
        return session.ConnectionState is RdpConnectionState.Connected or RdpConnectionState.LoggedIn;
    }

    private void UpdateThumbnails()
    {
        if (!ReferenceEquals(_workspaceTabs.SelectedTab, _detailsPage)
            || SelectedNode is not { Kind: RdgNodeKind.Server } selected
            || GetSession(selected) is not { SupportsThumbnail: true } session
            || !IsSessionConnected(session))
        {
            return;
        }

        UpdateThumbnail(session);
    }

    private void UpdateThumbnail(RdpSession session)
    {
        if (!session.SupportsThumbnail || session.RdpHost is null)
        {
            return;
        }

        Bitmap? thumbnail = session.RdpHost.CaptureThumbnail(_thumbnail.ClientSize);
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

    internal string GetQualifiedSessionTitle(RdpSession session)
    {
        string? sourceDisplayName = null;
        if (session.Context is not null)
        {
            IConnectionSource? source = _sources.FirstOrDefault(s => s.Id == session.Context.SourceId);
            sourceDisplayName = source?.DisplayName ?? session.Context.SourceDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(sourceDisplayName))
        {
            string sourceName = Path.GetFileNameWithoutExtension(sourceDisplayName);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = sourceDisplayName;
            }
            return $"[{sourceName}] {session.DisplayName}";
        }

        return session.DisplayName;
    }

    private void UpdateSessionTitles()
    {
        foreach (RdpSession session in _sessions.Values)
        {
            if (session.Context is not null)
            {
                IConnectionSource? source = _sources.FirstOrDefault(s => s.Id == session.Context.SourceId);
                if (source is not null && !string.Equals(session.Context.SourceDisplayName, source.DisplayName, StringComparison.Ordinal))
                {
                    session.Context.SourceDisplayName = source.DisplayName;
                }
            }

            string qualifiedTitle = GetQualifiedSessionTitle(session);
            if (!string.Equals(session.Page.Text, qualifiedTitle, StringComparison.Ordinal))
            {
                session.Page.Text = qualifiedTitle;
            }
        }

        int activeCount = _sessions.Values.Count(IsSessionActive);
        string sessionPageTitle = activeCount == 0 ? "Sessões" : $"Sessões ({activeCount})";
        if (!string.Equals(_sessionPage.Text, sessionPageTitle, StringComparison.Ordinal))
        {
            _sessionPage.Text = sessionPageTitle;
        }

        UpdateSessionTabSizes();
        InvalidateSessionTabHeaders();
    }

    private void UpdateSessionTitle(RdpSession session)
    {
        if (session.Context is not null)
        {
            IConnectionSource? source = _sources.FirstOrDefault(s => s.Id == session.Context.SourceId);
            if (source is not null && !string.Equals(session.Context.SourceDisplayName, source.DisplayName, StringComparison.Ordinal))
            {
                session.Context.SourceDisplayName = source.DisplayName;
            }
        }

        string qualifiedTitle = GetQualifiedSessionTitle(session);
        if (!string.Equals(session.Page.Text, qualifiedTitle, StringComparison.Ordinal))
        {
            session.Page.Text = qualifiedTitle;
        }

        UpdateSessionTabSizes();
        InvalidateSessionTabHeaders();
    }

    private bool _updatingSessionTabSizes;

    private void UpdateSessionTabSizes()
    {
        if (_updatingSessionTabSizes)
        {
            return;
        }

        int tabCount = _sessionTabs.TabPages.Count;
        if (tabCount == 0)
        {
            return;
        }

        _updatingSessionTabSizes = true;
        try
        {
            int maxTextWidth = 0;
            foreach (TabPage page in _sessionTabs.TabPages)
            {
                int textWidth = TextRenderer.MeasureText(
                    page.Text,
                    Font,
                    Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                if (textWidth > maxTextWidth)
                {
                    maxTextWidth = textWidth;
                }
            }

            int fullTabWidth = Math.Max(DefaultSessionTabWidth, maxTextWidth + SessionTabHorizontalChrome);
            int availableWidth = _sessionTabs.ClientSize.Width > 12
                ? _sessionTabs.ClientSize.Width - 8
                : _sessionPanel.ClientSize.Width - 8;
            int targetWidth = availableWidth > 0 && (fullTabWidth * tabCount) > availableWidth
                ? Math.Max(MinimumSessionTabWidth, availableWidth / tabCount)
                : fullTabWidth;
            int targetHeight = Math.Max(26, Font.Height + 10);
            Size targetSize = new(targetWidth, targetHeight);
            if (_sessionTabs.ItemSize != targetSize)
            {
                _sessionTabs.ItemSize = targetSize;
            }
        }
        finally
        {
            _updatingSessionTabSizes = false;
        }
    }

    private void InvalidateSessionTabHeaders()
    {
        if (!_sessionTabs.IsHandleCreated || _sessionTabs.TabPages.Count == 0)
        {
            return;
        }

        for (int index = 0; index < _sessionTabs.TabPages.Count; index++)
        {
            _sessionTabs.Invalidate(_sessionTabs.GetTabRect(index));
        }
    }

    private void DisconnectAllSessions()
    {
        foreach (RdpSession session in _sessions.Values.Where(IsSessionActive).ToArray())
        {
            try
            {
                session.Disconnect();
            }
            catch (Exception exception) when (
                exception is System.Runtime.InteropServices.COMException
                    or System.Runtime.InteropServices.InvalidComObjectException
                    or TargetInvocationException
                    or ObjectDisposedException
                    or InvalidOperationException)
            {
                SetStatus($"Falha ao desconectar {session.DisplayName}: {exception.InnerException?.Message ?? exception.Message}");
            }
        }
    }

    private void RemoveGuacamoleReferencesFor(XElement element)
    {
        foreach (IConnectionSource source in _sources)
        {
            if (source is GuacamoleInventorySource guacSource)
            {
                guacSource.RemoveReferencesFor(element);
            }
        }
    }

    internal void CloseSessionsForSource(Guid sourceId)
    {
        RdpSession[] sessions = _sessions.Values
            .Where(session => session.Context?.SourceId == sourceId)
            .ToArray();

        foreach (RdpSession session in sessions)
        {
            CloseSession(session);
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

    internal void CloseSession(RdpSession session)
    {
        try
        {
            if (IsSessionActive(session))
            {
                session.Disconnect();
            }
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException
                or TargetInvocationException
                or ObjectDisposedException
                or InvalidOperationException)
        {
            SetStatus($"Falha ao encerrar {session.DisplayName}: {exception.InnerException?.Message ?? exception.Message}");
        }

        session.DetachStateChanged(OnRdpConnectionStateChanged);
        _sessions.Remove(session.ServerElement);
        _sessionTabs.TabPages.Remove(session.Page);
        if (ReferenceEquals(_thumbnail.Image, session.Thumbnail))
        {
            _thumbnail.Image = null;
        }

        session.Dispose();
        if (_sessions.Count == 0 || !_sessions.Values.Any(candidate => candidate.SupportsThumbnail && IsSessionConnected(candidate)))
        {
            _thumbnailTimer.Stop();
        }

        if (_sessions.Count == 0)
        {
            _sessionPanel.Controls.Clear();
            _sessionPanel.Controls.Add(_sessionPlaceholder);
        }

        UpdateSessionTitles();
        UpdateConnectionViews();
        UpdateCommands();
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
            RequestWorkspaceSave();
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
        RequestWorkspaceSave();
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

    private void AutoResizeConnectionPane()
    {
        if (_workspaceSplit is null || _populatingTree || !_tree.IsHandleCreated)
        {
            return;
        }

        _expandedConnectionPaneWidth = CalculateOptimalConnectionPaneWidth();
        ClampConnectionPaneWidth();
    }

    private void ClampConnectionPaneWidth()
    {
        if (_workspaceSplit is null
            || _populatingTree
            || _connectionPaneMode == ConnectionPaneMode.Hidden
            || !_tree.Visible
            || !_tree.IsHandleCreated)
        {
            return;
        }

        int targetWidth = GetExpandedConnectionPaneWidth();
        if (_workspaceSplit.SplitterDistance != targetWidth)
        {
            _workspaceSplit.SplitterDistance = targetWidth;
        }
    }

    private int CalculateOptimalConnectionPaneWidth()
    {
        if (_tree.Nodes.Count == 0 || !_tree.IsHandleCreated)
        {
            return MinimumExpandedConnectionPaneWidth;
        }

        int maxNodeRight = 0;
        foreach (TreeNode node in GetVisibleTreeNodes(_tree.Nodes))
        {
            RdgNode? rdgNode = node.Tag as RdgNode;
            string displayText = rdgNode?.Name ?? node.Text;
            int textWidth = TextRenderer.MeasureText(
                displayText,
                _tree.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int statusWidth = rdgNode?.Kind == RdgNodeKind.Server
                ? 3 + StatusCircleDiameter + StatusCircleSpacing
                : 2;

            int nodeLeft = ((node.Level + 1) * _tree.Indent) + 20;
            int nodeRight = nodeLeft + statusWidth + textWidth + 8;
            if (nodeRight > maxNodeRight)
            {
                maxNodeRight = nodeRight;
            }
        }

        int chromeWidth = _workspaceSplit is not null
            ? _workspaceSplit.Panel1.Padding.Horizontal
                + (SystemInformation.BorderSize.Width * 4)
                + SystemInformation.VerticalScrollBarWidth
                + 12
            : 40;
        return Math.Max(MinimumExpandedConnectionPaneWidth, maxNodeRight + chromeWidth);
    }

    private static IEnumerable<TreeNode> GetVisibleTreeNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            if (node.IsExpanded)
            {
                foreach (TreeNode child in GetVisibleTreeNodes(node.Nodes))
                {
                    yield return child;
                }
            }
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
        MarkChanged(_activeSource, status);
    }

    private void MarkChanged(IConnectionSource? source, string status)
    {
        MarkDocumentModified(source);
        SetStatus(status);
    }

    private void MarkDocumentModified(IConnectionSource? source = null)
    {
        IConnectionSource? target = source ?? _activeSource;
        target?.MarkModified();
        if (target is not null)
        {
            UpdateSourceNodeText(target);
        }
        UpdateTitle();
        UpdateCommands();
    }

    private void UpdateTitle()
    {
        if (_sources.Count == 0)
        {
            Text = "PRDC";
            return;
        }

        if (_sources.Count == 1)
        {
            IConnectionSource source = _sources[0];
            string dirtyMarker = source.IsDirty ? " *" : string.Empty;
            Text = $"PRDC - {source.DisplayName}{dirtyMarker}";
            return;
        }

        int dirtyCount = _sources.Count(s => s.IsDirty);
        if (dirtyCount == 0)
        {
            Text = "PRDC - Workspace";
        }
        else if (dirtyCount == 1)
        {
            Text = "PRDC - Workspace [1 origem modificada]";
        }
        else
        {
            Text = $"PRDC - Workspace [{dirtyCount} origens modificadas]";
        }
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

    private void InitializeWorkspace()
    {
        _isRestoringWorkspace = true;
        try
        {
            LoadWorkspaceResult loadResult = WorkspaceManager.LoadWorkspace();
            _isWorkspaceRecoveryMode = loadResult.IsRecoveryMode;
            _currentWorkspaceId = loadResult.Workspace.WorkspaceId;

            if (loadResult.IsCorrupted)
            {
                SetStatus($"Workspace corrompido foi isolado ({Path.GetFileName(loadResult.CorruptedBackupPath)}). Novo workspace iniciado.");
            }
            else if (loadResult.IsRecoveryMode)
            {
                SetStatus(loadResult.ErrorMessage ?? "Workspace em modo de recuperação (somente-leitura).");
            }

            ApplyWorkspaceLayout(loadResult.Workspace.Layout);

            var loadedFavorites = FavoriteManager.ExtractFromWorkspace(loadResult.Workspace);
            _favoriteManager = new FavoriteManager(loadedFavorites);

            string workspaceFile = WorkspaceManager.WorkspaceFilePath;
            _workspaceLoadedFromDisk = File.Exists(workspaceFile);

            if (loadResult.Workspace.Sources.Count > 0)
            {
                ClearSources();

                foreach (WorkspaceSourceEntry entry in loadResult.Workspace.Sources)
                {
                    if (string.Equals(entry.Type, "RdgFile", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(entry.Path))
                        {
                            continue;
                        }

                        if (File.Exists(entry.Path))
                        {
                            try
                            {
                                RdgFileSource rdgSource = RdgFileSource.Load(entry.Path, entry.Id);
                                RegisterSource(rdgSource);
                            }
                            catch
                            {
                                RdgFileSource missingSource = RdgFileSource.CreateMissing(entry.Path, entry.Id);
                                RegisterSource(missingSource);
                            }
                        }
                        else
                        {
                            RdgFileSource missingSource = RdgFileSource.CreateMissing(entry.Path, entry.Id);
                            RegisterSource(missingSource);
                        }
                    }
                    else if (string.Equals(entry.Type, "GuacamolePortal", StringComparison.OrdinalIgnoreCase))
                    {
                        Guid portalId = entry.PortalId ?? entry.Id ?? Guid.Empty;
                        if (portalId == Guid.Empty)
                        {
                            continue;
                        }

                        string inventoryPath = GuacamolePortalManager.GetInventoryFilePath(portalId);
                        if (File.Exists(inventoryPath))
                        {
                            try
                            {
                                GuacamoleCacheLoadResult cacheLoad = Task.Run(async () =>
                                    await GuacamoleInventoryCache.LoadAsync(inventoryPath)).GetAwaiter().GetResult();

                                if (cacheLoad.IsSuccess && cacheLoad.CacheRoot is not null)
                                {
                                    GuacamoleImportResult materialized = GuacamoleInventoryCache.Materialize(cacheLoad.CacheRoot);
                                    string displayName = entry.DisplayName ?? "Portal Guacamole";
                                    string metaPath = GuacamolePortalManager.GetMetadataFilePath(portalId);
                                    if (File.Exists(metaPath))
                                    {
                                        try
                                        {
                                            string metaJson = File.ReadAllText(metaPath);
                                            var meta = JsonSerializer.Deserialize<GuacamolePortalMetadata>(metaJson);
                                            if (!string.IsNullOrWhiteSpace(meta?.DisplayName))
                                            {
                                                displayName = meta.DisplayName;
                                            }
                                        }
                                        catch { }
                                    }

                                    GuacamoleInventorySource guacSource = GuacamoleInventorySource.FromImportResult(
                                        materialized,
                                        entry.Id ?? portalId,
                                        displayName,
                                        portalId);
                                    RegisterSource(guacSource);
                                }
                                else
                                {
                                    GuacamoleInventorySource missingGuac = GuacamoleInventorySource.CreateMissing(
                                        portalId,
                                        entry.DisplayName,
                                        entry.Id);
                                    RegisterSource(missingGuac);
                                }
                            }
                            catch
                            {
                                GuacamoleInventorySource missingGuac = GuacamoleInventorySource.CreateMissing(
                                    portalId,
                                    entry.DisplayName,
                                    entry.Id);
                                RegisterSource(missingGuac);
                            }
                        }
                        else
                        {
                            GuacamoleInventorySource missingGuac = GuacamoleInventorySource.CreateMissing(
                                portalId,
                                entry.DisplayName,
                                entry.Id);
                            RegisterSource(missingGuac);
                        }
                    }
                }

                if (_sources.Count == 0)
                {
                    _untitledCounter = 1;
                    RdgFileSource initialSource = RdgFileSource.CreateNew($"Sem título {_untitledCounter}");
                    RegisterSource(initialSource);
                    _activeSource = initialSource;
                }
                else
                {
                    if (loadResult.Workspace.State.ActiveSourceId is { } activeId)
                    {
                        _activeSource = _sources.FirstOrDefault(s => s.Id == activeId) ?? _sources[0];
                    }
                    else
                    {
                        _activeSource = _sources[0];
                    }
                }
            }

            _searchCatalog.BuildCatalog(_sources, _favoriteManager);
        }
        finally
        {
            _isRestoringWorkspace = false;
        }
    }

    private void ApplyWorkspaceLayout(WorkspaceLayout layout)
    {
        if (layout.Window is { } window && window.Width.HasValue && window.Height.HasValue && window.X.HasValue && window.Y.HasValue)
        {
            Rectangle targetBounds = new(window.X.Value, window.Y.Value, window.Width.Value, window.Height.Value);
            if (WorkspaceManager.IsWindowBoundsValid(targetBounds))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = targetBounds;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1200, 760);
            }

            if (window.IsMaximized == true)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        if (layout.ConnectionPane?.Width is { } paneWidth && paneWidth >= 100)
        {
            _expandedConnectionPaneWidth = Math.Clamp(paneWidth, 100, Math.Max(200, Width - 300));
        }

        if (layout.SplitterDistance is { } splitterDistance && splitterDistance >= 100)
        {
            if (_workspaceSplit is not null)
            {
                int maxDistance = Math.Max(150, _workspaceSplit.Width - _workspaceSplit.Panel2MinSize);
                _workspaceSplit.SplitterDistance = Math.Clamp(splitterDistance, 100, maxDistance);
            }
        }

        if (layout.ConnectionPane?.IsVisible == false)
        {
            SetConnectionPaneMode(ConnectionPaneMode.Hidden);
        }
    }

    internal WorkspaceDocument CreateWorkspaceSnapshot()
    {
        WorkspaceDocument doc = new()
        {
            WorkspaceId = _currentWorkspaceId != Guid.Empty ? _currentWorkspaceId : Guid.NewGuid(),
            SchemaVersion = WorkspaceManager.CurrentSchemaVersion,
            LastModifiedUtc = DateTime.UtcNow,
            Sources = [],
            Layout = new WorkspaceLayout
            {
                Window = new WindowBounds
                {
                    X = WindowState == FormWindowState.Normal ? Location.X : RestoreBounds.X,
                    Y = WindowState == FormWindowState.Normal ? Location.Y : RestoreBounds.Y,
                    Width = WindowState == FormWindowState.Normal ? Size.Width : RestoreBounds.Width,
                    Height = WindowState == FormWindowState.Normal ? Size.Height : RestoreBounds.Height,
                    IsMaximized = WindowState == FormWindowState.Maximized
                },
                ConnectionPane = new ConnectionPaneLayout
                {
                    IsVisible = _connectionPaneMode != ConnectionPaneMode.Hidden,
                    Width = _expandedConnectionPaneWidth
                },
                SplitterDistance = _workspaceSplit?.SplitterDistance ?? _expandedConnectionPaneWidth
            },
            State = new WorkspaceState
            {
                ActiveSourceId = _activeSource?.Id
            }
        };

        foreach (IConnectionSource source in _sources)
        {
            if (source is RdgFileSource rdgSource)
            {
                string? filePath = rdgSource.Document.FilePath ?? rdgSource.MissingFilePath;
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    doc.Sources.Add(new WorkspaceSourceEntry
                    {
                        Type = "RdgFile",
                        Id = rdgSource.Id,
                        Path = filePath,
                        DisplayName = Path.GetFileName(filePath)
                    });
                }
            }
            else if (source is GuacamoleInventorySource guacSource)
            {
                doc.Sources.Add(new WorkspaceSourceEntry
                {
                    Type = "GuacamolePortal",
                    Id = guacSource.Id,
                    PortalId = guacSource.PortalId ?? guacSource.Id,
                    DisplayName = guacSource.DisplayName
                });
            }
        }

        FavoriteManager.ApplyToWorkspace(doc, _favoriteManager.Favorites);
        return doc;
    }

    internal void RequestWorkspaceSave()
    {
        if (_populatingTree || _isRestoringWorkspace || _isWorkspaceRecoveryMode)
        {
            return;
        }

        _workspaceSaveDebounceTimer.Stop();
        _workspaceSaveDebounceTimer.Start();
    }

    internal void SaveCurrentWorkspace()
    {
        _workspaceSaveDebounceTimer.Stop();

        if (_isWorkspaceRecoveryMode || _isRestoringWorkspace)
        {
            return;
        }

        try
        {
            WorkspaceDocument snapshot = CreateWorkspaceSnapshot();
            WorkspaceManager.SaveWorkspace(snapshot);
        }
        catch
        {
            // Falha não-fatal de salvamento silenciosa
        }
    }
}

internal enum ConnectionPaneMode
{
    Visible,
    Hidden,
    AutoHide
}

internal enum SessionKind
{
    NativeRdp,
    GuacamoleWeb
}

internal sealed class RdpSession : IDisposable
{
    public RdpSession(XElement serverElement, string displayName)
    {
        ServerElement = serverElement;
        DisplayName = displayName;
        Kind = SessionKind.NativeRdp;
        RdpHost = new RdpClientHost();
        Page = new TabPage(displayName) { Padding = new Padding(3) };
        Page.Controls.Add(RdpHost);
    }

    public RdpSession(
        XElement serverElement,
        string displayName,
        GuacamoleConnectionReference guacamoleReference,
        Guid? portalId = null)
    {
        ServerElement = serverElement;
        DisplayName = displayName;
        Kind = SessionKind.GuacamoleWeb;
        GuacamoleReference = guacamoleReference;
        GuacamoleHost = new GuacamoleSessionHost(guacamoleReference, portalId ?? guacamoleReference.PortalId)
        {
            Dock = DockStyle.Fill
        };
        Page = new TabPage(displayName) { Padding = new Padding(3) };
        Page.Controls.Add(GuacamoleHost);
    }

    public XElement ServerElement { get; }

    public SessionContext? Context { get; internal set; }

    public string DisplayName { get; set; }

    public SessionKind Kind { get; }

    public RdpClientHost? RdpHost { get; private set; }

    public GuacamoleSessionHost? GuacamoleHost { get; }

    public GuacamoleConnectionReference? GuacamoleReference { get; }

    public TabPage Page { get; }

    public Bitmap? Thumbnail { get; set; }

    public RdpConnectionState ConnectionState =>
        Kind == SessionKind.GuacamoleWeb
            ? GuacamoleHost!.ConnectionState
            : RdpHost!.ConnectionState;

    public bool SupportsLogOff => Kind == SessionKind.NativeRdp;

    public bool IsLogOffPending => Kind == SessionKind.NativeRdp && RdpHost!.IsLogOffPending;

    public bool SupportsThumbnail => Kind == SessionKind.NativeRdp;

    public bool OwnsHost(object? sender)
    {
        return Kind == SessionKind.GuacamoleWeb
            ? ReferenceEquals(GuacamoleHost, sender)
            : ReferenceEquals(RdpHost, sender);
    }

    public void AttachStateChanged(EventHandler<RdpConnectionStateChangedEventArgs> handler)
    {
        if (Kind == SessionKind.GuacamoleWeb)
        {
            GuacamoleHost!.ConnectionStateChanged += handler;
        }
        else
        {
            RdpHost!.ConnectionStateChanged += handler;
        }
    }

    public void DetachStateChanged(EventHandler<RdpConnectionStateChangedEventArgs> handler)
    {
        if (Kind == SessionKind.GuacamoleWeb)
        {
            GuacamoleHost!.ConnectionStateChanged -= handler;
        }
        else if (RdpHost is not null)
        {
            RdpHost.ConnectionStateChanged -= handler;
        }
    }

    public void Disconnect()
    {
        if (Kind == SessionKind.GuacamoleWeb)
        {
            GuacamoleHost!.DisconnectSession();
        }
        else
        {
            RdpHost!.DisconnectSession();
        }
    }

    public void ClearThumbnail()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }

    public void ReplaceNativeHost()
    {
        if (Kind != SessionKind.NativeRdp || RdpHost is null)
        {
            throw new InvalidOperationException("ReplaceNativeHost aplica-se apenas a sessões NativeRdp.");
        }

        RdpClientHost previous = RdpHost;
        Page.Controls.Remove(previous);
        RdpHost = new RdpClientHost();
        Page.Controls.Add(RdpHost);
        previous.Dispose();
        ClearThumbnail();
    }

    public void Dispose()
    {
        ClearThumbnail();
        if (Kind == SessionKind.GuacamoleWeb)
        {
            GuacamoleHost?.Dispose();
        }
        else
        {
            RdpHost?.Dispose();
        }

        Page.Dispose();
    }
}
