using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PRDC;

/// <summary>
/// Overlay flutuante não-modal de Busca Global (Quick Open - Ctrl+P).
/// Projetado com UX inspirada no VS Code, JetBrains e Windows Terminal:
/// borda limpa, centralizado no topo, pesquisa em tempo real, suporte a temas Claro/Escuro
/// e navegação 100% por teclado.
/// </summary>
internal sealed class QuickOpenOverlayForm : Form
{
    private readonly SearchCatalogManager _catalog;
    private readonly TextBox _searchBox;
    private readonly ListBox _resultsList;
    private readonly Panel _searchPanel;
    private readonly Panel _footerPanel;
    private readonly Label _footerLabel;

    private readonly List<GlobalSearchResult> _currentResults = [];
    private bool _isClosing;

    /// <summary>
    /// Disparado quando o usuário aciona a ação primária (Enter) para conectar ao servidor.
    /// </summary>
    public event Action<GlobalSearchEntry>? ConnectRequested;

    /// <summary>
    /// Disparado quando o usuário aciona a ação secundária (Ctrl+Enter) para navegar até a origem.
    /// </summary>
    public event Action<GlobalSearchEntry>? GoToSourceRequested;

    internal TextBox SearchBox => _searchBox;
    internal ListBox ResultsList => _resultsList;
    internal IReadOnlyList<GlobalSearchResult> CurrentResults => _currentResults;

    public QuickOpenOverlayForm(SearchCatalogManager catalog, Form? owner = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;

        if (owner is not null)
        {
            Owner = owner;
        }

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = true;

        Width = 660;
        Height = 220; // Ajustado dinamicamente com base nos resultados

        // 1. Painel Superior da Caixa de Busca
        _searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 10, 12, 6)
        };

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 12f, FontStyle.Regular),
            Text = string.Empty
        };
        _searchBox.TextChanged += (_, _) => OnSearchTextChanged();
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        _searchPanel.Controls.Add(_searchBox);

        // 2. Painel Inferior de Ajuda / Atalhos
        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(12, 4, 12, 4)
        };

        _footerLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            Text = "↵ Conectar   |   Ctrl+↵ Ir para a origem   |   Esc Fechar",
            TextAlign = ContentAlignment.MiddleLeft
        };
        _footerPanel.Controls.Add(_footerLabel);

        // 3. Lista de Resultados com OwnerDraw
        _resultsList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 36,
            IntegralHeight = false
        };
        _resultsList.DrawItem += OnDrawResultItem;
        _resultsList.DoubleClick += (_, _) => ExecutePrimaryAction();
        _resultsList.KeyDown += OnResultsListKeyDown;

        Controls.Add(_resultsList);
        Controls.Add(_footerPanel);
        Controls.Add(_searchPanel);

        // Eventos do formulário
        Deactivate += (_, _) => CloseOverlay();
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyCurrentTheme();

        // Posiciona na tela em relação ao Owner
        RepositionInOwner();

        // Executa busca inicial vazia para apresentar favoritos
        PerformSearch(string.Empty);
    }

    /// <summary>
    /// Reposiciona o overlay centralizado horizontalmente e 60px abaixo do topo do formulário pai.
    /// </summary>
    public void RepositionInOwner()
    {
        if (Owner is not null && !Owner.IsDisposed)
        {
            int targetWidth = Math.Min(680, Math.Max(500, Owner.Width - 60));
            Width = targetWidth;

            int left = Owner.Left + (Owner.Width - Width) / 2;
            int top = Owner.Top + 60;

            Screen screen = Screen.FromControl(Owner);
            left = Math.Max(screen.WorkingArea.Left + 8, Math.Min(left, screen.WorkingArea.Right - Width - 8));
            top = Math.Max(screen.WorkingArea.Top + 8, top);

            Location = new Point(left, top);
        }
    }

    /// <summary>
    /// Executa a pesquisa e atualiza a lista de resultados em tempo real.
    /// </summary>
    internal void PerformSearch(string query)
    {
        _currentResults.Clear();
        var searchResults = _catalog.Search(query, limit: 20);
        _currentResults.AddRange(searchResults);

        _resultsList.BeginUpdate();
        _resultsList.Items.Clear();

        if (_currentResults.Count == 0)
        {
            _resultsList.Items.Add(string.IsNullOrWhiteSpace(query)
                ? "(Nenhum favorito cadastrado. Digite para pesquisar...)"
                : "(Nenhum servidor encontrado)");
        }
        else
        {
            foreach (var res in _currentResults)
            {
                _resultsList.Items.Add(res);
            }
            _resultsList.SelectedIndex = 0;
        }

        _resultsList.EndUpdate();
        AdjustOverlayHeight();
    }

    /// <summary>
    /// Move a seleção para o próximo resultado.
    /// </summary>
    internal void SelectNext(int step = 1)
    {
        if (_resultsList.Items.Count == 0 || _currentResults.Count == 0) return;
        int next = Math.Min(_resultsList.Items.Count - 1, _resultsList.SelectedIndex + step);
        _resultsList.SelectedIndex = next;
    }

    /// <summary>
    /// Move a seleção para o resultado anterior.
    /// </summary>
    internal void SelectPrevious(int step = 1)
    {
        if (_resultsList.Items.Count == 0 || _currentResults.Count == 0) return;
        int prev = Math.Max(0, _resultsList.SelectedIndex - step);
        _resultsList.SelectedIndex = prev;
    }

    /// <summary>
    /// Executa a ação primária (Enter): conectar diretamente ao servidor selecionado.
    /// </summary>
    internal void ExecutePrimaryAction()
    {
        if (_resultsList.SelectedItem is GlobalSearchResult res)
        {
            GlobalSearchEntry entry = res.Entry;
            CloseOverlay();
            ConnectRequested?.Invoke(entry);
        }
    }

    /// <summary>
    /// Executa a ação secundária (Ctrl+Enter): navegar até o nó de origem na árvore.
    /// </summary>
    internal void ExecuteSecondaryAction()
    {
        if (_resultsList.SelectedItem is GlobalSearchResult res)
        {
            GlobalSearchEntry entry = res.Entry;
            CloseOverlay();
            GoToSourceRequested?.Invoke(entry);
        }
    }

    internal void CloseOverlay()
    {
        if (_isClosing) return;
        _isClosing = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            CloseOverlay();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Enter))
        {
            ExecuteSecondaryAction();
            return true;
        }

        if (keyData == Keys.Enter)
        {
            ExecutePrimaryAction();
            return true;
        }

        if (keyData == Keys.Down)
        {
            SelectNext(1);
            return true;
        }

        if (keyData == Keys.Up)
        {
            SelectPrevious(1);
            return true;
        }

        if (keyData == Keys.PageDown)
        {
            SelectNext(6);
            return true;
        }

        if (keyData == Keys.PageUp)
        {
            SelectPrevious(6);
            return true;
        }

        if (keyData == Keys.Home && _resultsList.Focused)
        {
            if (_resultsList.Items.Count > 0) _resultsList.SelectedIndex = 0;
            return true;
        }

        if (keyData == Keys.End && _resultsList.Focused)
        {
            if (_resultsList.Items.Count > 0) _resultsList.SelectedIndex = _resultsList.Items.Count - 1;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnSearchTextChanged()
    {
        PerformSearch(_searchBox.Text);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up or Keys.PageDown or Keys.PageUp or Keys.Enter or Keys.Escape)
        {
            e.Handled = true;
        }
    }

    private void OnResultsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Escape)
        {
            e.Handled = true;
        }
    }

    private void AdjustOverlayHeight()
    {
        int itemsToShow = Math.Min(10, Math.Max(2, _resultsList.Items.Count));
        int calculatedHeight = _searchPanel.Height + _footerPanel.Height + (itemsToShow * _resultsList.ItemHeight) + 4;
        Height = Math.Min(420, calculatedHeight);
        Invalidate();
    }

    private void OnDrawResultItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _resultsList.Items.Count) return;

        bool isSelected = (e.State & DrawItemState.Selected) != 0;
        bool isDark = ThemeManager.IsDark;

        Color bg = isSelected
            ? (isDark ? Color.FromArgb(9, 71, 113) : Color.FromArgb(204, 232, 255))
            : (isDark ? Color.FromArgb(32, 33, 36) : Color.White);

        Color textPrimary = isSelected
            ? (isDark ? Color.White : Color.Black)
            : (isDark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(32, 33, 36));

        Color textSecondary = isSelected
            ? (isDark ? Color.FromArgb(200, 220, 240) : Color.FromArgb(60, 60, 60))
            : (isDark ? Color.FromArgb(154, 160, 166) : Color.FromArgb(95, 99, 104));

        Color starColor = Color.FromArgb(234, 179, 8); // Âmbar Dourado

        using SolidBrush bgBrush = new(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        // Se for mensagem de vazio
        if (_resultsList.Items[e.Index] is string emptyMessage)
        {
            using SolidBrush emptyBrush = new(textSecondary);
            using Font italicFont = new("Segoe UI", 9.5f, FontStyle.Italic);
            e.Graphics.DrawString(emptyMessage, italicFont, emptyBrush, e.Bounds.Left + 16, e.Bounds.Top + 9);
            return;
        }

        if (_resultsList.Items[e.Index] is not GlobalSearchResult res) return;
        GlobalSearchEntry entry = res.Entry;

        int currentX = e.Bounds.Left + 14;
        int centerY = e.Bounds.Top + 8;

        // 1. Indicador de Favorito (Estrela)
        if (entry.IsFavorite)
        {
            using SolidBrush starBrush = new(starColor);
            using Font starFont = new("Segoe UI", 11f, FontStyle.Bold);
            e.Graphics.DrawString("★", starFont, starBrush, currentX, centerY - 1);
            currentX += 22;
        }

        // 2. Nome de Exibição do Servidor
        using Font titleFont = new("Segoe UI", 10f, FontStyle.Bold);
        using SolidBrush titleBrush = new(textPrimary);
        string name = entry.DisplayName;
        e.Graphics.DrawString(name, titleFont, titleBrush, currentX, centerY);
        SizeF nameSize = e.Graphics.MeasureString(name, titleFont);
        currentX += (int)nameSize.Width + 8;

        // 3. Origem Qualificada entre Colchetes: [Origem]
        using Font originFont = new("Segoe UI", 9f, FontStyle.Regular);
        using SolidBrush originBrush = new(textSecondary);
        string originText = $"[{entry.SourceDisplayName}]";
        e.Graphics.DrawString(originText, originFont, originBrush, currentX, centerY + 1);
        SizeF originSize = e.Graphics.MeasureString(originText, originFont);
        currentX += (int)originSize.Width + 8;

        // 4. IP ou Caminho Hierárquico no lado direito (se houver espaço)
        string extraInfo = !string.IsNullOrWhiteSpace(entry.HostName) ? entry.HostName : entry.NodePath;
        if (!string.IsNullOrWhiteSpace(extraInfo) && currentX < e.Bounds.Right - 80)
        {
            using Font pathFont = new("Segoe UI", 8.5f, FontStyle.Regular);
            int extraWidth = (int)e.Graphics.MeasureString(extraInfo, pathFont).Width;
            int rightX = Math.Max(currentX, e.Bounds.Right - extraWidth - 14);
            Rectangle extraBounds = new(rightX, centerY + 2, extraWidth + 4, e.Bounds.Height);
            e.Graphics.DrawString(extraInfo, pathFont, originBrush, extraBounds);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Borda externa de 1px
        Color borderColor = ThemeManager.IsDark
            ? Color.FromArgb(64, 64, 64)
            : Color.FromArgb(200, 200, 200);

        using Pen borderPen = new(borderColor, 1);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        // Divisor sob o campo de pesquisa
        int dividerY = _searchPanel.Height;
        using Pen divPen = new(borderColor, 1);
        e.Graphics.DrawLine(divPen, 1, dividerY, Width - 2, dividerY);

        // Divisor acima do rodapé
        int footerDivY = Height - _footerPanel.Height - 1;
        e.Graphics.DrawLine(divPen, 1, footerDivY, Width - 2, footerDivY);
    }

    private void OnThemeChanged()
    {
        if (IsDisposed || Disposing) return;
        ApplyCurrentTheme();
        Invalidate(true);
    }

    private void ApplyCurrentTheme()
    {
        bool isDark = ThemeManager.IsDark;

        Color formBg = isDark ? Color.FromArgb(32, 33, 36) : Color.White;
        Color textPrimary = isDark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(32, 33, 36);
        Color textSecondary = isDark ? Color.FromArgb(154, 160, 166) : Color.FromArgb(95, 99, 104);
        Color footerBg = isDark ? Color.FromArgb(28, 29, 32) : Color.FromArgb(246, 248, 250);

        BackColor = formBg;
        _searchPanel.BackColor = formBg;
        _searchBox.BackColor = formBg;
        _searchBox.ForeColor = textPrimary;

        _resultsList.BackColor = formBg;
        _resultsList.ForeColor = textPrimary;

        _footerPanel.BackColor = footerBg;
        _footerLabel.BackColor = footerBg;
        _footerLabel.ForeColor = textSecondary;
    }
}
