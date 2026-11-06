using System.Drawing;
using System.Windows.Forms;

namespace PRDC;

internal sealed class FavoriteAliasDialog : Form
{
    private readonly TextBox _aliasTextBox = new() { Dock = DockStyle.Fill };

    public string? AliasText => string.IsNullOrWhiteSpace(_aliasTextBox.Text) ? null : _aliasTextBox.Text.Trim();

    public FavoriteAliasDialog(string originalDisplayName, string? currentAlias = null)
    {
        Text = "Renomear Favorito";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _aliasTextBox.Text = currentAlias ?? originalDisplayName;
        _aliasTextBox.SelectAll();

        Label promptLabel = new()
        {
            Text = $"Defina um apelido para o favorito.\n(Deixe em branco para usar o nome original '{originalDisplayName}')",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        Button okButton = new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };

        Button cancelButton = new()
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            AutoSize = true
        };

        FlowLayoutPanel buttonPanel = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttonPanel.Controls.AddRange([cancelButton, okButton]);

        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(_aliasTextBox, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        ThemeManager.ApplyTheme(this);
    }
}

